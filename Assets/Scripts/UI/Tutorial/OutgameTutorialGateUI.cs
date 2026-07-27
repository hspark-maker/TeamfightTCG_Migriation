using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 아웃게임 튜토리얼 강제 게이트 UI(스텝·세이브·앵커를 모르는 순수 표시 컴포넌트).
// 4패널로 타깃 영역만 비우는 이유: 구멍이 물리적 공백이라 클릭 통과가 셰이더·ICanvasRaycastFilter 0줄로 성립한다.
// 완료 판정은 타깃 버튼 onClick 구독으로만 — 기존 리스너 무접촉이라 원래 동작이 그대로 실행된다.
// 불변식: 딤이 걸린 채 누를 수 있는 것이 하나도 없는 상태를 만들지 않는다(RefreshVisibility 참조).
public class OutgameTutorialGateUI : MonoBehaviour
{
    public static OutgameTutorialGateUI Instance { get; private set; }

    const float HolePadding   = 12f;    // 구멍 여유(타깃 테두리가 딤에 물리지 않게)
    const float BannerMargin  = 36f;    // 구멍과 배너 사이 간격
    const float BannerWidth   = 900f;
    const float BannerHeight  = 200f;
    const float BannerBottom  = 220f;   // 배너 전용 모드의 하단 여백(화면 중앙 연출을 가리지 않게)

    RectTransform     m_canvasRect;
    GameObject        m_gateRoot;
    RectTransform[]   m_panels = new RectTransform[4];   // 0=Top 1=Bottom 2=Left 3=Right
    RectTransform     m_bannerRect;
    TextMeshProUGUI   m_bannerText;

    readonly Vector3[] m_corners = new Vector3[4];       // GetWorldCorners 재사용 버퍼

    RectTransform m_target;
    Button        m_targetButton;
    Canvas        m_targetCanvas;      // 타깃 캔버스(Overlay면 스크린 변환에 카메라 null)
    Action        m_onSatisfied;
    bool          m_armed;             // 게이트 진행 중(타깃 파괴 감지에 사용)
    bool          m_satisfied;         // 중복 클릭 가드 — 콜백은 1회만
    bool          m_blockWarned;       // 누를 수 없는 타깃 경고 1회(매 프레임 스팸 방지)

    /// <summary>게이트 UI를 1회 생성. 이미 있으면 재사용.</summary>
    public static OutgameTutorialGateUI Ensure()
    {
        if (Instance != null) return Instance;
        var t_go = new GameObject("OutgameTutorialGate");
        return Instance = t_go.AddComponent<OutgameTutorialGateUI>();
    }

    /// <summary>타깃만 남기는 게이트를 건다(_onSatisfied는 1회만 호출).
    /// 버튼이 없으면 소프트락이므로 게이트를 걸지 않는다.
    /// _onSatisfied가 null이면 클릭을 완료로 보지 않는다 — 딤만 유지하고 완료는 호출자가 다른 신호로 판정한다
    /// (구매처럼 눌러도 실패할 수 있는 스텝).</summary>
    public void ShowGate(RectTransform _target, Button _targetButton, string _message, Action _onSatisfied)
    {
        if (_target == null)
        {
            Debug.LogWarning("[OutgameTutorialGateUI] 타깃 RectTransform이 없어 게이트를 걸지 않습니다.");
            HideGate();
            return;
        }
        if (_targetButton == null)
        {
            Debug.LogWarning($"[OutgameTutorialGateUI] 타깃 '{_target.name}'에 Button이 없어 게이트를 걸지 않습니다(소프트락 방지).");
            HideGate();
            return;
        }

        DetachButton();

        m_target       = _target;
        m_targetButton = _targetButton;
        m_targetCanvas = _target.GetComponentInParent<Canvas>();
        m_onSatisfied  = _onSatisfied;
        m_satisfied    = false;
        m_blockWarned  = false;
        m_armed        = true;

        if (_onSatisfied != null) m_targetButton.onClick.AddListener(OnTargetClicked);

        SetBanner(_message);
        RefreshVisibility();   // 첫 프레임 깜빡임 방지(LateUpdate 이전에 1회)
    }

    /// <summary>딤 없이 안내 배너만 띄운다. 3D 팩처럼 uGUI 구멍을 뚫을 수 없는 타깃용
    /// (Overlay 딤을 깔면 3D 오브젝트가 그 아래로 가려져 강조가 아니라 은폐가 된다).</summary>
    public void ShowBanner(string _message)
    {
        DetachButton();
        m_target       = null;
        m_targetCanvas = null;
        m_onSatisfied  = null;
        m_satisfied    = false;
        m_blockWarned  = false;
        m_armed        = false;   // 추종할 구멍이 없다 → LateUpdate 미개입

        if (string.IsNullOrEmpty(_message)) { HideGate(); return; }

        for (int t_i = 0; t_i < m_panels.Length; t_i++) SetPanel(m_panels[t_i], 0f, 0f, 0f, 0f);

        SetBanner(_message);
        m_bannerRect.anchoredPosition =
            new Vector2(0f, m_canvasRect.rect.yMin + m_bannerRect.sizeDelta.y * 0.5f + BannerBottom);

        m_gateRoot.SetActive(true);
    }

    /// <summary>딤을 숨기고 리스너를 해제한다(앵커 미등장 시 대기 상태). 콜백은 유지되지 않는다.</summary>
    public void HideGate()
    {
        DetachButton();
        m_target       = null;
        m_targetCanvas = null;
        m_armed        = false;
        m_blockWarned  = false;
        if (m_gateRoot != null) m_gateRoot.SetActive(false);
    }

    /// <summary>게이트 전체 초기화(콜백·완료 가드 포함).</summary>
    public void Clear()
    {
        HideGate();
        m_onSatisfied = null;
        m_satisfied   = false;
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildUI();
    }

    void OnDestroy()
    {
        DetachButton();   // 리스너 누수 = 다음 스텝 오발화
        if (Instance == this) Instance = null;
    }

    // 타깃이 레이아웃 애니메이션·스크롤로 움직여도 구멍이 따라가도록 매 프레임 재계산.
    void LateUpdate()
    {
        if (!m_armed) return;
        if (m_target == null) { HideGate(); return; }   // 타깃 파괴(씬 전환 등) 시 화면 잠김 방지

        RefreshVisibility();
    }

    // 딤은 "타깃을 실제로 누를 수 있을 때"만 띄운다. 꺼져 있거나(탭 전환) interactable=false면(팩 SO 미배선·
    // 골드 잠금 등) 딤만 숨기고 게이트는 유지 — 그러지 않으면 화면 전면 차단 + 탈출 수단 0이 된다.
    // 다시 누를 수 있게 되면 자동 복귀하므로 ShowGate 시점 판정만으로 끝내지 않는다.
    void RefreshVisibility()
    {
        bool t_active    = m_target.gameObject.activeInHierarchy;
        bool t_clickable = m_targetButton != null && m_targetButton.IsInteractable();
        bool t_visible   = t_active && t_clickable;

        if (m_gateRoot.activeSelf != t_visible) m_gateRoot.SetActive(t_visible);

        if (!t_visible)
        {
            // 보이는데 못 누르는 건 배선 실수 신호 — 탭 전환(비활성)은 정상이라 경고하지 않는다.
            if (t_active && !m_blockWarned)
            {
                m_blockWarned = true;
                Debug.LogWarning($"[OutgameTutorialGateUI] 타깃 '{m_target.name}'의 버튼이 비활성(interactable=false)이라 딤을 숨기고 대기합니다(소프트락 방지).");
            }
            return;
        }

        m_blockWarned = false;
        Layout();
    }

    // 타깃 월드 코너 → 스크린 → 게이트 캔버스 로컬로 변환해 구멍을 잡고 4패널·배너를 배치.
    void Layout()
    {
        Rect t_full = m_canvasRect.rect;
        Camera t_targetCam = (m_targetCanvas != null && m_targetCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            ? m_targetCanvas.worldCamera
            : null;

        m_target.GetWorldCorners(m_corners);

        var t_min = new Vector2(float.MaxValue, float.MaxValue);
        var t_max = new Vector2(float.MinValue, float.MinValue);
        for (int t_i = 0; t_i < 4; t_i++)
        {
            Vector2 t_screen = RectTransformUtility.WorldToScreenPoint(t_targetCam, m_corners[t_i]);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(m_canvasRect, t_screen, null, out Vector2 t_local)) continue;
            t_min = Vector2.Min(t_min, t_local);
            t_max = Vector2.Max(t_max, t_local);
        }

        t_min -= Vector2.one * HolePadding;
        t_max += Vector2.one * HolePadding;

        // 화면 밖으로 나간 부분은 잘라낸다(패널이 캔버스를 넘어가 뒤집히지 않게).
        t_min.x = Mathf.Max(t_min.x, t_full.xMin);
        t_min.y = Mathf.Max(t_min.y, t_full.yMin);
        t_max.x = Mathf.Min(t_max.x, t_full.xMax);
        t_max.y = Mathf.Min(t_max.y, t_full.yMax);

        // 구멍이 화면 밖이면 상단 패널 하나로 전부 덮는다.
        if (t_max.x <= t_min.x || t_max.y <= t_min.y)
        {
            SetPanel(m_panels[0], t_full.xMin, t_full.yMin, t_full.xMax, t_full.yMax);
            SetPanel(m_panels[1], 0f, 0f, 0f, 0f);
            SetPanel(m_panels[2], 0f, 0f, 0f, 0f);
            SetPanel(m_panels[3], 0f, 0f, 0f, 0f);
            PlaceBanner(t_full, 0f, 0f);
            return;
        }

        SetPanel(m_panels[0], t_full.xMin, t_max.y, t_full.xMax, t_full.yMax);   // Top
        SetPanel(m_panels[1], t_full.xMin, t_full.yMin, t_full.xMax, t_min.y);   // Bottom
        SetPanel(m_panels[2], t_full.xMin, t_min.y, t_min.x, t_max.y);           // Left
        SetPanel(m_panels[3], t_max.x, t_min.y, t_full.xMax, t_max.y);           // Right

        PlaceBanner(t_full, t_min.y, t_max.y);
    }

    void SetPanel(RectTransform _rect, float _xMin, float _yMin, float _xMax, float _yMax)
    {
        _rect.sizeDelta        = new Vector2(Mathf.Max(0f, _xMax - _xMin), Mathf.Max(0f, _yMax - _yMin));
        _rect.anchoredPosition = new Vector2((_xMin + _xMax) * 0.5f, (_yMin + _yMax) * 0.5f);
    }

    // 구멍을 가리지 않는 쪽에 배너를 둔다 — 구멍이 상단이면 아래, 하단이면 위.
    void PlaceBanner(Rect _full, float _holeYMin, float _holeYMax)
    {
        if (!m_bannerRect.gameObject.activeSelf) return;

        float t_half   = m_bannerRect.sizeDelta.y * 0.5f;
        float t_center = (_holeYMin + _holeYMax) * 0.5f;
        float t_y = t_center > 0f
            ? _holeYMin - BannerMargin - t_half
            : _holeYMax + BannerMargin + t_half;

        t_y = Mathf.Clamp(t_y, _full.yMin + t_half + BannerMargin, _full.yMax - t_half - BannerMargin);
        m_bannerRect.anchoredPosition = new Vector2(0f, t_y);
    }

    void SetBanner(string _message)
    {
        bool t_has = !string.IsNullOrEmpty(_message);
        m_bannerRect.gameObject.SetActive(t_has);
        if (t_has) m_bannerText.text = _message;
    }

    // 원래 동작은 그대로 실행되고(기존 리스너 무접촉) 콜백만 1회 얹는다.
    void OnTargetClicked()
    {
        if (m_satisfied) return;
        m_satisfied = true;

        Action t_callback = m_onSatisfied;
        HideGate();          // 콜백이 다음 게이트를 걸 수 있도록 먼저 정리
        m_onSatisfied = null;
        t_callback?.Invoke();
    }

    void DetachButton()
    {
        if (m_targetButton != null) m_targetButton.onClick.RemoveListener(OnTargetClicked);
        m_targetButton = null;
    }

    // ── 코드 빌드 캔버스 ──────────────────────────────────────────────────────
    void BuildUI()
    {
        var t_canvasGo = new GameObject("OutgameTutorialGateCanvas");
        t_canvasGo.transform.SetParent(transform, false);
        var t_canvas = t_canvasGo.AddComponent<Canvas>();
        t_canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        // 전투 오버레이(TutorialOverlayUI 200)보다 위. 단 UIPoolManager 팝업 캔버스는 씬에서 order 1이라
        // 게이트 중에 뜬 팝업은 딤에 가려진다 — 팝업 스텝을 넣으려면 그 캔버스를 이 값 위로 올려야 한다.
        t_canvas.sortingOrder = 300;
        var t_scaler = t_canvasGo.AddComponent<CanvasScaler>();
        t_scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        t_scaler.referenceResolution = new Vector2(1080f, 1920f);
        t_canvasGo.AddComponent<GraphicRaycaster>();
        EnsureEventSystem();

        m_canvasRect = t_canvasGo.GetComponent<RectTransform>();

        m_gateRoot = new GameObject("Gate");
        m_gateRoot.transform.SetParent(t_canvasGo.transform, false);
        var t_rootRect = m_gateRoot.AddComponent<RectTransform>();
        t_rootRect.anchorMin = Vector2.zero;
        t_rootRect.anchorMax = Vector2.one;
        t_rootRect.offsetMin = t_rootRect.offsetMax = Vector2.zero;

        string[] t_names = { "PanelTop", "PanelBottom", "PanelLeft", "PanelRight" };
        for (int t_i = 0; t_i < 4; t_i++) m_panels[t_i] = CreatePanel(t_names[t_i], t_rootRect);

        BuildBanner(t_rootRect);   // 패널보다 뒤에 생성 = 위에 그려짐

        m_gateRoot.SetActive(false);
    }

    // 딤 패널. raycastTarget=true라 타깃 외 입력을 전부 흡수한다(구멍만 통과).
    RectTransform CreatePanel(string _name, RectTransform _parent)
    {
        var t_go = new GameObject(_name);
        t_go.transform.SetParent(_parent, false);
        var t_image = t_go.AddComponent<Image>();
        t_image.color         = new Color(0f, 0f, 0f, 0.72f);
        t_image.raycastTarget = true;

        var t_rect = t_image.rectTransform;
        t_rect.anchorMin = t_rect.anchorMax = t_rect.pivot = new Vector2(0.5f, 0.5f);
        t_rect.sizeDelta = Vector2.zero;
        return t_rect;
    }

    void BuildBanner(RectTransform _parent)
    {
        var t_bgGo = new GameObject("Banner");
        t_bgGo.transform.SetParent(_parent, false);
        var t_bg = t_bgGo.AddComponent<Image>();
        t_bg.color         = new Color(0f, 0f, 0f, 0.85f);
        t_bg.raycastTarget = false;   // 배너는 입력 무흡수(차단은 패널 담당)

        m_bannerRect = t_bg.rectTransform;
        m_bannerRect.anchorMin = m_bannerRect.anchorMax = m_bannerRect.pivot = new Vector2(0.5f, 0.5f);
        m_bannerRect.sizeDelta = new Vector2(BannerWidth, BannerHeight);

        var t_txtGo = new GameObject("Text");
        t_txtGo.transform.SetParent(t_bgGo.transform, false);
        m_bannerText = t_txtGo.AddComponent<TextMeshProUGUI>();
        TutorialUIStyle.ApplyFont(m_bannerText);
        m_bannerText.fontSize            = 46f;
        m_bannerText.color               = Color.white;
        m_bannerText.alignment           = TextAlignmentOptions.Center;
        m_bannerText.enableWordWrapping  = true;
        m_bannerText.raycastTarget       = false;

        var t_txtRect = m_bannerText.rectTransform;
        t_txtRect.anchorMin = Vector2.zero;
        t_txtRect.anchorMax = Vector2.one;
        t_txtRect.offsetMin = new Vector2(32f, 20f);
        t_txtRect.offsetMax = new Vector2(-32f, -20f);

        t_bgGo.SetActive(false);
    }

    static void EnsureEventSystem()
    {
        if (UnityEngine.Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() != null) return;
        var t_es = new GameObject("EventSystem");
        t_es.AddComponent<UnityEngine.EventSystems.EventSystem>();
        t_es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
    }
}

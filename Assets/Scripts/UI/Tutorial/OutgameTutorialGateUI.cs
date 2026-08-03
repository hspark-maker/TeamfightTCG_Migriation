using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 아웃게임 튜토리얼 강제 게이트 UI(스텝·세이브·앵커를 모르는 순수 표시 컴포넌트).
// 전체화면 딤 1장이 입력을 전부 흡수하고, 타깃만 중첩 Canvas로 딤 위에 승격해 선명하게 보이고 눌리게 한다.
// 완료 판정은 타깃 버튼 onClick 구독으로만 — 기존 리스너 무접촉이라 원래 동작이 그대로 실행된다.
//
// 불변식 2개:
//  (1) 딤 표시 == 타깃 승격 == 포인터 표시. 뒤집는 곳은 RefreshVisibility 하나뿐이다.
//  (2) 딤이 걸린 채 누를 수 있는 것이 하나도 없는 상태를 만들지 않는다.
//
// 메시지 모드(ShowMessageGate)만 (1)의 예외다 — 누를 대상이 아니라 읽을 영역이라 승격·손가락 없이 딤+링만 켠다.
// 완료가 딤 탭 자체라 (2)는 오히려 항상 만족한다.
//
// 룩은 프리팹(OutgameTutorialGate.prefab)에서 저작한다. 미배선이면 딤+문구만 코드로 그리는 폴백으로 떨어진다
// — 링·손가락 스프라이트가 Resources 밖에 있어 코드 경로에서는 얻을 방법이 없다.
public class OutgameTutorialGateUI : MonoBehaviour
{
    public static OutgameTutorialGateUI Instance { get; private set; }

    // 정렬 불변식: TutorialOverlay(200) < 딤(350) < 타깃(351) < 안내 요소(352) < UIPoolManager 팝업(400) < Mulligan(999) < LoadingCover(1000).
    // 400을 넘기면 안 된다 — 플레이 스텝의 "유효한 덱이 없습니다"(LobbyMatchLauncher)와 구매 실패 팝업
    // (PackShowcaseController)이 딤에 묻히는데, 그때 타깃 버튼은 interactable=true 그대로라 아래 탈출로도 발동하지 않는다.
    //
    // 링·손가락·문구가 타깃보다 위인 이유: 딤 위로 승격된 타깃이 자기 위에 겹친 안내를 그대로 덮어버린다.
    // 딤만 타깃 아래에 남는다 — 딤까지 올리면 타깃이 다시 묻혀 누를 수 없게 된다.
    const int GateOrder     = 350;
    const int TargetOrder   = GateOrder + 1;
    const int OrnamentOrder = TargetOrder + 1;

    [Header("표시 요소 (blocker 미배선 = 코드 폴백)")]
    [SerializeField] Image           blocker;       // 전체화면 딤 겸 입력 흡수막
    [SerializeField] RectTransform   focusRing;     // 9슬라이스 포커스 링(옵션)
    [SerializeField] RectTransform   hand;          // 손가락 포인터(옵션)
    [SerializeField] RectTransform   messageRect;   // 안내 문구 프레임
    [SerializeField] TextMeshProUGUI messageText;

    [Header("배치")]
    [Tooltip("링이 타깃보다 얼마나 큰가")]
    [SerializeField] float   ringPadding   = 24f;
    [Tooltip("손끝 미세 보정. 기본 위치는 '타깃 중앙 + 손 크기의 절반'이라 스프라이트 여백만큼만 더 보정하면 된다")]
    [SerializeField] Vector2 handOffset    = Vector2.zero;
    [Tooltip("타깃과 문구 사이 간격")]
    [SerializeField] float   messageMargin = 36f;
    [Tooltip("문구 전용 모드(타깃 없음)의 하단 여백")]
    [SerializeField] float   messageBottom = 220f;

    [Header("포인터 연출")]
    [SerializeField] float pulseScale    = 1.08f;
    [SerializeField] float pulseDuration = 0.6f;

    RectTransform m_canvasRect;
    Canvas        m_gateCanvas;   // 중첩 Canvas가 리셋하는 정렬 레이어·셰이더 채널을 복사해 올 원본
    GameObject    m_gateRoot;

    readonly Vector3[] m_corners = new Vector3[4];   // GetWorldCorners 재사용 버퍼

    RectTransform m_target;
    Button        m_targetButton;
    Canvas        m_targetCanvas;      // 승격 전에 잡아 둔 타깃의 원래 캔버스(스크린 변환·루트 조회에 쓴다)
    Action        m_onSatisfied;
    bool          m_armed;             // 게이트 진행 중(타깃 파괴 감지에 사용)
    bool          m_satisfied;         // 중복 클릭 가드 — 콜백은 1회만
    bool          m_blockWarned;       // 누를 수 없는 타깃 경고 1회(매 프레임 스팸 방지)
    bool          m_confirmMode;       // 메시지 모드(딤 탭으로 완료. 승격·손가락 없음)
    Button        m_blockerButton;     // 딤 탭 수신용. Awake에서 1회 확보하고 리스너만 모드별로 붙였다 뗀다

    // 승격 상태. 원래 컴포넌트를 지우지 않도록 "내가 붙였는지"와 원래 정렬값을 함께 들고 있는다.
    Canvas m_promotedCanvas;
    bool   m_promoted;
    bool   m_addedCanvas;
    bool   m_addedRaycaster;
    bool   m_prevOverrideSorting;
    int    m_prevSortingOrder;
    int    m_prevSortingLayerID;

    Tween m_ringTween;
    Tween m_handTween;

    /// <summary>게이트 UI를 1회 생성. 이미 있으면 재사용.
    /// <paramref name="_prefab"/>이 있으면 그것을 인스턴스화하고, 없으면 코드 폴백으로 빌드한다.
    /// 프리팹은 호출측(OutgameTutorialBridge)이 [SerializeField]로 보유해 넘긴다.</summary>
    public static OutgameTutorialGateUI Ensure(OutgameTutorialGateUI _prefab = null)
    {
        if (Instance != null) return Instance;

        if (_prefab != null)
        {
            var t_spawned = Instantiate(_prefab);
            t_spawned.name = "OutgameTutorialGate";
            if (Instance != null) return Instance;

            // 루트가 비활성 저장이면 Awake가 안 돌아 Instance가 비어 있다 — 누수 없이 폴백으로 살린다.
            Debug.LogError("[OutgameTutorialGateUI] 프리팹이 Awake를 돌지 못했습니다(루트 비활성 저장?) — 코드 폴백으로 대체합니다.");
            Destroy(t_spawned.gameObject);
        }
        else
        {
            Debug.LogWarning("[OutgameTutorialGateUI] 게이트 프리팹 미배선 — 딤+문구만 그립니다(포커스 링·손가락 없음).");
        }

        new GameObject("OutgameTutorialGate").AddComponent<OutgameTutorialGateUI>();
        return Instance;
    }

    /// <summary>타깃만 딤 위로 올리는 게이트를 건다(_onSatisfied는 1회만 호출).
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

        Release();

        m_target       = _target;
        m_targetButton = _targetButton;
        m_targetCanvas = _target.GetComponentInParent<Canvas>();
        m_onSatisfied  = _onSatisfied;
        m_satisfied    = false;
        m_blockWarned  = false;
        m_armed        = true;

        if (_onSatisfied != null) m_targetButton.onClick.AddListener(OnTargetClicked);

        SetDim(true);
        SetMessage(_message);
        RefreshVisibility();   // 첫 프레임 깜빡임 방지(LateUpdate 이전에 1회)
    }

    /// <summary>딤 없이 안내 문구만 띄운다. 걸 타깃이 아예 없거나(개봉 대기처럼 클릭이 아닌 신호로 끝나는 스텝),
    /// 타깃에 Button이 없어 ShowGate가 거부하는 경우용.
    /// 딤을 켜지 않는 것이 이 모드의 계약이다 — 개봉 스와이프(PackTearHandle)가 EventSystem.IsPointerOverGameObject로
    /// 시작 여부를 판정하므로, 전체화면 딤이 있으면 화면 어디를 눌러도 true가 되어 제스처가 영영 시작되지 않는다.
    /// 게다가 이 모드는 m_armed=false라 LateUpdate가 돌지 않아 탈출로도 없다.</summary>
    public void ShowBanner(string _message)
    {
        Release();

        m_target       = null;
        m_targetCanvas = null;
        m_onSatisfied  = null;
        m_satisfied    = false;
        m_blockWarned  = false;
        m_armed        = false;   // 추종할 타깃이 없다 → LateUpdate 미개입

        if (string.IsNullOrEmpty(_message)) { HideGate(); return; }

        SetDim(false);
        SetPointerActive(false);
        SetMessage(_message);

        if (this.messageRect != null)
            this.messageRect.anchoredPosition =
                new Vector2(0f, m_canvasRect.rect.yMin + this.messageRect.sizeDelta.y * 0.5f + this.messageBottom);

        m_gateRoot.SetActive(true);
    }

    /// <summary>딤 + 문구를 띄우고 화면(딤) 탭으로 넘기는 설명 게이트. <paramref name="_highlight"/>가 있으면 링으로 그 영역을 강조한다.
    /// 승격·손가락이 없는 것이 이 모드의 계약이다 — 읽을 영역이지 누를 대상이 아니라서, 승격하면 아직 눌러선 안 되는
    /// 위젯이 튜토리얼 때문에 뚫린다. 그래서 하이라이트에 Button이 없어도(순수 영역) 정상이다.</summary>
    public void ShowMessageGate(RectTransform _highlight, string _message, Action _onSatisfied)
    {
        Release();

        m_target       = _highlight;
        m_targetButton = null;
        m_targetCanvas = _highlight != null ? _highlight.GetComponentInParent<Canvas>() : null;
        m_onSatisfied  = _onSatisfied;
        m_satisfied    = false;
        m_blockWarned  = false;
        m_confirmMode  = true;
        m_armed        = _highlight != null;   // 추종할 영역이 있을 때만 LateUpdate가 링을 따라간다

        ArmBlockerClick();

        SetDim(true);
        SetMessage(_message);

        if (m_armed)
        {
            RefreshVisibility();   // 첫 프레임 깜빡임 방지(LateUpdate 이전에 1회)
            return;
        }

        // 하이라이트가 없으면 링도 없다 → 문구만 하단에 고정(ShowBanner와 같은 배치).
        SetPointerActive(false);

        if (this.messageRect != null)
            this.messageRect.anchoredPosition =
                new Vector2(0f, m_canvasRect.rect.yMin + this.messageRect.sizeDelta.y * 0.5f + this.messageBottom);

        m_gateRoot.SetActive(true);
    }

    /// <summary>딤을 숨기고 승격·리스너를 해제한다(앵커 미등장 시 대기 상태). 콜백은 유지되지 않는다.</summary>
    public void HideGate()
    {
        Release();

        m_target       = null;
        m_targetCanvas = null;
        m_armed        = false;
        m_blockWarned  = false;

        SetPointerActive(false);
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

        if (this.blocker == null) BuildFallbackUI();   // 프리팹 참조 미배선 = 코드 빌드 폴백

        CacheRoots();
        CacheBlockerButton();  // blocker가 확정된 뒤여야 한다(폴백 경로도 여기서 함께 처리된다)
        LiftOrnaments();       // 승격된 타깃(351)에 안내가 덮이지 않도록 352로 올린다
        NormalizeGraphics();   // 안내 요소의 raycastTarget을 런타임에서 한 번 바로잡는다
        EnsureEventSystem();

        m_gateRoot.SetActive(false);
    }

    void OnDestroy()
    {
        Release();      // 승격·리스너가 남으면 다음 스텝에서 오발화하거나 버튼이 영구히 떠 있는다
        StopPulse();
        if (Instance == this) Instance = null;
    }

    // 타깃이 레이아웃 애니메이션·스크롤로 움직여도 링·손가락이 따라가도록 매 프레임 재계산.
    void LateUpdate()
    {
        if (!m_armed) return;
        if (m_target == null) { HideGate(); return; }   // 타깃 파괴(씬 전환 등) 시 화면 잠김 방지

        RefreshVisibility();
    }

    // 딤·승격·포인터는 "타깃을 실제로 누를 수 있을 때"만 켠다. 꺼져 있거나(탭 전환) interactable=false면(팩 SO 미배선·
    // 골드 잠금 등) 셋 다 내리고 게이트는 유지 — 그러지 않으면 화면 전면 차단 + 탈출 수단 0이 된다.
    // 승격을 같이 내리는 이유가 하나 더 있다: overrideSorting은 조상 CanvasGroup의 raycast 필터를 끊으므로,
    // 승격을 남겨 두면 게임이 막으려던 입력이 튜토리얼 때문에 뚫린다.
    void RefreshVisibility()
    {
        bool t_active = m_target.gameObject.activeInHierarchy;

        // 메시지 모드엔 누를 타깃이 없다(버튼 없는 순수 영역도 하이라이트한다) → 표시 여부는 활성 여부만으로 판정한다.
        // 화면 탭 자체가 탈출로라 딤이 유지돼도 불변식 (2)를 어기지 않는다.
        bool t_clickable = m_confirmMode || (m_targetButton != null && m_targetButton.IsInteractable());
        bool t_visible   = t_active && t_clickable;

        if (t_visible && !m_confirmMode) Promote();
        else                             Demote();

        if (m_gateRoot.activeSelf != t_visible) m_gateRoot.SetActive(t_visible);

        if (!t_visible)
        {
            SetPointerActive(false);

            // 보이는데 못 누르는 건 배선 실수 신호 — 탭 전환(비활성)은 정상이라 경고하지 않는다.
            if (t_active && !m_blockWarned)
            {
                m_blockWarned = true;
                Debug.LogWarning($"[OutgameTutorialGateUI] 타깃 '{m_target.name}'의 버튼이 비활성(interactable=false)이라 안내를 숨기고 대기합니다(소프트락 방지).");
            }
            return;
        }

        m_blockWarned = false;
        Layout();
    }

    // 타깃 월드 코너 → 스크린 → 게이트 캔버스 로컬로 변환해 링·손가락·문구를 배치.
    // 타깃의 rect.size·anchoredPosition을 직접 읽으면 안 된다 — 캔버스 referenceResolution이 씬마다 달라
    // (로비 1080x1920, 개봉 1440x3120) 그대로 옮기면 배율만큼 어긋난다. 스크린 경유가 유일한 정답이다.
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

        // 타깃이 화면 밖이면 포인터를 전부 숨긴다(허공을 가리키는 유령 방지). 딤은 유지 — 차단은 계속돼야 한다.
        bool t_onScreen = t_max.x > t_min.x && t_max.y > t_min.y
                          && t_max.x > t_full.xMin && t_min.x < t_full.xMax
                          && t_max.y > t_full.yMin && t_min.y < t_full.yMax;
        if (!t_onScreen) { SetPointerActive(false); return; }

        SetPointerActive(true);

        Vector2 t_center = (t_min + t_max) * 0.5f;
        Vector2 t_size   = t_max - t_min;

        if (this.focusRing != null)
        {
            // 화면 전체폭 영역(덱 편집의 소유 카드·편성 칸)은 링을 그대로 키우면 좌우 테두리가 화면 밖으로 나가 사라진다
            // — 9슬라이스라 보이는 건 테두리뿐이다. 펄스로 pulseScale까지 커지는 것까지 감안해 캔버스 안으로 제한한다.
            Vector2 t_limit = t_full.size / Mathf.Max(1f, this.pulseScale);

            this.focusRing.sizeDelta        = Vector2.Min(t_size + Vector2.one * (this.ringPadding * 2f), t_limit);
            this.focusRing.anchoredPosition = t_center;
        }

        // 손끝(스프라이트 좌상단)이 타깃 중앙을 가리키도록, 손 이미지를 자기 크기의 절반만큼 우하단으로 민다
        // — 그래야 이미지 좌상단 모서리가 정확히 타깃 중앙에 온다. handOffset은 스프라이트 여백 보정용.
        // 화면 끝에 붙은 타깃(하단바 탭·우측 상단 아이콘)에서 손가락이 잘리지 않게 가로·세로 모두 캔버스 안으로 민다.
        // 메시지 모드는 손가락을 쓰지 않는다 — 숨겨 둔 채 좌표만 계산할 이유가 없다.
        if (this.hand != null && !m_confirmMode)
        {
            Vector2 t_handHalf = this.hand.rect.size * 0.5f;
            Vector2 t_handPos  = t_center + new Vector2(t_handHalf.x, -t_handHalf.y) + this.handOffset;

            t_handPos.x = Mathf.Clamp(t_handPos.x, t_full.xMin + t_handHalf.x, t_full.xMax - t_handHalf.x);
            t_handPos.y = Mathf.Clamp(t_handPos.y, t_full.yMin + t_handHalf.y, t_full.yMax - t_handHalf.y);
            this.hand.anchoredPosition = t_handPos;
        }

        PlaceMessage(t_full, t_min.y, t_max.y);
    }

    // 타깃을 가리지 않는 쪽에 문구를 둔다 — 타깃이 화면 위쪽이면 아래, 아래쪽이면 위.
    // 고정 위치로 두면 하단바 탭 스텝에서 문구가 타깃과 겹치고, 타깃이 문구 위로 승격돼 깨진 것처럼 보인다.
    void PlaceMessage(Rect _full, float _targetYMin, float _targetYMax)
    {
        if (this.messageRect == null || !this.messageRect.gameObject.activeSelf) return;

        float t_half   = this.messageRect.sizeDelta.y * 0.5f;
        float t_center = (_targetYMin + _targetYMax) * 0.5f;
        float t_y = t_center > 0f
            ? _targetYMin - this.messageMargin - t_half
            : _targetYMax + this.messageMargin + t_half;

        t_y = Mathf.Clamp(t_y, _full.yMin + t_half + this.messageMargin, _full.yMax - t_half - this.messageMargin);
        this.messageRect.anchoredPosition = new Vector2(0f, t_y);
    }

    // ── 타깃 승격 ────────────────────────────────────────────────────────────

    // 타깃에 중첩 Canvas를 얹어 딤 위로 올린다. 멱등(RefreshVisibility가 매 프레임 부른다).
    void Promote()
    {
        if (m_promoted || m_target == null) return;

        var t_go   = m_target.gameObject;
        var t_root = m_targetCanvas != null ? m_targetCanvas.rootCanvas : null;

        m_promotedCanvas = t_go.GetComponent<Canvas>();
        m_addedCanvas    = m_promotedCanvas == null;

        if (m_addedCanvas)
        {
            m_promotedCanvas = t_go.AddComponent<Canvas>();
        }
        else
        {
            // 저작된 Canvas는 지우지 않는다 — 원래 정렬값만 백업해 두고 복원한다.
            m_prevOverrideSorting = m_promotedCanvas.overrideSorting;
            m_prevSortingOrder    = m_promotedCanvas.sortingOrder;
            m_prevSortingLayerID  = m_promotedCanvas.sortingLayerID;
        }

        m_promotedCanvas.overrideSorting = true;
        m_promotedCanvas.sortingOrder    = TargetOrder;

        // 중첩 Canvas는 정렬 레이어와 셰이더 채널을 기본값으로 리셋한다 — 루트에서 복사하지 않으면
        // 승격 중에만 TMP·그라디언트가 깨진다(Normal/Tangent 채널 손실).
        if (t_root != null)
        {
            m_promotedCanvas.sortingLayerID           = t_root.sortingLayerID;
            m_promotedCanvas.additionalShaderChannels = t_root.additionalShaderChannels;
        }

        m_addedRaycaster = t_go.GetComponent<GraphicRaycaster>() == null;
        if (m_addedRaycaster) t_go.AddComponent<GraphicRaycaster>();

        m_promoted = true;
    }

    // 승격 해제. 타깃이 비활성이거나 파괴된 뒤에도 안전해야 한다(탭 버튼은 클릭 즉시 SetActive(false)된다).
    void Demote()
    {
        if (!m_promoted) return;
        m_promoted = false;

        if (m_promotedCanvas == null) return;   // 타깃이 이미 파괴됨

        // 파괴 순서 고정: GraphicRaycaster가 Canvas를 RequireComponent하므로 Canvas를 먼저 지우면
        // 조용히 실패해 둘 다 남는다.
        if (m_addedRaycaster)
        {
            var t_raycaster = m_promotedCanvas.GetComponent<GraphicRaycaster>();
            if (t_raycaster != null) Destroy(t_raycaster);
        }

        if (m_addedCanvas)
        {
            Destroy(m_promotedCanvas);
        }
        else
        {
            m_promotedCanvas.overrideSorting = m_prevOverrideSorting;
            m_promotedCanvas.sortingOrder    = m_prevSortingOrder;
            m_promotedCanvas.sortingLayerID  = m_prevSortingLayerID;
        }

        m_promotedCanvas = null;
    }

    // ── 표시 토글 ────────────────────────────────────────────────────────────

    // 딤은 켜고 끄는 것이 곧 입력 차단의 켜고 끔이다. 둘을 따로 두면 "안 보이는데 막힌" 상태가 생긴다.
    // 주의: 딤이 막는 것은 EventSystem 입력뿐이다 — raw Input을 폴링하는 코드는 오히려 과차단된다.
    void SetDim(bool _on)
    {
        if (this.blocker == null) return;

        this.blocker.enabled       = _on;
        this.blocker.raycastTarget = _on;
    }

    void SetPointerActive(bool _on)
    {
        bool t_hand = _on && !m_confirmMode;   // 메시지 모드는 읽을 영역이라 손가락을 띄우지 않는다(링만)

        if (this.focusRing != null && this.focusRing.gameObject.activeSelf != _on)     this.focusRing.gameObject.SetActive(_on);
        if (this.hand      != null && this.hand.gameObject.activeSelf      != t_hand) this.hand.gameObject.SetActive(t_hand);

        if (_on) StartPulse();
        else     StopPulse();
    }

    void SetMessage(string _message)
    {
        bool t_has = !string.IsNullOrEmpty(_message);

        if (this.messageRect != null) this.messageRect.gameObject.SetActive(t_has);
        if (t_has && this.messageText != null) this.messageText.text = _message;
    }

    // localScale만 건드린다 — sizeDelta·anchoredPosition은 Layout이 매 프레임 덮어써 트윈이 조용히 사라진다.
    void StartPulse()
    {
        if (this.pulseDuration <= 0f) return;

        if (this.focusRing != null && m_ringTween == null)
            m_ringTween = this.focusRing.DOScale(this.pulseScale, this.pulseDuration)
                .SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo).SetLink(this.focusRing.gameObject);

        // 숨겨 둔 손가락(메시지 모드)까지 돌릴 이유가 없다 — SetPointerActive가 먼저 활성 여부를 확정한다.
        if (this.hand != null && this.hand.gameObject.activeSelf && m_handTween == null)
            m_handTween = this.hand.DOScale(this.pulseScale, this.pulseDuration)
                .SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo).SetLink(this.hand.gameObject);
    }

    // SetLink는 파괴에만 반응하고 비활성화에는 반응하지 않는다 → 숨길 때 직접 죽이고 스케일을 되돌린다.
    void StopPulse()
    {
        if (m_ringTween != null)
        {
            m_ringTween.Kill();
            m_ringTween = null;
            if (this.focusRing != null) this.focusRing.localScale = Vector3.one;
        }

        if (m_handTween != null)
        {
            m_handTween.Kill();
            m_handTween = null;
            if (this.hand != null) this.hand.localScale = Vector3.one;
        }
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

    // 메시지 모드의 완료 = 딤 탭. 다른 모드에서 딤을 눌러도 아무 일이 없어야 하므로 모드 플래그로 한 번 더 막는다.
    void OnBlockerClicked()
    {
        if (!m_confirmMode || m_satisfied) return;
        m_satisfied = true;

        Action t_callback = m_onSatisfied;
        HideGate();          // 콜백이 다음 게이트를 걸 수 있도록 먼저 정리
        m_onSatisfied = null;
        t_callback?.Invoke();
    }

    // 타깃을 놓는 모든 경로가 반드시 지나는 단일 창구. 승격과 리스너를 함께 푼다 —
    // 한쪽만 풀면 버튼이 모든 UI 위에 영구히 떠 있거나 다음 스텝이 오발화한다.
    void Release()
    {
        Demote();

        if (m_targetButton != null) m_targetButton.onClick.RemoveListener(OnTargetClicked);
        m_targetButton = null;

        // 메시지 모드 상태도 여기서 되돌린다 — 딤 리스너가 남으면 다음 스텝이 화면 탭만으로 넘어가 버린다.
        if (m_blockerButton != null) m_blockerButton.onClick.RemoveListener(OnBlockerClicked);
        m_confirmMode = false;
    }

    // 딤 탭 수신을 켠다. Release()가 항상 먼저 떼므로 중복 부착은 없지만, 단일 창구를 지키려 여기서도 한 번 뗀다.
    void ArmBlockerClick()
    {
        if (m_blockerButton == null) return;

        m_blockerButton.onClick.RemoveListener(OnBlockerClicked);
        m_blockerButton.onClick.AddListener(OnBlockerClicked);
    }

    // ── 초기화 ──────────────────────────────────────────────────────────────

    void CacheRoots()
    {
        var t_canvas = GetComponentInChildren<Canvas>(true);
        if (t_canvas != null)
        {
            // 정렬값은 저작 대상이 아니다 — 400(팝업) 위로 올라가면 팝업이 딤에 묻혀 하드락이 된다.
            t_canvas.sortingOrder = GateOrder;
            m_gateCanvas = t_canvas;
            m_canvasRect = t_canvas.GetComponent<RectTransform>();
        }

        // 표시 요소들의 부모가 곧 게이트 루트다(별도 필드를 두면 오배선 축만 늘어난다).
        m_gateRoot = this.blocker != null ? this.blocker.transform.parent.gameObject : gameObject;
    }

    // 메시지 모드의 완료 신호는 딤 탭이다 → blocker에 Button을 1회 확보해 둔다(프리팹·폴백 공통).
    // 룩은 딤 이미지가 전부라 transition은 끈다.
    void CacheBlockerButton()
    {
        if (this.blocker == null) return;

        m_blockerButton = this.blocker.GetComponent<Button>();
        if (m_blockerButton == null) m_blockerButton = this.blocker.gameObject.AddComponent<Button>();

        m_blockerButton.transition = Selectable.Transition.None;
    }

    // 링·손가락·문구를 승격된 타깃(351)보다 위(352)로 올린다. 게이트 캔버스 그대로 두면 타깃이 딤 위로 올라오면서
    // 자기 위에 겹친 손가락·링을 덮어버린다(타깃 중앙을 가리키는 손가락이 특히 통째로 사라진다).
    // blocker는 제외 — 딤까지 올리면 타깃이 다시 딤에 묻혀 누를 수 없게 된다.
    // GraphicRaycaster는 일부러 붙이지 않는다: 레이캐스터 없는 중첩 Canvas의 그래픽은 레이캐스트 대상에서 아예 빠져
    // 안내가 타깃 클릭을 가로챌 수 없다(raycastTarget=false와 이중 안전장치).
    void LiftOrnaments()
    {
        LiftAbove(this.focusRing,   m_gateCanvas);
        LiftAbove(this.hand,        m_gateCanvas);
        LiftAbove(this.messageRect, m_gateCanvas);
    }

    static void LiftAbove(Component _ornament, Canvas _root)
    {
        if (_ornament == null) return;

        var t_canvas = _ornament.GetComponent<Canvas>();
        if (t_canvas == null) t_canvas = _ornament.gameObject.AddComponent<Canvas>();

        t_canvas.overrideSorting = true;
        t_canvas.sortingOrder    = OrnamentOrder;

        // Promote()와 같은 이유 — 중첩 Canvas는 정렬 레이어·셰이더 채널을 기본값으로 리셋해
        // 복사하지 않으면 문구(TMP)와 그라디언트가 깨진다.
        if (_root == null) return;

        t_canvas.sortingLayerID           = _root.sortingLayerID;
        t_canvas.additionalShaderChannels = _root.additionalShaderChannels;
    }

    // 안내 요소는 입력을 먹으면 안 된다 — 문구 전용 모드엔 딤이 없어 메시지가 터치를 가로챈다.
    void NormalizeGraphics()
    {
        if (this.blocker != null) this.blocker.raycastTarget = true;

        ClearRaycast(this.focusRing);
        ClearRaycast(this.hand);
        ClearRaycast(this.messageRect);
    }

    static void ClearRaycast(Component _root)
    {
        if (_root == null) return;

        var t_graphics = _root.GetComponentsInChildren<Graphic>(true);
        for (int t_i = 0; t_i < t_graphics.Length; t_i++) t_graphics[t_i].raycastTarget = false;
    }

    // 프리팹 미배선 폴백. 링·손가락 스프라이트는 Resources 밖(Layer Lab ResourcesData)이라 코드로 얻을 수 없다
    // → 딤 + 문구까지만 만든다. 안내 강도는 떨어지지만 진행은 막히지 않는다.
    void BuildFallbackUI()
    {
        var t_canvasGo = new GameObject("Canvas");
        t_canvasGo.transform.SetParent(transform, false);
        var t_canvas = t_canvasGo.AddComponent<Canvas>();
        t_canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        t_canvas.sortingOrder = GateOrder;
        var t_scaler = t_canvasGo.AddComponent<CanvasScaler>();
        t_scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        t_scaler.referenceResolution = new Vector2(1080f, 1920f);
        t_scaler.matchWidthOrHeight  = 0f;
        t_canvasGo.AddComponent<GraphicRaycaster>();

        var t_gateGo = new GameObject("Gate");
        t_gateGo.transform.SetParent(t_canvasGo.transform, false);
        var t_gateRect = t_gateGo.AddComponent<RectTransform>();
        t_gateRect.anchorMin = Vector2.zero;
        t_gateRect.anchorMax = Vector2.one;
        t_gateRect.offsetMin = t_gateRect.offsetMax = Vector2.zero;

        var t_blockerGo = new GameObject("Blocker");
        t_blockerGo.transform.SetParent(t_gateRect, false);
        this.blocker = t_blockerGo.AddComponent<Image>();
        this.blocker.color = new Color(0f, 0f, 0f, 0.72f);
        var t_blockerRect = this.blocker.rectTransform;
        t_blockerRect.anchorMin = Vector2.zero;
        t_blockerRect.anchorMax = Vector2.one;
        t_blockerRect.offsetMin = t_blockerRect.offsetMax = Vector2.zero;

        var t_msgGo = new GameObject("Message");
        t_msgGo.transform.SetParent(t_gateRect, false);
        var t_msgBg = t_msgGo.AddComponent<Image>();
        t_msgBg.color = new Color(0f, 0f, 0f, 0.85f);

        this.messageRect = t_msgBg.rectTransform;
        this.messageRect.anchorMin = this.messageRect.anchorMax = this.messageRect.pivot = new Vector2(0.5f, 0.5f);
        this.messageRect.sizeDelta = new Vector2(900f, 200f);

        var t_txtGo = new GameObject("Text");
        t_txtGo.transform.SetParent(t_msgGo.transform, false);
        this.messageText = t_txtGo.AddComponent<TextMeshProUGUI>();
        this.messageText.fontSize           = 46f;
        this.messageText.color              = Color.white;
        this.messageText.alignment          = TextAlignmentOptions.Center;
        this.messageText.enableWordWrapping = true;

        var t_txtRect = this.messageText.rectTransform;
        t_txtRect.anchorMin = Vector2.zero;
        t_txtRect.anchorMax = Vector2.one;
        t_txtRect.offsetMin = new Vector2(32f, 20f);
        t_txtRect.offsetMax = new Vector2(-32f, -20f);

        t_msgGo.SetActive(false);
    }

    static void EnsureEventSystem()
    {
        if (UnityEngine.Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() != null) return;

        var t_es = new GameObject("EventSystem");
        t_es.AddComponent<UnityEngine.EventSystems.EventSystem>();
        t_es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
    }
}

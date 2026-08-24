using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 아웃게임 튜토리얼 강제 게이트 UI(스텝·세이브·앵커를 모르는 순수 표시 컴포넌트).
// 전체화면 딤 1장이 입력을 전부 흡수하고, 타깃만 중첩 Canvas로 딤 위에 승격해 선명하게 보이고 눌리게 한다.
// 완료 판정은 타깃 버튼 onClick 구독으로만 — 기존 리스너 무접촉이라 원래 동작이 그대로 실행된다.
//
// 불변식 3개:
//  (1) 딤 표시 == 타깃 승격 == 포인터 표시. 뒤집는 곳은 RefreshVisibility 하나뿐이다.
//      단 스텝이 딤을 끄면(TutorialStepDef.UseDim=false) 딤·승격이 처음부터 빠지고 포인터만 남는다
//      — 그 스텝의 차단은 기능 잠금(OutgameFeatureLock)이 대신 맡는다.
//  (2) 딤이 걸린 채 누를 수 있는 것이 하나도 없는 상태를 만들지 않는다.
//  (3) 무대는 하나뿐이고 주인은 마지막에 건 쪽이다. 남의 무대는 걷지 않는다(m_owner·OwnedBy).
//      온보딩(OutgameTutorialBridge)과 트리거(TriggeredTutorialBridge)가 이 인스턴스를 공유하므로,
//      소유권 없이 걷으면 그 런은 완료 신호를 받을 주체를 잃고 영영 멈춘다.
//      가져가는 것은 막지 않는다 — 트리거가 발화하면 무대를 넘겨받는 것이 맞고, 온보딩은
//      트리거가 끝났다는 통지를 받아 자기 안내를 다시 세운다.
//
// 강조는 "딤 위로 올라온 대상" 그 자체다 — 테두리를 덧그리지 않는다. 봐야 할 것만 밝게 남는 것이 안내다.
//
// 메시지 모드(ShowMessageGate)는 손가락만 빼고 딤+승격을 켠다 — 읽을 영역이라도 딤 아래 깔리면
// 무엇을 보라는 것인지 성립하지 않는다. 대신 승격에 레이캐스터를 달지 않는다(PromoteOne 참조).
// 그리고 영역 안에 카드가 있으면 영역째가 아니라 카드만 올린다(CollectHighlights).
// 완료가 딤 탭 자체라 (2)는 오히려 항상 만족한다.
// 이 모드에서 딤을 끄면 판은 투명해지되 **입력은 계속 막는다** — 완료가 그 탭이기 때문이다(SetBlocker 참조).
// 뒤 화면이 스스로 할 말을 다 하고 있어 문구 한 줄만 얹으면 되는 자리에 쓴다(강화 결과판 등).
//
// 룩은 프리팹(OutgameTutorialGate.prefab)에서 저작한다. 미배선이면 딤+문구만 코드로 그리는 폴백으로 떨어진다
// — 손가락 스프라이트가 Resources 밖에 있어 코드 경로에서는 얻을 방법이 없다.
public class OutgameTutorialGateUI : MonoBehaviour
{
    public static OutgameTutorialGateUI Instance { get; private set; }

    // 정렬 불변식: TutorialOverlay(200) < 딤(350) < 타깃(351) < 안내 요소(352) < UIPoolManager 팝업(400) < Mulligan(999) < LoadingCover(1000).
    // 400을 넘기면 안 된다 — 플레이 스텝의 "유효한 덱이 없습니다"(LobbyMatchLauncher)와 구매 실패 팝업
    // (PackShowcaseController)이 딤에 묻히는데, 그때 타깃 버튼은 interactable=true 그대로라 아래 탈출로도 발동하지 않는다.
    //
    // 손가락·문구가 타깃보다 위인 이유: 딤 위로 승격된 타깃이 자기 위에 겹친 안내를 그대로 덮어버린다.
    // 딤만 타깃 아래에 남는다 — 딤까지 올리면 타깃이 다시 묻혀 누를 수 없게 된다.
    const int GateOrder     = UiSortingOrder.TutorialGate;
    const int TargetOrder   = GateOrder + 1;
    const int OrnamentOrder = TargetOrder + 1;

    [Header("표시 요소 (blocker 미배선 = 코드 폴백)")]
    [SerializeField] Image           blocker;       // 전체화면 딤 겸 입력 흡수막
    [SerializeField] RectTransform   hand;          // 손가락 포인터(옵션)
    [SerializeField] RectTransform   messageRect;   // 안내 문구 프레임
    [SerializeField] TextMeshProUGUI messageText;

    [Header("배치")]
    [Tooltip("Hand 기준점을 놓을 위치 = 타깃 중앙 + 이 값. 손끝 방향·각도는 프리팹의 HandIcon(자식)에서 저작하고, " +
             "여기서는 그 손끝이 타깃 중앙에 닿도록 기준점만 밀어 준다(코드는 손 크기·각도를 보정하지 않는다)")]
    [SerializeField] Vector2 handOffset = Vector2.zero;

    [Tooltip("문구를 타깃에서 비켜 놓을 때의 최소 간격. 문구 자리는 화면 중앙이 기본이고, " +
             "타깃과 세로로 겹치는 스텝에서만 이 간격을 두고 위/아래로 물러난다")]
    [SerializeField] float messageMargin = 36f;

    [Tooltip("문구를 하단에 두라고 저작된 스텝(messageAtBottom)에서 화면 아래 끝과 벌리는 간격.\n" +
             "이 캔버스는 안전영역을 따르지 않으므로 홈 인디케이터에 물리지 않을 만큼은 띄운다")]
    [SerializeField] float messageBottomInset = 120f;

    [Header("포인터 연출")]
    [SerializeField] float pulseScale    = 1.08f;
    [SerializeField] float pulseDuration = 0.6f;

    RectTransform m_canvasRect;
    Canvas        m_gateCanvas;   // 중첩 Canvas가 리셋하는 정렬 레이어·셰이더 채널을 복사해 올 원본
    GameObject    m_gateRoot;

    readonly Vector3[] m_corners = new Vector3[4];   // GetWorldCorners 재사용 버퍼

    // 지금 무대를 쥔 브리지. MonoBehaviour로 들고 있는 이유는 파괴 판정 때문이다 —
    // object로 두면 씬과 함께 죽은 주인이 참조로 남아 무대가 영영 잠긴다(Unity의 == 오버로드가 안 걸린다).
    MonoBehaviour m_owner;

    RectTransform m_target;
    Button        m_targetButton;
    Canvas        m_targetCanvas;      // 승격 전에 잡아 둔 타깃의 원래 캔버스(스크린 변환·루트 조회에 쓴다)
    Action        m_onSatisfied;
    bool          m_armed;             // 게이트 진행 중(타깃 파괴 감지에 사용)
    bool          m_satisfied;         // 중복 클릭 가드 — 콜백은 1회만
    bool          m_blockWarned;       // 누를 수 없는 타깃 경고 1회(매 프레임 스팸 방지)
    bool          m_confirmMode;       // 메시지 모드(딤 탭으로 완료. 승격·손가락 없음)
    bool          m_atBottom;          // 문구의 홈이 화면 중앙이 아니라 하단인가(무대 가운데를 비워야 하는 스텝)
    bool          m_dim = true;        // 딤으로 타깃 외 입력을 막는가. 끄면 승격도 하지 않는다(가릴 것이 없다)
    Button        m_blockerButton;     // 딤 탭 수신용. Awake에서 1회 확보하고 리스너만 모드별로 붙였다 뗀다
    Color         m_dimColor = Color.black;   // 프리팹에 저작된 딤 색(판을 끌 때 알파 0으로 내렸다가 되돌린다)

    // 승격 상태. 원래 컴포넌트를 지우지 않도록 "내가 붙였는지"와 원래 정렬값을 함께 들고 있는다.
    // 카드 단위 승격(CollectHighlights) 때문에 여러 개가 동시에 올라간다.
    readonly List<Promotion> m_promotions = new List<Promotion>();
    bool m_promoted;

    readonly List<CardVisualView> m_cardBuffer = new List<CardVisualView>();   // 카드 수집 재사용 버퍼

    Tween m_handTween;

    struct Promotion
    {
        public Canvas Canvas;
        public bool   AddedCanvas;
        public bool   AddedRaycaster;
        public bool   PrevOverrideSorting;
        public int    PrevSortingOrder;
        public int    PrevSortingLayerID;
    }

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
            Debug.LogWarning("[OutgameTutorialGateUI] 게이트 프리팹 미배선 — 딤+문구만 그립니다(손가락 없음).");
        }

        new GameObject("OutgameTutorialGate").AddComponent<OutgameTutorialGateUI>();
        return Instance;
    }

    /// <summary>타깃만 딤 위로 올리는 게이트를 건다(_onSatisfied는 1회만 호출).
    /// 버튼이 없으면 소프트락이므로 게이트를 걸지 않는다.
    /// _onSatisfied가 null이면 클릭을 완료로 보지 않는다 — 딤만 유지하고 완료는 호출자가 다른 신호로 판정한다
    /// (구매처럼 눌러도 실패할 수 있는 스텝).
    /// <paramref name="_dim"/>=false면 손가락·문구만 띄우고 차단은 기능 잠금(OutgameFeatureLock)에 맡긴다 —
    /// 딤이 없으면 타깃을 가릴 것도 없으므로 승격도 하지 않는다.
    /// <paramref name="_owner"/>는 무대를 가져가는 브리지다(불변식 3).</summary>
    public void ShowGate(MonoBehaviour _owner, RectTransform _target, Button _targetButton, string _message, Action _onSatisfied, bool _dim = true)
    {
        if (_target == null)
        {
            Debug.LogWarning("[OutgameTutorialGateUI] 타깃 RectTransform이 없어 게이트를 걸지 않습니다.");
            HideGate(_owner);
            return;
        }
        if (_targetButton == null)
        {
            Debug.LogWarning($"[OutgameTutorialGateUI] 타깃 '{_target.name}'에 Button이 없어 게이트를 걸지 않습니다(소프트락 방지).");
            HideGate(_owner);
            return;
        }

        Release();

        m_owner        = _owner;
        m_target       = _target;
        m_targetButton = _targetButton;
        m_targetCanvas = _target.GetComponentInParent<Canvas>();
        m_onSatisfied  = _onSatisfied;
        m_satisfied    = false;
        m_blockWarned  = false;
        m_armed        = true;
        m_dim          = _dim;

        if (_onSatisfied != null) m_targetButton.onClick.AddListener(OnTargetClicked);

        SetBlocker(_dim, _dim);
        SetMessage(_message);
        RefreshVisibility();   // 첫 프레임 깜빡임 방지(LateUpdate 이전에 1회)
    }

    /// <summary>딤 없이 안내 문구만 띄운다. 걸 타깃이 아예 없거나(개봉 대기처럼 클릭이 아닌 신호로 끝나는 스텝),
    /// 타깃에 Button이 없어 ShowGate가 거부하는 경우용.
    /// 딤을 켜지 않는 것이 이 모드의 계약이다 — 개봉 스와이프(PackTearHandle)가 EventSystem.IsPointerOverGameObject로
    /// 시작 여부를 판정하므로, 전체화면 딤이 있으면 화면 어디를 눌러도 true가 되어 제스처가 영영 시작되지 않는다.
    /// 게다가 이 모드는 m_armed=false라 LateUpdate가 돌지 않아 탈출로도 없다.</summary>
    public void ShowBanner(MonoBehaviour _owner, string _message)
    {
        Release();

        m_owner        = _owner;
        m_target       = null;
        m_targetCanvas = null;
        m_onSatisfied  = null;
        m_satisfied    = false;
        m_blockWarned  = false;
        m_armed        = false;   // 추종할 타깃이 없다 → LateUpdate 미개입

        if (string.IsNullOrEmpty(_message)) { HideGate(_owner); return; }

        SetBlocker(false, false);
        SetPointerActive(false);
        SetMessage(_message);

        m_gateRoot.SetActive(true);
    }

    /// <summary>딤 + 문구를 띄우고 화면(딤) 탭으로 넘기는 설명 게이트.
    /// <paramref name="_highlight"/>가 있으면 그 안의 카드만(없으면 영역째) 딤 위로 올려 강조한다.
    /// 승격은 하되 레이캐스터 없이 한다 — 딤에 묻히면 안 되지만, 읽을 영역이라 눌려서도 안 된다.
    /// 손가락이 없는 것도 이 모드의 계약이다. 그래서 하이라이트에 Button이 없어도(순수 영역) 정상이다.
    /// <paramref name="_atBottom"/>이면 문구의 홈이 하단이 된다 — 무대 한가운데를 비워야 하는 스텝용이다.
    /// <paramref name="_dim"/>을 끄면 판이 투명해진다(뒤 화면을 그대로 보여 주는 자리) — 다만 <b>입력은 그대로 막는다</b>.
    /// 이 모드의 완료가 화면 탭이라 그렇다: 안 막으면 탭이 뒤 화면으로 새어 안내가 넘어가지 않는다.
    /// <paramref name="_owner"/>는 무대를 가져가는 브리지다(불변식 3).</summary>
    public void ShowMessageGate(MonoBehaviour _owner, RectTransform _highlight, string _message, Action _onSatisfied,
                                bool _atBottom = false, bool _dim = true)
    {
        Release();

        m_owner        = _owner;
        m_target       = _highlight;
        m_targetButton = null;
        m_targetCanvas = _highlight != null ? _highlight.GetComponentInParent<Canvas>() : null;
        m_onSatisfied  = _onSatisfied;
        m_satisfied    = false;
        m_blockWarned  = false;
        m_confirmMode  = true;
        m_atBottom     = _atBottom;
        m_dim          = _dim;                 // 승격도 이 값을 따른다 — 투명한 판 위로 올릴 이유가 없다
        m_armed        = _highlight != null;   // 추종할 영역이 있을 때만 LateUpdate가 문구를 따라 배치한다

        ArmBlockerClick();

        SetBlocker(_dim, true);
        SetMessage(_message);

        if (m_armed)
        {
            RefreshVisibility();   // 첫 프레임 깜빡임 방지(LateUpdate 이전에 1회)
            return;
        }

        // 하이라이트가 없으면 따라갈 영역도 없다 → 문구만 띄운다(자리는 SetMessage가 홈으로 고정).
        SetPointerActive(false);

        m_gateRoot.SetActive(true);
    }

    // 자기 안내만 접는다(앵커 미등장 시 대기 상태). 남이 무대를 쥐고 있으면 아무 일도 하지 않는다.
    // 공개면은 Show* / Clear(owner) / ClearForce 셋이면 충분하다 — 이것은 Show* 실패 경로의 내부 정리다.
    void HideGate(MonoBehaviour _owner)
    {
        if (!OwnedBy(_owner)) return;

        HideGateInternal();
    }

    /// <summary>자기 게이트를 전체 초기화한다(콜백·완료 가드 포함). 남의 무대는 건드리지 않는다.</summary>
    public void Clear(MonoBehaviour _owner)
    {
        if (!OwnedBy(_owner)) return;

        ClearForce();
    }

    /// <summary>소유권을 무시하고 무대를 비운다. 유저가 명시적으로 튜토리얼을 리셋하는 디버그 경로 전용이다.</summary>
    public void ClearForce()
    {
        HideGateInternal();
        m_onSatisfied = null;
        m_satisfied   = false;
        m_owner       = null;
    }

    // 무대의 주인인가. 주인이 없거나 씬과 함께 죽었으면(Unity의 == 오버로드) 누구든 접을 수 있다.
    bool OwnedBy(MonoBehaviour _owner) => m_owner == null || m_owner == _owner;

    // 소유권을 이미 확인한 뒤의 실제 접기. 게이트가 스스로 자기 상태를 되돌리는 경로도 여기로 온다.
    void HideGateInternal()
    {
        Release();

        m_target       = null;
        m_targetCanvas = null;
        m_armed        = false;
        m_blockWarned  = false;

        SetPointerActive(false);
        if (m_gateRoot != null) m_gateRoot.SetActive(false);
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (this.blocker == null) BuildFallbackUI();   // 프리팹 참조 미배선 = 코드 빌드 폴백

        CacheDimColor();
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

    // 타깃이 레이아웃 애니메이션·스크롤로 움직여도 손가락·문구가 따라가도록 매 프레임 재계산.
    void LateUpdate()
    {
        if (!m_armed) return;
        if (m_target == null) { HideGateInternal(); return; }   // 타깃 파괴(씬 전환 등) 시 화면 잠김 방지

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

        // 딤이 꺼진 스텝은 승격도 하지 않는다 — 승격은 딤 위로 끌어올리려는 장치인데 가릴 딤이 없다.
        if (t_visible && m_dim) Promote();
        else                    Demote();

        if (m_gateRoot.activeSelf != t_visible) m_gateRoot.SetActive(t_visible);

        if (!t_visible)
        {
            SetPointerActive(false);

            // 보이는데 못 누르는 건 배선 실수 신호 — 탭 전환(비활성)은 정상이라 경고하지 않는다.
            if (t_active && !m_blockWarned)
            {
                m_blockWarned = true;
                Debug.LogWarning($"[OutgameTutorialGateUI] 타깃 '{m_target.name}'의 버튼이 비활성(interactable=false)이라 안내를 숨기고 대기합니다(소프트락 방지).{DescribeLockCause()}");
            }
            return;
        }

        m_blockWarned = false;
        Layout();
    }

    // 기능 잠금이 원인이면 대기가 영영 안 풀린다(잠금은 진행으로만 열리는데 진행이 이 스텝에서 멈춰 있다).
    // 다른 원인(골드 부족 등)은 유저가 스스로 풀 수 있어 정상 대기다 — 둘을 로그에서 구분해 준다.
    string DescribeLockCause()
    {
        var t_lock = m_target != null ? m_target.GetComponentInParent<FeatureLockView>() : null;
        if (t_lock == null || !t_lock.IsLocked) return string.Empty;

        return $" 원인은 기능 잠금({t_lock.Feature})입니다 — 이 스텝까지의 unlocks에 해당 기능을 넣으세요.";
    }

    // 타깃 월드 코너 → 스크린 → 게이트 캔버스 로컬로 변환해 손가락·문구를 배치.
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
        if (!t_onScreen) { SetPointerActive(false); HomeMessage(); return; }   // 피할 타깃이 화면에 없다 → 홈으로

        SetPointerActive(true);

        Vector2 t_center = (t_min + t_max) * 0.5f;

        // 손은 타깃 중앙 + handOffset에 그대로 놓는다. 코드가 방향을 추측하지 않는 이유는
        // 손끝이 어디인지가 프리팹 저작에 달려 있기 때문이다 — Hand는 빈 기준점이고 그 안의 HandIcon을
        // 회전·이동시켜 손끝을 맞춘다. 여기서 자기 크기만큼 밀면 저작해 둔 각도와 어긋난다.
        // 메시지 모드는 손가락을 쓰지 않는다 — 숨겨 둔 채 좌표만 계산할 이유가 없다.
        if (this.hand != null && !m_confirmMode)
            this.hand.anchoredPosition = t_center + this.handOffset;

        PlaceMessage(t_full, t_min.y, t_max.y);
    }

    // 문구의 홈은 화면 중앙이다 — 시선이 처음 닿는 자리라, 비켜야 할 이유가 없으면 옮기지 않는다.
    // 다만 안내는 승격된 타깃보다 위(352)에 그려지므로 겹치면 강조 대상을 문구판이 그대로 덮는다.
    // 그래서 세로로 겹칠 때만, 여유가 더 넓은 쪽으로 최소한만 물러난다.
    void PlaceMessage(Rect _full, float _targetYMin, float _targetYMax)
    {
        if (this.messageRect == null || !this.messageRect.gameObject.activeSelf) return;

        // 하단이 홈인 스텝은 피할 것을 이미 저작이 정했다 — 자동 회피가 도로 가운데로 끌어올리지 않게 한다.
        if (m_atBottom) { HomeMessage(); return; }

        float t_half = this.messageRect.sizeDelta.y * 0.5f;
        float t_gap  = t_half + this.messageMargin;

        if (_targetYMin >= t_gap || _targetYMax <= -t_gap) { HomeMessage(); return; }

        float t_y = (_targetYMin - _full.yMin) >= (_full.yMax - _targetYMax)
            ? _targetYMin - t_gap    // 아래가 더 넓다
            : _targetYMax + t_gap;

        // 타깃이 화면을 거의 채우면 물러날 자리가 없다 — 겹치더라도 문구는 화면 안에 있어야 읽힌다.
        this.messageRect.anchoredPosition = new Vector2(0f, Mathf.Clamp(t_y, _full.yMin + t_gap, _full.yMax - t_gap));
    }

    // 문구가 돌아갈 자리. 기본은 화면 중앙이고, 하단 저작이면 화면 아래에 붙인다(가운데는 무대 몫이다).
    void HomeMessage()
    {
        if (this.messageRect == null) return;

        if (!m_atBottom || m_canvasRect == null)
        {
            this.messageRect.anchoredPosition = Vector2.zero;
            return;
        }

        float t_y = m_canvasRect.rect.yMin + this.messageRect.sizeDelta.y * 0.5f + this.messageBottomInset;
        this.messageRect.anchoredPosition = new Vector2(0f, t_y);
    }

    // ── 타깃 승격 ────────────────────────────────────────────────────────────

    // 강조할 것들에 중첩 Canvas를 얹어 딤 위로 올린다. 멱등(RefreshVisibility가 매 프레임 부른다).
    void Promote()
    {
        if (m_promoted || m_target == null) return;

        m_promoted = true;

        CollectHighlights();
        for (int t_i = 0; t_i < m_cardBuffer.Count; t_i++) PromoteOne(m_cardBuffer[t_i].gameObject);

        if (m_cardBuffer.Count == 0) PromoteOne(m_target.gameObject);
    }

    // 설명 스텝은 영역째가 아니라 그 안의 카드만 올린다 — "이게 네 덱이다"는 카드만 보이면 성립하고,
    // 패널 프레임·수치·버튼까지 딸려 올라오면 무엇을 보라는 것인지 흐려진다.
    // 클릭 스텝은 제외한다: 눌러야 할 것은 버튼이지 카드가 아니라, 카드만 올리면 정작 누를 것이 딤 아래 남는다.
    // 마스크 안의 카드(소유 카드 스크롤)도 제외 — 승격이 RectMask2D 클리핑을 끊어 카드가 뷰포트 밖으로 샌다.
    void CollectHighlights()
    {
        m_cardBuffer.Clear();
        if (!m_confirmMode) return;

        m_target.GetComponentsInChildren(m_cardBuffer);   // 비활성 제외 = 빈 슬롯의 카드는 애초에 빠진다

        for (int t_i = 0; t_i < m_cardBuffer.Count; t_i++)
        {
            if (!IsClipped(m_cardBuffer[t_i].transform)) continue;

            m_cardBuffer.Clear();   // 하나라도 잘리면 영역째 올리는 원래 방식으로 되돌린다
            return;
        }
    }

    // 타깃과 카드 사이에 클리핑 마스크가 끼어 있는가.
    bool IsClipped(Transform _card)
    {
        for (var t_t = _card.parent; t_t != null && t_t != m_target; t_t = t_t.parent)
            if (t_t.GetComponent<RectMask2D>() != null || t_t.GetComponent<Mask>() != null) return true;

        return false;
    }

    void PromoteOne(GameObject _go)
    {
        var t_root = m_targetCanvas != null ? m_targetCanvas.rootCanvas : null;

        var t_promotion = new Promotion { Canvas = _go.GetComponent<Canvas>() };
        t_promotion.AddedCanvas = t_promotion.Canvas == null;

        if (t_promotion.AddedCanvas)
        {
            t_promotion.Canvas = _go.AddComponent<Canvas>();
        }
        else
        {
            // 저작된 Canvas는 지우지 않는다 — 원래 정렬값만 백업해 두고 복원한다.
            t_promotion.PrevOverrideSorting = t_promotion.Canvas.overrideSorting;
            t_promotion.PrevSortingOrder    = t_promotion.Canvas.sortingOrder;
            t_promotion.PrevSortingLayerID  = t_promotion.Canvas.sortingLayerID;
        }

        t_promotion.Canvas.overrideSorting = true;
        t_promotion.Canvas.sortingOrder    = TargetOrder;

        // 중첩 Canvas는 정렬 레이어와 셰이더 채널을 기본값으로 리셋한다 — 루트에서 복사하지 않으면
        // 승격 중에만 TMP·그라디언트가 깨진다(Normal/Tangent 채널 손실).
        if (t_root != null)
        {
            t_promotion.Canvas.sortingLayerID           = t_root.sortingLayerID;
            t_promotion.Canvas.additionalShaderChannels = t_root.additionalShaderChannels;
        }

        // 메시지 모드는 딤 탭이 유일한 완료 경로다 — 레이캐스터를 붙이면 승격된 영역이 탭을 삼켜 진행이 막힌다.
        // 중첩 Canvas에 레이캐스터가 없으면 그 아래 그래픽은 레이캐스트에서 빠져 탭이 딤까지 내려간다(보이기만 한다).
        t_promotion.AddedRaycaster = !m_confirmMode && _go.GetComponent<GraphicRaycaster>() == null;
        if (t_promotion.AddedRaycaster) _go.AddComponent<GraphicRaycaster>();

        m_promotions.Add(t_promotion);
    }

    // 승격 해제. 타깃이 비활성이거나 파괴된 뒤에도 안전해야 한다(탭 버튼은 클릭 즉시 SetActive(false)된다).
    void Demote()
    {
        if (!m_promoted) return;
        m_promoted = false;

        for (int t_i = 0; t_i < m_promotions.Count; t_i++)
        {
            var t_promotion = m_promotions[t_i];
            if (t_promotion.Canvas == null) continue;   // 대상이 이미 파괴됨

            // 파괴 순서 고정: GraphicRaycaster가 Canvas를 RequireComponent하므로 Canvas를 먼저 지우면
            // 조용히 실패해 둘 다 남는다.
            if (t_promotion.AddedRaycaster)
            {
                var t_raycaster = t_promotion.Canvas.GetComponent<GraphicRaycaster>();
                if (t_raycaster != null) Destroy(t_raycaster);
            }

            if (t_promotion.AddedCanvas)
            {
                Destroy(t_promotion.Canvas);
            }
            else
            {
                t_promotion.Canvas.overrideSorting = t_promotion.PrevOverrideSorting;
                t_promotion.Canvas.sortingOrder    = t_promotion.PrevSortingOrder;
                t_promotion.Canvas.sortingLayerID  = t_promotion.PrevSortingLayerID;
            }
        }

        m_promotions.Clear();
    }

    // ── 표시 토글 ────────────────────────────────────────────────────────────

    // 딤은 켜고 끄는 것이 곧 입력 차단의 켜고 끔이다. 둘을 따로 두면 "안 보이는데 막힌" 상태가 생긴다.
    // 색까지 투명으로 내리는 이유: 컴포넌트만 꺼 두면 무언가 다시 켜는 순간 어두운 판이 그대로 돌아온다.
    // 주의: 딤이 막는 것은 EventSystem 입력뿐이다 — raw Input을 폴링하는 코드는 오히려 과차단된다.
    // 딤은 "보이는가"와 "입력을 막는가"가 갈린다. 메시지 모드는 완료가 화면 탭이라 투명해도 반드시 받아야 하고,
    // 딤 없는 클릭 스텝은 반대로 아무것도 막지 않아야 타깃이 눌린다(승격도 없으므로 막으면 그대로 잠긴다).
    void SetBlocker(bool _visible, bool _blockInput)
    {
        if (this.blocker == null) return;

        this.blocker.enabled       = _visible || _blockInput;
        this.blocker.raycastTarget = _blockInput;
        this.blocker.color         = _visible ? m_dimColor : new Color(m_dimColor.r, m_dimColor.g, m_dimColor.b, 0f);
    }

    void SetPointerActive(bool _on)
    {
        bool t_hand = _on && !m_confirmMode;   // 메시지 모드는 읽을 영역이라 손가락을 띄우지 않는다(승격만으로 강조)

        if (this.hand != null && this.hand.gameObject.activeSelf != t_hand) this.hand.gameObject.SetActive(t_hand);

        if (t_hand) StartPulse();
        else        StopPulse();
    }

    // 문구는 항상 홈(중앙, 저작이 하단이면 하단)에서 출발한다. 타깃을 피해 물러나는 판단은 Layout(PlaceMessage)
    // 하나가 맡는다 — 따라갈 타깃이 없는 모드(배너·앵커 없는 메시지)는 그래서 홈에 그대로 남는다.
    void SetMessage(string _message)
    {
        bool t_has = !string.IsNullOrEmpty(_message);

        if (this.messageRect != null)
        {
            this.messageRect.gameObject.SetActive(t_has);
            HomeMessage();
        }

        if (t_has && this.messageText != null) this.messageText.text = _message;
    }

    // localScale만 건드린다 — sizeDelta·anchoredPosition은 Layout이 매 프레임 덮어써 트윈이 조용히 사라진다.
    void StartPulse()
    {
        if (this.pulseDuration <= 0f) return;

        // 숨겨 둔 손가락(메시지 모드)까지 돌릴 이유가 없다 — SetPointerActive가 먼저 활성 여부를 확정한다.
        if (this.hand != null && this.hand.gameObject.activeSelf && m_handTween == null)
            m_handTween = this.hand.DOScale(this.pulseScale, this.pulseDuration)
                .SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo).SetLink(this.hand.gameObject);
    }

    // SetLink는 파괴에만 반응하고 비활성화에는 반응하지 않는다 → 숨길 때 직접 죽이고 스케일을 되돌린다.
    void StopPulse()
    {
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
        HideGateInternal();  // 콜백이 다음 게이트를 걸 수 있도록 먼저 정리
        m_onSatisfied = null;
        t_callback?.Invoke();
    }

    // 메시지 모드의 완료 = 딤 탭. 다른 모드에서 딤을 눌러도 아무 일이 없어야 하므로 모드 플래그로 한 번 더 막는다.
    void OnBlockerClicked()
    {
        if (!m_confirmMode || m_satisfied) return;
        m_satisfied = true;

        Action t_callback = m_onSatisfied;
        HideGateInternal();  // 콜백이 다음 게이트를 걸 수 있도록 먼저 정리
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
        m_atBottom    = false;  // 자리 저작도 스텝의 것이다 — 남기면 다음 문구가 이유 없이 아래에 선다.
        m_dim         = true;   // 딤 없는 스텝이 그 다음 모드로 새지 않게. 이 값은 두 Show*가 저작대로 덮는다.
    }

    // 딤 탭 수신을 켠다. Release()가 항상 먼저 떼므로 중복 부착은 없지만, 단일 창구를 지키려 여기서도 한 번 뗀다.
    void ArmBlockerClick()
    {
        if (m_blockerButton == null) return;

        m_blockerButton.onClick.RemoveListener(OnBlockerClicked);
        m_blockerButton.onClick.AddListener(OnBlockerClicked);
    }

    // ── 초기화 ──────────────────────────────────────────────────────────────

    // 딤 색의 정본은 프리팹 저작값이다 — 끌 때 알파를 0으로 덮으므로 원본을 여기서 한 번 떠 둔다.
    void CacheDimColor()
    {
        if (this.blocker == null) return;

        m_dimColor = this.blocker.color;
    }

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

    // 손가락·문구를 승격된 타깃(351)보다 위(352)로 올린다. 게이트 캔버스 그대로 두면 타깃이 딤 위로 올라오면서
    // 자기 위에 겹친 손가락을 덮어버린다(타깃 중앙을 가리키는 손가락이 특히 통째로 사라진다).
    // blocker는 제외 — 딤까지 올리면 타깃이 다시 딤에 묻혀 누를 수 없게 된다.
    // GraphicRaycaster는 일부러 붙이지 않는다: 레이캐스터 없는 중첩 Canvas의 그래픽은 레이캐스트 대상에서 아예 빠져
    // 안내가 타깃 클릭을 가로챌 수 없다(raycastTarget=false와 이중 안전장치).
    void LiftOrnaments()
    {
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

        ClearRaycast(this.hand);
        ClearRaycast(this.messageRect);
    }

    static void ClearRaycast(Component _root)
    {
        if (_root == null) return;

        var t_graphics = _root.GetComponentsInChildren<Graphic>(true);
        for (int t_i = 0; t_i < t_graphics.Length; t_i++) t_graphics[t_i].raycastTarget = false;
    }

    // 프리팹 미배선 폴백. 손가락 스프라이트는 Resources 밖(Layer Lab ResourcesData)이라 코드로 얻을 수 없다
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

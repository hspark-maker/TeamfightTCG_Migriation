using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 튜토리얼 인게임 오버레이 — 스크립트 스텝 순서를 따라 안내 문구를 순차로 띄운다.
/// **연출/입력 게이트 전용**: 전투 규칙/RNG/결정 지점 무접촉. 스텝 소비의 단일 진실원은
/// <see cref="TutorialConfig"/> 큐 — 이 UI는 표시와 탭 대기만 한다.
/// 싱글 튜토리얼(<see cref="TutorialConfig.IsActive"/>)에서만 <see cref="Ensure"/>로 생성.
///
/// 기능:
/// - 순차 배너: 스텝마다 문구 교체(fade+pop).
/// - 탭 진행: 마스크 활성 구간에서 화면 탭으로 다음 진행(<see cref="WaitForTapAsync"/>).
/// - 입력 마스크: 마스크는 uGUI raycast를 흡수한다. **주의: 카드 입력은 Physics2D
///   `OnMouseDown`이라 uGUI 마스크로 차단되지 않는다** — 실제 카드 입력 차단은 호출측이
///   `TurnState.InputAllowed=false`로 하며, 마스크는 (1)탭 감지 (2)배경 어둡게(darken) 담당.
///   darken 여부(<see cref="ScriptedAttack.dimBackground"/>)와 무관하게 마스크는 탭을 받는다.
/// - 포인터: 공격 스텝에서 공격자 카드에 hintArrow(드래그 방향) 표시 + 양측 슬롯 하이라이트.
/// 캔버스는 코드 빌드(프리팹 불필요, TutorialSetupUI 선례).
/// </summary>
public class TutorialOverlayUI : MonoBehaviour
{
    public static TutorialOverlayUI Instance { get; private set; }

    // 프리팹으로 저작/수정 가능하도록 직렬화. 프리팹이 있으면 이 참조들이 이미 배선된 채 로드되고,
    // 없으면 Awake에서 BuildUI가 코드로 생성해 채운다(폴백). 프리팹 재생성은 아래 에디터 메뉴 참조.
    [SerializeField] TextMeshProUGUI  banner;
    [SerializeField] CanvasGroup      bannerGroup;
    [SerializeField] Image            dimMask;       // 풀스크린 어둡게 + 입력 차단 + 탭 감지
    [SerializeField] CanvasGroup      dimGroup;
    [SerializeField] TutorialTapCatcher tapCatcher;  // OnDown/OnUp 콜백은 런타임 배선(delegate는 직렬화 안 됨)
    [SerializeField] GameObject       tapHint;       // "탭하여 계속" 힌트. 배너와 함께 표시/숨김(null 허용)

    // dim 어둡기 세기. Image 색 alpha가 아니라 CanvasGroup alpha로 제어 → 프리팹의 Image 투명도와 무관하게
    // 항상 동일한 어둡기 보장. (프리팹에서 DimMask Image alpha를 0으로 저장해도 dim 정상 작동.)
    const float DimStrength = 0.6f;

    // 배너가 설 세 자리(캔버스 단위, 기준 해상도 1080x1920). 앵커/피벗은 자리마다 정해져 있고
    // 여기 값은 그 기준점에서의 보정이다 — Top은 화면 위에서 아래로, Bottom은 화면 아래에서 위로.
    // **좌표를 코드에 박아 두지 않는 이유**: 저작이 눈으로 잡아야 하는 값인데 코드 상수면 빌드를 거쳐야 보인다.
    // 프리팹에서 이 셋을 옮기면 그대로 반영된다(Banner RectTransform을 직접 옮기는 건 소용없다 —
    // 표시할 때마다 아래 SetBannerAnchor가 자리를 다시 잡는다).
    [Header("배너 위치 (자리별 보정 — 프리팹에서 잡는다)")]
    [Tooltip("맨위: 화면 최상단 기준. y가 음수일수록 아래로 내려온다")]
    [SerializeField] Vector2 bannerTopPos         = new Vector2(0f, -160f);
    [Tooltip("중간위: 맨위와 같은 화면 상단 기준. y가 음수일수록 아래로 내려온다(맨위보다 더 내린 자리)")]
    [SerializeField] Vector2 bannerUpperMiddlePos = Vector2.zero;
    [Tooltip("중간: 화면 정중앙 기준")]
    [SerializeField] Vector2 bannerCenterPos      = Vector2.zero;
    [Tooltip("중간아래: 완전아래와 같은 화면 하단 기준. y가 양수일수록 위로 올라온다(완전아래보다 더 올린 자리)")]
    [SerializeField] Vector2 bannerLowerMiddlePos = Vector2.zero;
    [Tooltip("완전아래: 화면 최하단 기준. y가 양수일수록 위로 올라온다")]
    [SerializeField] Vector2 bannerBottomPos      = new Vector2(0f, 160f);

    bool tapped;      // dim 활성 중 탭(release) 발생 플래그(WaitForTapAsync가 소비)
    bool tapArmed;    // 탭 대기 활성 구간(이 구간에서 시작된 press만 인정)
    bool pressActive; // 이번 대기 구간 안에서 pointer down이 발생했는가
    bool inspected; // Inspect 대기 중 적 카드 롱프레스 발생 플래그(WaitForInspectAsync가 소비)

    // 필드 포커스(구멍 뚫린 딤). 풀스크린 dimMask로는 특정 영역만 남길 수 없어서 4패널로 감싼다 —
    // 구멍이 물리적 공백이라 셰이더/마스크 없이 성립하고, 그 자리만 밝게 남는다
    // (아웃게임 OutgameTutorialGateUI와 같은 방식). 프리팹 배선이 필요 없도록 최초 사용 시 코드로 만든다.
    CanvasGroup     focusGroup;
    RectTransform[] focusPanels;    // 0=Top 1=Bottom 2=Left 3=Right
    RectTransform[] focusCorners;   // 0=BL 1=BR 2=TR 3=TL — 구멍 모서리를 덮어 라운드로 보이게

    [Tooltip("포커스 구멍 모서리 라운드 반지름(캔버스 단위, 기준 해상도 1080x1920). 0이면 각진 사각")]
    [SerializeField] float focusCornerRadius = 36f;

    [Header("터치 유도 손 아이콘")]
    [Tooltip("눌러야 할 자리에 떠 있는 손. 비우면 손 표시 자체를 생략한다(무동작 안전)")]
    [SerializeField] Sprite handSprite;
    [SerializeField] float  handSize      = 260f;   // 캔버스 단위(1080x1920 기준). 작으면 전투 화면에서 묻힌다
    [SerializeField] float  handBobHeight = 34f;    // 위아래 떠 있는 폭
    [SerializeField] float  handTapPeriod = 1.1f;   // 한 번 누르는 주기(초)

    [Tooltip("목표 지점 기준 손 위치 보정(캔버스 단위). 오른쪽 아래로 빼면 손이 대상을 덜 가린다")]
    [SerializeField] Vector2 handOffset = new Vector2(34f, -24f);

    // 손은 프리팹 배선 없이 최초 사용 시 만든다(포커스 패널과 같은 규약).
    RectTransform handRect;
    CanvasGroup   handGroup;
    Sequence      handSeq;

    // 역필렛 조각 4장(호 중심 위치별). 구멍 안쪽 모서리를 덮는 부분만 불투명이라
    // 겹쳐 놓으면 사각 구멍이 라운드 구멍이 된다. 전 인스턴스 공용.
    static readonly Sprite[] s_cornerSprites = new Sprite[4];

    // 직전 스텝에서 켠 하이라이트/포인터(교체/정리 시 되돌리기 위해 추적).
    CardView highlightedAttacker;
    CardView highlightedTarget;
    CardView pointerCard;

    /// <summary>튜토리얼 활성 시 오버레이를 1회 생성. 이미 있으면 재사용.
    /// <paramref name="_prefab"/>이 지정되면 그것을 인스턴스화(디자이너 저작 프리팹),
    /// 없으면 코드로 빌드(<see cref="BuildUI"/> 폴백). Instance는 어느 경로든 Awake에서 설정.
    /// 프리팹은 호출측(GameInitializer)이 [SerializeField]로 보유해 전달한다.</summary>
    public static TutorialOverlayUI Ensure(TutorialOverlayUI _prefab = null)
    {
        if (Instance != null) return Instance;
        if (_prefab != null) { Instantiate(_prefab).name = "TutorialOverlay"; return Instance; }
        new GameObject("TutorialOverlay").AddComponent<TutorialOverlayUI>();
        return Instance;
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (this.dimMask == null) BuildUI();   // 프리팹 참조 미배선 = 코드 빌드 폴백
        WireTapCatcher();                       // 탭 콜백은 항상 런타임 배선(프리팹/코드 공통)
        ResetToHidden();                        // 프리팹 저작 alpha가 1이어도 시작 시 강제 숨김
        EnsureEventSystem();
    }

    // 시작 시 배너/마스크를 즉시 숨김(트윈 없음). 프리팹에서 CanvasGroup alpha=1로 저장돼 있어도
    // 첫 스텝 표시 전까지 배너/TapHint/dim이 보이지 않도록 한다. 코드 빌드 경로에도 무해.
    void ResetToHidden()
    {
        if (this.bannerGroup != null) this.bannerGroup.alpha = 0f;
        if (this.tapHint != null) this.tapHint.SetActive(false);
        if (this.dimGroup != null)
        {
            this.dimGroup.alpha          = 0f;
            this.dimGroup.blocksRaycasts = false;
        }
        if (this.dimMask != null)
        {
            this.dimMask.raycastTarget = false;
            // 어둡기는 CanvasGroup alpha로만 제어하므로 Image는 불투명 검정으로 강제(저작 alpha 무시).
            this.dimMask.color = new Color(0f, 0f, 0f, 1f);
        }
    }

    // dim 마스크의 TapCatcher에 press/release 콜백 연결. delegate는 직렬화 안 되므로 런타임에서만.
    void WireTapCatcher()
    {
        if (this.tapCatcher == null && this.dimMask != null)
            this.tapCatcher = this.dimMask.GetComponent<TutorialTapCatcher>();
        if (this.tapCatcher != null)
        {
            this.tapCatcher.OnDown = OnMaskDown;
            this.tapCatcher.OnUp   = OnMaskUp;
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── 표시 API ────────────────────────────────────────────────────────────

    /// <summary>
    /// 설명/게이트 스텝. 배너 + 탭 대기용 마스크 활성(탭 감지). darken이면 배경 어둡게,
    /// 아니면 투명하되 탭은 여전히 받는다. 하이라이트/포인터 없음.
    /// </summary>
    public void ShowMessage(string _message, bool _darken, TutorialScenarioData.BannerAnchor _anchor)
    {
        ClearHighlight();
        ClearPointer();
        ActivateMask(_darken);
        HideHand();   // 화면 아무 데나 탭하는 구간 — 특정 지점을 가리키면 안 된다
        SetBannerAnchor(_anchor);
        SetBanner(_message, _showTapHint: true);   // 이 스텝의 진행 수단이 탭이다
    }

    /// <summary>
    /// 공격 스텝 안내. 배너 + 공격자/타깃 슬롯 하이라이트 + (showPointer면) 공격자에 드래그 포인터.
    /// 마스크는 끈다(카드 드래그를 받아야 하므로). attacker/target 슬롯 뷰는 null 허용.
    /// </summary>
    public void ShowAttack(string _message, CardView _attacker, CardView _target, bool _showPointer,
        TutorialScenarioData.BannerAnchor _anchor)
    {
        ClearHighlight();
        ClearPointer();

        this.highlightedAttacker = _attacker;
        this.highlightedTarget   = _target;
        _attacker?.SetHighlight(true);
        _target?.SetHighlight(true);

        if (_showPointer && _attacker != null)
        {
            this.pointerCard = _attacker;
            _attacker.SetTutorialPointer(true);
        }

        DeactivateMask();
        SetBannerAnchor(_anchor);
        SetBanner(_message, _showTapHint: false);   // 진행은 공격 입력 — 탭 안내는 오해를 준다

        // 슬롯 지정 스텝: 먼저 눌러야 하는 건 공격자 카드다. 지정이 없으면(자유공격) 손도 없다.
        ShowHandOn(_attacker);
    }

    /// <summary>dim 마스크가 활성인 동안 화면 탭을 대기. **손을 뗄 때(release)** 완료된다.
    /// 단, 이 대기 구간 안에서 새로 시작된 press의 release만 인정한다 — 직전 스텝/롱프레스에서부터
    /// 이어져 눌려 있던 손가락은 무시해 다이얼로그 즉시 스킵을 막는다. ct로 씬 파괴 취소.</summary>
    public async UniTask WaitForTapAsync(CancellationToken _ct)
    {
        this.tapped      = false;
        this.pressActive = false;
        this.tapArmed    = true;
        try
        {
            await UniTask.WaitUntil(() => this.tapped, cancellationToken: _ct);
        }
        finally
        {
            this.tapArmed    = false;
            this.tapped      = false;
            this.pressActive = false;
            // 탭이 소비된 순간 힌트는 역할이 끝났다. 다음 표시 호출이 꺼주길 기대하지 않는다 —
            // 그 사이(연출 대기·스텝 폐기 등)에 배너만 남고 힌트가 붙어 있는 프레임이 생긴다.
            if (this.tapHint != null) this.tapHint.SetActive(false);
        }
    }

    /// <summary>
    /// Inspect 안내. 배너만 띄우고 마스크는 끈다(적 카드 롱프레스를 받아야 하므로).
    /// 카드 입력은 Physics2D라 마스크와 무관 — 마스크를 켜면 어두워지기만 하므로 여기선 끈다.
    /// </summary>
    public void ShowInspect(string _message, TutorialScenarioData.BannerAnchor _anchor)
    {
        ClearHighlight();
        ClearPointer();
        DeactivateMask();
        HideHand();   // 롱프레스 유도라 탭 손은 오해를 준다
        SetBannerAnchor(_anchor);
        // Inspect는 탭이 아니라 롱프레스로 진행 → "탭하여 계속" 힌트 숨김(오해 방지).
        SetBanner(_message, _showTapHint: false);
    }

    /// <summary>Inspect 대기 중 적 카드 롱프레스 발생을 통지. 대기 중일 때만 의미 있다.</summary>
    public void NotifyInspected() => this.inspected = true;

    /// <summary>적 카드 롱프레스(정보확인)를 대기. 발생하면 완료. ct로 씬 파괴 취소.</summary>
    public async UniTask WaitForInspectAsync(CancellationToken _ct)
    {
        this.inspected = false;
        await UniTask.WaitUntil(() => this.inspected, cancellationToken: _ct);
        this.inspected = false;
    }

    /// <summary>
    /// 필드 포커스 안내. <paramref name="_screenRect"/>(화면 px)만 남기고 나머지를 딤으로 덮는다.
    /// 카드 입력은 Physics2D라 uGUI 패널로 막히지 않는다 — 여기서 하는 건 **어디를 볼지 알려주는 것뿐**이고,
    /// 무엇을 고를 수 있는지는 호출측이 TurnState로 정한다(자유 선택은 일부러 제한하지 않는다).
    ///
    /// 탭 힌트는 끈다 — 이 구간은 화면 탭이 아니라 카드 선택으로 진행한다.
    /// </summary>
    public void ShowFieldFocus(Rect _screenRect, string _message, TutorialScenarioData.BannerAnchor _anchor)
        => ShowFocus(_screenRect, _message, _anchor, _waitTap: false);

    /// <summary>카드 낱장 포커스. 그 카드만 남기고 배경·나머지 카드를 전부 딤으로 덮는다
    /// (다른 카드는 구멍 밖이라 패널에 덮인다 — 별도 스프라이트 암전이 필요 없다).
    ///
    /// <paramref name="_waitTap"/>=true면 탭으로 진행하는 설명 스텝용 —
    /// 딤 패널은 입력을 흘려보내고 투명한 풀스크린 마스크가 탭을 받는다(WaitForTapAsync와 짝).
    /// 여러 장을 넘기면 그 카드들을 **모두 감싸는 하나의 구멍**이 된다 — 떨어진 카드끼리 묶으면
    /// 사이 공백까지 뚫리니 낱장 강조가 목적이면 한 장만 넘길 것.</summary>
    public void ShowCardFocus(string _message, TutorialScenarioData.BannerAnchor _anchor, bool _waitTap,
        params CardView[] _cards)
    {
        Rect t_rect = new Rect();
        if (_cards != null)
            foreach (CardView t_card in _cards)
                if (t_card != null) t_rect = CameraUtil.Union(t_rect, t_card.ScreenBounds(CardFocusPadding));

        ShowFocus(t_rect, _message, _anchor, _waitTap);
    }

    const float CardFocusPadding = 18f;   // 카드 구멍 여유(px) — 프레임 장식이 딤에 물리지 않게

    void ShowFocus(Rect _screenRect, string _message, TutorialScenarioData.BannerAnchor _anchor, bool _waitTap)
    {
        ClearHighlight();
        ClearPointer();

        // 탭 대기 구간에서만 풀스크린 마스크를 **투명하게** 켠다(어둡기는 패널이 담당, 마스크는 탭만 받는다).
        // 둘 다 어둡게 켜면 구멍까지 덮여 포커스가 무의미해진다.
        if (_waitTap) ActivateMask(false);
        else          DeactivateMask();

        if (_screenRect.width <= 0f || _screenRect.height <= 0f) { ClearFieldFocus(); }
        else
        {
            EnsureFocusPanels();
            LayoutFocusPanels(_screenRect);
            // 탭 모드면 패널이 raycast를 삼키지 않아야 아래 마스크가 탭을 받는다.
            foreach (RectTransform t_panel in this.focusPanels)
                t_panel.GetComponent<Image>().raycastTarget = !_waitTap;
            foreach (RectTransform t_corner in this.focusCorners)
                t_corner.GetComponent<Image>().raycastTarget = !_waitTap;

            this.focusGroup.gameObject.SetActive(true);
            this.focusGroup.blocksRaycasts = !_waitTap;
            this.focusGroup.DOKill();
            this.focusGroup.DOFade(DimStrength, 0.15f).SetLink(this.focusGroup.gameObject);
        }

        SetBannerAnchor(_anchor);
        // 탭으로 진행하는 구간만 "탭하여 계속" 표시. 카드 선택으로 진행하는 구간에 띄우면 오해를 준다.
        SetBanner(_message, _showTapHint: _waitTap);

        // 손은 **그 자리를 눌러야 진행되는 구간**에만. 화면 아무 데나 탭하면 되는 구간(_waitTap)에
        // 특정 지점을 가리키면 거기만 눌러야 하는 줄 알게 된다.
        if (!_waitTap && _screenRect.width > 0f) ShowHandAt(_screenRect.center);
        else                                     HideHand();
    }

    /// <summary>눌러야 할 화면 좌표(px) 위에 손 아이콘을 띄운다. 위아래로 떠 있다가 주기적으로 누르는 동작.
    /// 스프라이트가 미배선이면 아무것도 하지 않는다(무동작 안전).</summary>
    public void ShowHandAt(Vector2 _screenPos)
    {
        if (this.handSprite == null) return;
        EnsureHand();

        // 캔버스가 화면 전체 stretch → 정규화 앵커가 곧 화면 비율. 해상도·CanvasScaler와 무관하게 맞는다
        // (포커스 패널과 같은 방식 — 픽셀 변환을 두 군데서 각자 하면 한쪽이 조용히 어긋난다).
        this.handRect.anchorMin = this.handRect.anchorMax = new Vector2(
            Mathf.Clamp01(_screenPos.x / Screen.width),
            Mathf.Clamp01(_screenPos.y / Screen.height));
        this.handRect.anchoredPosition = this.handOffset;

        this.handRect.gameObject.SetActive(true);
        this.handGroup.DOKill();
        this.handGroup.alpha = 0f;
        this.handGroup.DOFade(1f, 0.15f).SetLink(this.handRect.gameObject);

        PlayHandLoop();
    }

    /// <summary>카드 위에 손을 띄운다. 카드가 null이면 숨긴다.</summary>
    public void ShowHandOn(CardView _card)
    {
        if (_card == null) { HideHand(); return; }
        Rect t_r = _card.ScreenBounds();
        if (t_r.width <= 0f) { HideHand(); return; }
        ShowHandAt(t_r.center);
    }

    public void HideHand()
    {
        if (this.handRect == null) return;
        this.handSeq?.Kill();
        this.handSeq = null;
        this.handGroup.DOKill();
        this.handRect.gameObject.SetActive(false);
    }

    /// <summary>떠 있기(위아래) + 누르는 축소를 한 몸으로 반복.
    ///
    /// <b>반주기만 만들고 Yoyo로 되돌린다.</b> 왕복을 직접 이어 붙이고 Restart로 돌리면
    /// 끝 상태(내려온 y·눌린 scale)에서 시작 상태로 **순간 복귀**해 루프 이음매가 튄다.
    /// Yoyo는 같은 트윈을 역재생하므로 값도 속도도 자동으로 이어진다.
    ///
    /// 이즈가 InOutSine인 이유: 양 끝 속도가 0이라 방향이 바뀌는 지점(맨 위·맨 아래)에서
    /// 꺾임이 보이지 않는다. In/Out을 짝지어 쓰면 한쪽 끝은 최대 속도로 부딪힌다.</summary>
    void PlayHandLoop()
    {
        this.handSeq?.Kill();
        this.handRect.DOKill();

        // 시작 = 눌린 상태(손끝이 대상에 닿아 있음). 여기서 떠올랐다가 되돌아온다.
        // 기준선은 handOffset — 위아래 폭은 그 자리에서 재므로 보정을 바꿔도 진폭은 그대로다.
        this.handRect.anchoredPosition = this.handOffset;
        this.handRect.localScale       = Vector3.one * HandPressScale;

        float t_half = this.handTapPeriod * 0.5f;

        this.handSeq = DOTween.Sequence()
            .SetLink(this.handRect.gameObject)
            .SetLoops(-1, LoopType.Yoyo);
        this.handSeq.Append(this.handRect
            .DOAnchorPosY(this.handOffset.y + this.handBobHeight, t_half).SetEase(Ease.InOutSine));
        this.handSeq.Join(this.handRect.DOScale(1f, t_half).SetEase(Ease.InOutSine));
    }

    const float HandPressScale = 0.86f;   // 누른 순간(맨 아래) 배율

    void EnsureHand()
    {
        if (this.handRect != null) return;

        Transform t_canvas = this.dimMask != null ? this.dimMask.transform.parent : transform;
        var t_go = new GameObject("TutorialHand", typeof(RectTransform));
        t_go.transform.SetParent(t_canvas, false);
        t_go.transform.SetAsLastSibling();   // 딤·배너보다 위 — 가려지면 유도가 안 된다

        this.handRect = (RectTransform)t_go.transform;
        // 피벗을 손끝(위쪽 중앙)에 둔다 — 아이콘의 검지가 위를 가리키므로 이 점이 실제 터치 지점이 된다.
        this.handRect.pivot      = new Vector2(0.5f, 1f);
        this.handRect.sizeDelta  = new Vector2(this.handSize, this.handSize);

        this.handGroup = t_go.AddComponent<CanvasGroup>();
        this.handGroup.blocksRaycasts = false;   // 손이 입력을 먹으면 정작 그 자리를 못 누른다
        this.handGroup.interactable   = false;

        var t_img = t_go.AddComponent<Image>();
        t_img.sprite        = this.handSprite;
        t_img.raycastTarget = false;
        t_img.preserveAspect = true;

        t_go.SetActive(false);
    }

    /// <summary>필드 포커스 해제(딤 패널만 접는다 — 배너는 호출측 판단).</summary>
    public void ClearFieldFocus()
    {
        HideHand();
        if (this.focusGroup == null) return;
        this.focusGroup.blocksRaycasts = false;
        this.focusGroup.DOKill();
        this.focusGroup.DOFade(0f, 0.15f)
            .SetLink(this.focusGroup.gameObject)
            .OnComplete(() => { if (this.focusGroup != null) this.focusGroup.gameObject.SetActive(false); });
    }

    /// <summary>배너 숨기고 마스크 끄고 하이라이트/포인터 되돌림(턴 종료/정리).</summary>
    public void Clear()
    {
        ClearHighlight();
        ClearPointer();
        DeactivateMask();
        ClearFieldFocus();
        HideBanner();
    }

    // ── 내부 ────────────────────────────────────────────────────────────────

    /// <summary>배너 표시. <paramref name="_showTapHint"/>는 **호출부가 반드시 정한다** —
    /// 예전엔 여기서 무조건 켜고 각 호출부가 알아서 껐는데, 안 끄는 경로(ShowAttack)가 있어
    /// 탭으로 진행하지 않는 스텝에도 "탭하여 계속"이 남았다. 켜는 판단을 한 곳으로 모은다.</summary>
    void SetBanner(string _message, bool _showTapHint)
    {
        if (string.IsNullOrWhiteSpace(_message)) { HideBanner(); return; }

        if (this.tapHint != null) this.tapHint.SetActive(_showTapHint);
        this.banner.text = _message;
        this.bannerGroup.DOKill();
        this.banner.transform.DOKill();
        this.bannerGroup.alpha = 0f;
        this.banner.transform.localScale = Vector3.one * 0.92f;
        this.bannerGroup.DOFade(1f, 0.2f).SetLink(this.bannerGroup.gameObject);
        this.banner.transform.DOScale(1f, 0.2f).SetEase(Ease.OutBack).SetLink(this.banner.gameObject);
    }

    /// <summary>배너를 화면 위/가운데/아래로 옮긴다. 포커스가 아군 필드(하단)로 내려가면
    /// 배너가 그 위를 덮지 않도록 저작이 위치를 지정한다. 앵커·피벗을 같이 바꿔 화면 밖으로 새지 않게.
    ///
    /// **모든 표시 경로가 이걸 부른다.** 예전엔 포커스 스텝만 불러서, 한 번 자리가 옮겨지면 그 뒤의
    /// 일반 메시지·공격 안내가 그 자리를 그대로 물려받아 스텝마다 배너가 튀었다(되돌리는 쪽이 없었다).
    /// 자리는 스텝 데이터(<see cref="TutorialScenarioData.ScriptedAttack.bannerAnchor"/>)가 정한다.</summary>
    void SetBannerAnchor(TutorialScenarioData.BannerAnchor _anchor)
    {
        if (this.bannerGroup == null) return;

        // **윗동네는 화면 위쪽에서, 아랫동네는 화면 아래쪽에서 잰다.**
        // 맨위/중간위는 상단(1) 기준으로 "위에서 얼마나 내려왔나", 중간아래/완전아래는 하단(0) 기준으로
        // "아래에서 얼마나 올라왔나". 화면 비율(0.75/0.25)로 띄우면 세로 비율이 다른 기기에서
        // 같은 숫자가 다른 여백이 되고, 저작이 "위에서 N만큼"으로 생각하는 방식과도 어긋난다.
        // 가운데만 정중앙(0.5) 기준이다.
        switch (_anchor)
        {
            case TutorialScenarioData.BannerAnchor.UpperMiddle:
                ApplyBannerSlot(1f,   new Vector2(0.5f, 1f),   this.bannerUpperMiddlePos);
                break;
            case TutorialScenarioData.BannerAnchor.Center:
                ApplyBannerSlot(0.5f, new Vector2(0.5f, 0.5f), this.bannerCenterPos);
                break;
            case TutorialScenarioData.BannerAnchor.LowerMiddle:
                ApplyBannerSlot(0f,   new Vector2(0.5f, 0f),   this.bannerLowerMiddlePos);
                break;
            case TutorialScenarioData.BannerAnchor.Bottom:
                ApplyBannerSlot(0f,   new Vector2(0.5f, 0f),   this.bannerBottomPos);
                break;
            default:   // Top — BuildUI 기본값과 동일
                ApplyBannerSlot(1f,   new Vector2(0.5f, 1f),   this.bannerTopPos);
                break;
        }
    }

    /// <summary>배너를 화면 세로 비율 <paramref name="_anchorY"/> 자리에 세운다(0 = 맨 아래, 1 = 맨 위).</summary>
    void ApplyBannerSlot(float _anchorY, Vector2 _pivot, Vector2 _offset)
    {
        var t_rect = (RectTransform)this.bannerGroup.transform;
        t_rect.anchorMin = t_rect.anchorMax = new Vector2(0.5f, _anchorY);
        t_rect.pivot            = _pivot;
        t_rect.anchoredPosition = _offset;
    }

    // 4패널을 최초 1회 생성. 배너보다 **먼저** 넣어 배너가 항상 딤 위에 그려지게 한다.
    void EnsureFocusPanels()
    {
        if (this.focusGroup != null) return;

        Transform t_canvas = this.dimMask != null ? this.dimMask.transform.parent : transform;

        var t_root = new GameObject("FieldFocus", typeof(RectTransform));
        t_root.transform.SetParent(t_canvas, false);
        var t_rootRect = (RectTransform)t_root.transform;
        t_rootRect.anchorMin = Vector2.zero;
        t_rootRect.anchorMax = Vector2.one;
        t_rootRect.offsetMin = t_rootRect.offsetMax = Vector2.zero;
        // dim 바로 뒤(배너 앞). 배너는 뒤에 생성돼 있으므로 여기서 앞으로 당기면 배너가 가려진다.
        int t_dimIndex = this.dimMask != null ? this.dimMask.transform.GetSiblingIndex() : 0;
        t_root.transform.SetSiblingIndex(t_dimIndex + 1);

        this.focusGroup = t_root.AddComponent<CanvasGroup>();
        this.focusGroup.alpha = 0f;
        this.focusGroup.blocksRaycasts = false;
        t_root.SetActive(false);

        this.focusPanels = new RectTransform[4];
        for (int i = 0; i < 4; i++)
        {
            var t_go = new GameObject("Panel" + i, typeof(RectTransform));
            t_go.transform.SetParent(t_root.transform, false);
            var t_img = t_go.AddComponent<Image>();
            t_img.color = new Color(0f, 0f, 0f, 1f);   // 어둡기는 CanvasGroup alpha로만(dimMask와 같은 규약)
            t_img.raycastTarget = true;                // 구멍 밖 UI 클릭 차단
            this.focusPanels[i] = (RectTransform)t_go.transform;
        }

        // 모서리 조각. 회전 대신 방향별로 스프라이트를 따로 굽는다 —
        // 회전은 피벗과 얽혀 배치가 헷갈리고, 64px 텍스처 4장은 사실상 공짜다.
        this.focusCorners = new RectTransform[4];
        for (int i = 0; i < 4; i++)
        {
            var t_go = new GameObject("Corner" + i, typeof(RectTransform));
            t_go.transform.SetParent(t_root.transform, false);
            var t_img = t_go.AddComponent<Image>();
            t_img.sprite = CornerSprite(i);
            t_img.color  = new Color(0f, 0f, 0f, 1f);
            t_img.raycastTarget = true;
            this.focusCorners[i] = (RectTransform)t_go.transform;
        }
    }

    // 구멍 모서리(0=BL 1=BR 2=TR 3=TL)에 덮을 역필렛. 호 중심은 조각의 **구멍 안쪽 방향** 꼭짓점이고,
    // 그 원 안쪽만 투명하다 → 조각이 모서리의 각진 부분만 덮어 구멍이 둥글어진다.
    static Sprite CornerSprite(int _index)
    {
        if (s_cornerSprites[_index] != null) return s_cornerSprites[_index];

        const int c_size = 64;
        bool t_centerRight = _index == 0 || _index == 3;   // BL·TL 조각은 호 중심이 오른쪽
        bool t_centerTop   = _index == 0 || _index == 1;   // BL·BR 조각은 호 중심이 위쪽

        var t_tex = new Texture2D(c_size, c_size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var t_px  = new Color[c_size * c_size];
        float t_cx = t_centerRight ? c_size : 0f;
        float t_cy = t_centerTop   ? c_size : 0f;

        for (int t_y = 0; t_y < c_size; t_y++)
            for (int t_x = 0; t_x < c_size; t_x++)
            {
                float t_d = Vector2.Distance(new Vector2(t_x + 0.5f, t_y + 0.5f), new Vector2(t_cx, t_cy));
                // 경계 1.5px에 걸쳐 부드럽게 — 딱 자르면 곡선에 계단이 보인다.
                float t_a = Mathf.Clamp01((t_d - (c_size - 1.5f)) / 1.5f);
                t_px[t_y * c_size + t_x] = new Color(1f, 1f, 1f, t_a);
            }

        t_tex.SetPixels(t_px);
        t_tex.Apply();
        s_cornerSprites[_index] = Sprite.Create(t_tex, new Rect(0f, 0f, c_size, c_size), new Vector2(0.5f, 0.5f));
        return s_cornerSprites[_index];
    }

    // 구멍(_screenRect)을 뺀 나머지를 4패널로 덮는다. 캔버스가 화면 전체 stretch라
    // **정규화 앵커 = 화면 비율**이 그대로 성립한다 → 해상도·CanvasScaler와 무관하게 맞는다.
    void LayoutFocusPanels(Rect _screenRect)
    {
        float t_x0 = Mathf.Clamp01(_screenRect.xMin / Screen.width);
        float t_x1 = Mathf.Clamp01(_screenRect.xMax / Screen.width);
        float t_y0 = Mathf.Clamp01(_screenRect.yMin / Screen.height);
        float t_y1 = Mathf.Clamp01(_screenRect.yMax / Screen.height);

        SetPanel(0, new Vector2(0f,   t_y1), new Vector2(1f,   1f));    // Top
        SetPanel(1, new Vector2(0f,   0f),   new Vector2(1f,   t_y0));  // Bottom
        SetPanel(2, new Vector2(0f,   t_y0), new Vector2(t_x0, t_y1));  // Left
        SetPanel(3, new Vector2(t_x1, t_y0), new Vector2(1f,   t_y1));  // Right

        LayoutFocusCorners(t_x0, t_x1, t_y0, t_y1);
    }

    // 모서리 조각을 구멍 **안쪽**에 얹는다. 피벗을 그 모서리에 두면 회전 없이 안쪽으로만 뻗는다.
    // 반지름은 구멍보다 커질 수 없다(작은 구멍에서 네 조각이 겹쳐 구멍이 막히는 것 방지).
    void LayoutFocusCorners(float _x0, float _x1, float _y0, float _y1)
    {
        var t_canvasRect = (RectTransform)this.focusPanels[0].parent.parent;
        Vector2 t_canvas = t_canvasRect.rect.size;

        float t_r = Mathf.Min(this.focusCornerRadius,
                              (_x1 - _x0) * t_canvas.x * 0.5f,
                              (_y1 - _y0) * t_canvas.y * 0.5f);
        t_r = Mathf.Max(0f, t_r);

        SetCorner(0, new Vector2(_x0, _y0), new Vector2(0f, 0f), t_r);   // BottomLeft
        SetCorner(1, new Vector2(_x1, _y0), new Vector2(1f, 0f), t_r);   // BottomRight
        SetCorner(2, new Vector2(_x1, _y1), new Vector2(1f, 1f), t_r);   // TopRight
        SetCorner(3, new Vector2(_x0, _y1), new Vector2(0f, 1f), t_r);   // TopLeft
    }

    void SetCorner(int _i, Vector2 _anchor, Vector2 _pivot, float _radius)
    {
        RectTransform t_r = this.focusCorners[_i];
        t_r.gameObject.SetActive(_radius > 0.5f);
        t_r.anchorMin = t_r.anchorMax = _anchor;
        t_r.pivot     = _pivot;
        t_r.anchoredPosition = Vector2.zero;
        t_r.sizeDelta        = new Vector2(_radius, _radius);
    }

    void SetPanel(int _i, Vector2 _min, Vector2 _max)
    {
        RectTransform t_r = this.focusPanels[_i];
        t_r.anchorMin = _min;
        t_r.anchorMax = _max;
        t_r.offsetMin = t_r.offsetMax = Vector2.zero;
    }

    void HideBanner()
    {
        if (this.tapHint != null) this.tapHint.SetActive(false);
        if (this.bannerGroup == null) return;
        this.bannerGroup.DOKill();
        this.bannerGroup.DOFade(0f, 0.15f).SetLink(this.bannerGroup.gameObject);
    }

    // 마스크 활성: 탭 감지(raycast on) + darken이면 어둡게, 아니면 투명(그래도 탭 받음).
    void ActivateMask(bool _darken)
    {
        if (this.dimMask == null) return;
        this.dimMask.raycastTarget   = true;
        this.dimGroup.blocksRaycasts = true;
        this.dimGroup.DOKill();
        this.dimGroup.DOFade(_darken ? DimStrength : 0f, 0.15f).SetLink(this.dimGroup.gameObject);
    }

    void DeactivateMask()
    {
        if (this.dimMask == null) return;
        this.dimMask.raycastTarget   = false;
        this.dimGroup.blocksRaycasts = false;
        this.dimGroup.DOKill();
        this.dimGroup.DOFade(0f, 0.15f).SetLink(this.dimGroup.gameObject);
    }

    // pointer down: 대기 중이면 "이번 구간에서 시작된 press"로 표시.
    void OnMaskDown()
    {
        if (this.tapArmed) this.pressActive = true;
    }

    // pointer up: 이번 구간에서 시작된 press의 release만 진행으로 인정.
    void OnMaskUp()
    {
        if (this.tapArmed && this.pressActive) this.tapped = true;
        this.pressActive = false;
    }

    void ClearHighlight()
    {
        this.highlightedAttacker?.SetHighlight(false);
        this.highlightedTarget?.SetHighlight(false);
        this.highlightedAttacker = null;
        this.highlightedTarget   = null;
    }

    void ClearPointer()
    {
        this.pointerCard?.SetTutorialPointer(false);
        this.pointerCard = null;
    }

    // ── 코드 빌드 캔버스 ──────────────────────────────────────────────────────
    void BuildUI()
    {
        var t_canvasGo = new GameObject("TutorialOverlayCanvas");
        t_canvasGo.transform.SetParent(transform, false);
        var t_canvas = t_canvasGo.AddComponent<Canvas>();
        t_canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        t_canvas.sortingOrder = 200;   // 전투 UI 위
        var t_scaler = t_canvasGo.AddComponent<CanvasScaler>();
        t_scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        t_scaler.referenceResolution = new Vector2(1080f, 1920f);
        t_canvasGo.AddComponent<GraphicRaycaster>();

        // dim 마스크(풀스크린, 탭 감지 버튼) — 기본 비활성(raycast off).
        var t_dimGo = new GameObject("DimMask");
        t_dimGo.transform.SetParent(t_canvasGo.transform, false);
        this.dimGroup = t_dimGo.AddComponent<CanvasGroup>();
        this.dimGroup.alpha = 0f;
        this.dimGroup.blocksRaycasts = false;
        this.dimMask = t_dimGo.AddComponent<Image>();
        this.dimMask.color = new Color(0f, 0f, 0f, 1f);   // 어둡기는 CanvasGroup alpha로 제어(불투명 검정)
        this.dimMask.raycastTarget = false;
        var t_dimRect = this.dimMask.GetComponent<RectTransform>();
        t_dimRect.anchorMin = Vector2.zero;
        t_dimRect.anchorMax = Vector2.one;
        t_dimRect.offsetMin = t_dimRect.offsetMax = Vector2.zero;
        this.tapCatcher = t_dimGo.AddComponent<TutorialTapCatcher>();   // 콜백은 WireTapCatcher에서 배선

        // 배너(상단). dim보다 위에 그려지도록 나중에 생성.
        var t_bgGo = new GameObject("Banner");
        t_bgGo.transform.SetParent(t_canvasGo.transform, false);
        this.bannerGroup = t_bgGo.AddComponent<CanvasGroup>();
        this.bannerGroup.blocksRaycasts = false;   // 배너는 입력 무흡수(탭은 dim이 받음)
        this.bannerGroup.interactable   = false;
        var t_bg = t_bgGo.AddComponent<Image>();
        t_bg.color = new Color(0f, 0f, 0f, 0.72f);
        t_bg.raycastTarget = false;
        var t_bgRect = t_bg.GetComponent<RectTransform>();
        t_bgRect.anchorMin = new Vector2(0.5f, 1f);
        t_bgRect.anchorMax = new Vector2(0.5f, 1f);
        t_bgRect.pivot     = new Vector2(0.5f, 1f);
        t_bgRect.anchoredPosition = new Vector2(0f, -160f);
        t_bgRect.sizeDelta        = new Vector2(960f, 220f);

        var t_txtGo = new GameObject("Text");
        t_txtGo.transform.SetParent(t_bgGo.transform, false);
        this.banner = t_txtGo.AddComponent<TextMeshProUGUI>();
        TutorialUIStyle.ApplyFont(this.banner);
        this.banner.fontSize  = 46f;
        this.banner.color     = Color.white;
        this.banner.alignment = TextAlignmentOptions.Center;
        this.banner.enableWordWrapping = true;
        this.banner.raycastTarget = false;
        var t_txtRect = this.banner.GetComponent<RectTransform>();
        t_txtRect.anchorMin = Vector2.zero;
        t_txtRect.anchorMax = Vector2.one;
        t_txtRect.offsetMin = new Vector2(32f, 20f);
        t_txtRect.offsetMax = new Vector2(-32f, -20f);

        // "탭하여 계속" 힌트(배너 하단). 배너와 함께 표시/숨김되도록 필드로 보관.
        var t_hintGo = new GameObject("TapHint");
        this.tapHint = t_hintGo;
        t_hintGo.transform.SetParent(t_bgGo.transform, false);
        var t_hint = t_hintGo.AddComponent<TextMeshProUGUI>();
        TutorialUIStyle.ApplyFont(t_hint);
        t_hint.text      = "<size=70%>화면을 탭하여 계속</size>";
        t_hint.color     = new Color(1f, 1f, 1f, 0.65f);
        t_hint.alignment = TextAlignmentOptions.Center;
        t_hint.raycastTarget = false;
        var t_hintRect = t_hint.GetComponent<RectTransform>();
        t_hintRect.anchorMin = new Vector2(0f, 0f);
        t_hintRect.anchorMax = new Vector2(1f, 0f);
        t_hintRect.pivot     = new Vector2(0.5f, 1f);
        t_hintRect.anchoredPosition = new Vector2(0f, -6f);
        t_hintRect.sizeDelta        = new Vector2(0f, 44f);

        this.bannerGroup.alpha = 0f;
    }

    static void EnsureEventSystem()
    {
        if (Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() != null) return;
        var t_es = new GameObject("EventSystem");
        t_es.AddComponent<UnityEngine.EventSystems.EventSystem>();
        t_es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
    }

#if UNITY_EDITOR
    // 코드 빌드(BuildUI)와 동일한 레이아웃을 프리팹으로 저장. 디자이너가 프리팹에서 dim/배너/문구를
    // 직접 수정한 뒤 GameInitializer의 tutorialOverlayPrefab [SerializeField]에 물려주면 런타임이 사용한다.
    [UnityEditor.MenuItem("Tools/Tutorial/Rebuild Overlay Prefab")]
    static void RebuildOverlayPrefab()
    {
        const string t_dir  = "Assets/Prefabs/Tutorial";
        const string t_path = t_dir + "/TutorialOverlay.prefab";
        if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/Prefabs"))
            UnityEditor.AssetDatabase.CreateFolder("Assets", "Prefabs");
        if (!UnityEditor.AssetDatabase.IsValidFolder(t_dir))
            UnityEditor.AssetDatabase.CreateFolder("Assets/Prefabs", "Tutorial");

        var t_go = new GameObject("TutorialOverlay");
        try
        {
            t_go.AddComponent<TutorialOverlayUI>().BuildUI();   // 참조 배선된 채로 저장(탭 콜백만 런타임)
            UnityEditor.PrefabUtility.SaveAsPrefabAsset(t_go, t_path);
            Debug.Log($"[Tutorial] Overlay prefab saved: {t_path}");
        }
        finally { DestroyImmediate(t_go); }
        UnityEditor.AssetDatabase.Refresh();
    }
#endif
}

using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;
using TMPro;

[RequireComponent(typeof(CardAnimator))]
public class CardView : MonoBehaviour
{
    #region Static / Events
    // 탭 공격 무장 상태의 진실원은 BattleSelection. 아래 셰임은 기존 구독처를 위한 전달일 뿐이다.
    // TODO: 호출부 이관 후 삭제
    public static event System.Action<CardView, CardView> OnAttack
    {
        add    => BattleSelection.OnAttack += value;
        remove => BattleSelection.OnAttack -= value;
    }

    /// <summary>탭 무장 상태가 **실제로 바뀌었을 때만** 통지(무장=그 카드, 해제=null).
    /// 튜토리얼 가이드가 "아군 골랐다 → 이제 적 고를 차례"로 넘어가는 유일한 신호다.
    /// 다른 카드로 갈아타는 경우엔 null을 거치지 않는다 — 안내 배너가 한 프레임 깜빡이지 않게.</summary>
    // TODO: 호출부 이관 후 삭제
    public static event System.Action<CardView> OnAttackerArmed
    {
        add    => BattleSelection.OnAttackerArmed += value;
        remove => BattleSelection.OnAttackerArmed -= value;
    }

    /// <summary>지금 탭으로 무장된 공격자(없으면 null). 튜토리얼이 "이미 무장돼 있는가"를 먼저 확인해
    /// <see cref="OnAttackerArmed"/> 구독만 걸고 영영 기다리는 상황을 피한다.</summary>
    // TODO: 호출부 이관 후 삭제
    public static CardView SelectedAttacker => BattleSelection.SelectedAttacker;

    const float  REJECT_SHAKE_DUR      = 0.22f;
    const float  REJECT_SHAKE_STRENGTH = 0.12f;
    #endregion

    #region Fields
    [Header("Core")]
    [SerializeField] CardAnimator cardAnim;

    [Header("UI")]
    [SerializeField] TMP_Text hpText;
    [SerializeField] TMP_Text bonusHpText;
    // 체력 변동 연출에서 커졌다 작아지는 아이콘. 미배선이면 HP 숫자 자체를 대신 부풀린다
    // (연출이 아예 없는 것보단 숫자라도 들썩이는 편이 변동을 읽게 해준다).
    [SerializeField] Transform hpIcon;
    [SerializeField] float hpIconPopScale = 1.4f;   // 변동 중 배율(원래 스케일 대비 — 아이콘 기본이 1이 아니다)
    // HP 숫자도 같이 부푼다. 아이콘보다 작게 두는 게 기본 — 숫자가 아이콘만큼 커지면 옆 칸(이름·보너스HP)을 침범한다.
    [SerializeField] float hpTextPopScale = 1.25f;
    // 공격 선택 시 이 카드가 받을 예상 데미지("-N"). HP 라벨을 덮어써 "맞은 뒤 남을 체력"을 보여주면
    // 현재 체력과 헷갈리므로 수치는 별도 라벨에 띄운다. 미배선이면 HP 라벨 폴백(아래 ShowAttackPreview).
    [SerializeField] TMP_Text damagePreviewText;
    [SerializeField] TMP_Text nameText;
    [SerializeField] SpriteRenderer illustration;
    // 뒷면일 때 일러스트 자리에 대신 깔리는 그림(덱 뒷면). 미배선이면 앞면 그림이 그대로 남는다.
    [SerializeField] Sprite cardBackSprite;
    // 뒷면일 때 통째로 숨기는 것들: 카드 테두리(키워드 프레임 포함)와 이름·체력 표시.
    // 뒷면은 "덱에 꽂힌 카드 한 장"이라 앞면 장식이 하나라도 남으면 반쯤 뒤집힌 것처럼 보인다.
    [SerializeField] GameObject frameRoot;
    [SerializeField] GameObject infoRoot;
    [SerializeField] GameObject emptyOverlay;

    [Header("Highlight / Glow")]
    [SerializeField] SpriteRenderer selectedHighlight;
    [SerializeField] ParticleSystem passiveGlowSystem;
    [SerializeField] GameObject keywordGlowPrefab;
    [SerializeField] float targetFocusScale = 1.15f;   // 드래그 조준 시 타겟 적 카드 확대 배율.
    [SerializeField] float targetFocusDur   = 0.15f;

    [Header("Weapon")]
    [SerializeField] Transform weaponAnchor;

    [Header("Armed VFX")]
    // 무장 이펙트를 카드 아트 위로 올릴 정렬 order(레이어는 카드와 동일하게 맞춘다).
    [SerializeField] int armedVfxSortingOrder = 20;

    [Header("Keywords")]
    [SerializeField] Transform keywordIconRoot;
    [SerializeField] GameObject keywordIconPrefab;
    [SerializeField] KeywordIconConfig keywordIconConfig;
    [SerializeField] float iconSpacing = 0.3f;
    // true면 키워드 아이콘이 시너지 배지 자리를 차지하고, 시너지 배지는 표시하지 않는다.
    // 한 자리에 둘 다 그리면 겹치므로 "그 자리의 주인"은 이 스위치 하나가 정한다(양쪽 분기의 단일 진실원).
    // false로 되돌리면 종전대로 키워드=우하단 가로줄 + 시너지 배지 복귀.
    [SerializeField] bool keywordIconsUseSynergySlot = true;

    // 위 스위치가 true일 때 아이콘이 놓이는 자리. 이름 왼쪽(카드 하단 줄)에서 오른쪽으로 나열한다 —
    // 예전엔 시너지 배지와 같은 세로열 값을 그대로 썼는데, 그러면 배지 좌표를 손댈 때마다 아이콘이 따라 움직였다.
    [SerializeField] Vector2 keywordIconStart = new Vector2(-0.54f, -1.14f);
    [SerializeField] Vector2 keywordIconStep  = new Vector2(0.42f, 0f);

    // 프레임에 얹는 키워드별 장식 이미지(아이콘 줄과 별개, 가시성 보강용). 아직 이미지가 없는 키워드는
    // 배열에서 빼두면 된다 — 없는 항목은 그냥 안 켜진다.
    // 이름 매칭이 아니라 참조 배선인 이유: 오브젝트 이름을 바꿔도 조용히 꺼지지 않게.
    [System.Serializable]
    public struct KeywordFrame
    {
        public CardKeyword keyword;
        public GameObject  overlay;
    }
    [SerializeField] KeywordFrame[] keywordFrames;

    [Header("Synergy")]
    [SerializeField] Transform synergyBadgeRoot;         // 배지들을 붙일 앵커(자식 루트). keywordIconRoot와 동일 패턴.
    [SerializeField] SynergyBadgeView synergyBadgePrefab; // 색+텍스트 배지 프리팹.
    [SerializeField] float synergyBadgeXPos   = 0.55f;  // 배지 세로열 X(synergyBadgeRoot 기준).
    [SerializeField] float synergyBadgeYStart = 0.95f;  // 첫 배지(i=0) Y.
    [SerializeField] float synergyBadgeYStep  = -0.5f;  // 배지 간 Y 간격(아래로 쌓기).
    // 표시 최대 배지 수(초과분 드롭). 기본값은 CardVisualRules 상수 하나에서 — 프리팹 오버라이드는 남지만
    // 아웃게임 타일과 기본값이 따로 놀지 않게 코드 소스를 통일한다.
    [SerializeField] int   synergyMaxBadges   = CardVisualRules.MaxSynergyBadges;

    [Header("Aim Tilt")]
    // 조준 중 카드가 조준 방향으로 기우는 최대 각(도). 공격 돌진의 lean(maxLean)보다 작게 둬야
    // "조준=살짝, 타격=확 꺾임"으로 읽힌다.
    [SerializeField] float aimTiltMaxAngle = 10f;
    // 목표 각도로 수렴하는 속도. 값이 클수록 즉각적. 프레임레이트 무관 보간에 쓴다.
    [SerializeField] float aimTiltSpeed    = 12f;

    [Header("Input")]
    [SerializeField] float dragThreshold = 30f;
    [SerializeField] float deadZoneRadius = 80f;

    // 카드 실물 범위. BottomCenter(연출 발사점)와 입력 컨트롤러의 "손가락이 카드 밖으로 나갔나" 판정이 공유한다.
    Collider2D selfCollider;
    [SerializeField] float dirThreshold = 0.35f;
    [SerializeField] HintArrow hintArrow;
    [SerializeField] SwipeGuide swipeGuide;
    [SerializeField] LineRenderer dragLine;

    CardInstance boundCard;

    // 무기 수명(Instantiate/Destroy)과 무장 이펙트 풀 대여/반납은 각각 아래 두 객체가 소유한다.
    // MonoBehaviour가 아니라 순수 C# 객체 — 인스펙터 배선은 위 SerializeField에 그대로 남고 값만 주입한다.
    // 지연 생성인 이유: 비활성 상태로 Instantiate된 뷰는 Awake 전에 Render가 올 수 있다(주입값은 역직렬화 후에야 유효).
    CardWeaponView weaponView;
    CardArmedVfx   armedVfxView;

    CardWeaponView WeaponView
    {
        get
        {
            if (this.weaponView == null) this.weaponView = new CardWeaponView(transform, this.weaponAnchor);
            return this.weaponView;
        }
    }

    CardArmedVfx ArmedVfxView
    {
        get
        {
            if (this.armedVfxView == null) this.armedVfxView = new CardArmedVfx(transform, this.armedVfxSortingOrder);
            return this.armedVfxView;
        }
    }

    // 입력 제스처 상태머신(탭/드래그/롱프레스/조준/타깃 추적)은 CardInputController가 통째로 소유한다.
    // 여기 남는 건 Unity 메시지(OnMouse*/Update/OnDrawGizmos)를 그대로 넘기는 전달 스텁뿐이다 —
    // OnMouse*는 콜라이더가 달린 GameObject의 컴포넌트에만 오고, 별도 MonoBehaviour로 빼면
    // 프리팹/씬 YAML을 재직렬화해야 한다. 인스펙터 배선/튜닝값은 위 SerializeField에 그대로 남고 값만 주입한다.
    // 지연 생성 폴백은 WeaponView/ArmedVfxView와 같은 규약(Awake 이전 경로가 생겨도 NRE로 무너지지 않게).
    CardInputController inputCtrl;

    CardInputController InputCtrl
    {
        get
        {
            if (this.inputCtrl == null) this.inputCtrl = CreateInputController();
            return this.inputCtrl;
        }
    }

    CardInputController CreateInputController() => new CardInputController(
        this,
        this.selfCollider,
        this.hintArrow,
        this.swipeGuide,
        this.dragLine,
        this.dragThreshold,
        this.deadZoneRadius,
        this.dirThreshold,
        this.aimTiltMaxAngle,
        this.aimTiltSpeed);

    // 카드 위에 얹히는 장식 계층(키워드 아이콘·프레임 장식·시너지 배지·글로우)은 CardDecorView가 통째로 소유한다.
    // 여기 남는 건 Render의 한 줄 갱신 호출과, 기존 호출부를 위한 전달 셰임뿐이다.
    // WeaponView/ArmedVfxView와 같은 규약: 순수 C# 객체 + 인스펙터 배선은 위 SerializeField에 잔류(값만 주입) + 지연 생성.
    CardDecorView decorView;

    CardDecorView Decor
    {
        get
        {
            if (this.decorView == null) this.decorView = CreateDecorView();
            return this.decorView;
        }
    }

    CardDecorView CreateDecorView() => new CardDecorView(
        this,
        this.cardAnim,
        this.keywordIconRoot,
        this.keywordIconPrefab,
        this.keywordIconConfig,
        this.iconSpacing,
        this.keywordIconsUseSynergySlot,
        this.keywordIconStart,
        this.keywordIconStep,
        this.keywordFrames,
        this.synergyBadgeRoot,
        this.synergyBadgePrefab,
        this.synergyBadgeXPos,
        this.synergyBadgeYStart,
        this.synergyBadgeYStep,
        this.synergyMaxBadges,
        this.keywordGlowPrefab,
        this.passiveGlowSystem);

    Color hpTextOriginalColor;

    // ── HP 표기 상태 ─────────────────────────────────────────────────────
    // 지금 화면에 찍혀 있는 값. 규칙상 hp는 이미 확정돼 있어도(결정론: 상태변이 선행) 표기는 연출을 따라
    // 굴러가므로, 굴림의 **시작점은 모델이 아니라 이 값**이다. 모델을 시작점으로 쓰면 굴릴 것이 남지 않는다.
    int shownHp;
    int shownBonusHp;
    // 진행 중 굴림까지 모두 끝났을 때 도달할 논리 목표값. shownHp는 현재 프레임 값이라 연속 회복이
    // 첫 굴림을 끊으면 일부 회복량을 잃을 수 있다 — 다음 목표는 이 값에 누적한다.
    int hpDisplayTarget;
    // 아직 화면에 안 올린 회복량. 0보다 크면 "표기 유예 중" — 이 사이 Render가 최신 hp로 덮으면
    // 숫자만 먼저 올라간다. 힐러가 둘이면 몫이 둘 쌓이고, 투사체가 도착할 때마다 자기 몫씩 빠진다.
    int hpPendingHeal;
    Sequence hpRollSeq;

    public CardInstance BoundCard => this.boundCard;
    #endregion

    #region Unity Lifecycle
    void Awake()
    {
        BattleBoardView.Register(this);
        if (this.cardAnim == null) this.cardAnim = GetComponent<CardAnimator>();
        this.cardAnim.ExcludeFromFade(this.selectedHighlight);
        // SwipeGuide 화살표는 카드 자식이라 카드 fade(ApplyDragTargetFade의 FadeCards)에 휩쓸려
        // dim/highlight 포커스가 alpha 1로 덮인다 → fade 대상에서 제외해 SwipeGuide가 알파를 단독 제어.
        if (this.swipeGuide != null)
            foreach (SpriteRenderer t_sr in this.swipeGuide.GetComponentsInChildren<SpriteRenderer>(true))
                this.cardAnim.ExcludeFromFade(t_sr);
        this.selfCollider = GetComponentInChildren<Collider2D>();   // OnMouse* 를 받는 콜라이더

        if (this.dragLine == null)
        {
            this.dragLine = gameObject.AddComponent<LineRenderer>();
            this.dragLine.positionCount = 2;
            this.dragLine.startWidth    = 0.05f;
            this.dragLine.endWidth      = 0.02f;
            this.dragLine.material      = new Material(Shader.Find("Sprites/Default"));
            this.dragLine.startColor    = Color.white;
            this.dragLine.endColor      = new Color(1f, 1f, 1f, 0.4f);
            this.dragLine.useWorldSpace = true;
            this.dragLine.enabled       = false;
        }

        // 컨트롤러는 배선값을 **값으로** 받는다 → selfCollider/dragLine이 확정된 뒤에 만드는 게 계약이다.
        this.inputCtrl = CreateInputController();
    }
    void OnDestroy()
    {
        BattleBoardView.Unregister(this);
        this.armedVfxView?.Hide();   // 풀 대여분을 물고 죽으면 풀이 파괴된 오브젝트를 들고 있게 된다
        this.weaponView?.Cleanup();  // 무기 인스턴스는 자식이라 Unity가 함께 파괴 — 참조만 끊는다
        this.decorView?.Cleanup();   // 아이콘/배지 트윈 끊기(파괴 전 DOKill 규약) + 스냅샷 참조 해제
        KillHpRoll();
        if (this.hpText != null) this.hpText.DOKill();
    }

    // 튜토리얼: hintArrow를 강제 표시(정상 Update 로직 무시). 안내 중 "여기서 드래그" 포인터.
    bool tutorialPointer;
    public void SetTutorialPointer(bool _on)
    {
        this.tutorialPointer = _on;
        if (this.hintArrow != null) this.hintArrow.SetVisible(false);   // 화살표 완전 비표시
    }

    void Update() => InputCtrl.Tick();
    #endregion

    #region Input
    // 제스처 상태머신 본체는 CardInputController(순수 C# 객체)가 소유한다.
    // 여기 남는 건 Unity가 콜라이더 GameObject의 컴포넌트에만 보내는 OnMouse* 메시지 전달 스텁뿐이다 —
    // 별도 MonoBehaviour로 빼면 프리팹/씬 YAML을 재직렬화해야 한다.
    void OnMouseDown() => InputCtrl.OnMouseDown();
    void OnMouseDrag() => InputCtrl.OnMouseDrag();
    void OnMouseUp()   => InputCtrl.OnMouseUp();

    /// <summary>조준 기울기 원복 스텁. 탭 공격(HandleEnemyTap)은 **적 카드의** 컨트롤러에서 도는데
    /// 되돌려야 할 기울기는 공격자 카드의 것이라, 컨트롤러끼리 서로를 알지 않도록 이 스텁을 거친다.</summary>
    public void ResetAimTilt() => InputCtrl.ResetAimTilt();

    // ── 무효 타깃 거절 피드백(카드 한 장의 연출) ──────────────────────────
    // "언제 거절인가"는 CardInputController가 정한다. 여기엔 그 카드가 어떻게 반응하는지만 남는다.

    /// <summary>못 치는 대상임을 알리는 짧은 흔들기. idle 슬롯 카드 전용(공격/이동 연출 중 호출 금지).
    /// _focus면 흔들면서 확대도 유지 — "조준은 됐다(포커스), 다만 못 친다(흔들림)"를 동시에 말한다.
    /// 확대 해제는 조준이 벗어날 때 호출부(ClearRejectFocus)가 담당.</summary>
    public void PlayRejectShake(bool _focus = false)
    {
        Vector3 t_home = transform.position;
        transform.DOKill();
        transform.position = t_home;
        // 위치(흔들림)와 스케일(포커스)은 서로 다른 트윈 → DOKill 이후 동시에 걸어야 둘 다 산다.
        transform.DOShakePosition(REJECT_SHAKE_DUR, REJECT_SHAKE_STRENGTH, vibrato: 14, randomness: 0f)
                 .SetLink(gameObject)
                 .OnComplete(() => transform.position = t_home);
        if (_focus)
            transform.DOScale(this.targetFocusScale, this.targetFocusDur).SetEase(Ease.OutBack).SetLink(gameObject);
    }

    /// <summary>"이쪽을 쳐라" 주목 펄스. 유효 타깃(도발 등)에 사용.</summary>
    public void PlayAttentionPulse()
    {
        transform.DOKill();
        transform.localScale = Vector3.one;
        transform.DOPunchScale(Vector3.one * (this.targetFocusScale - 1f), this.targetFocusDur * 2f, vibrato: 4, elasticity: 0.4f)
                 .SetLink(gameObject)
                 .OnComplete(() => transform.localScale = Vector3.one);
        if (BattleRules.IsTaunt(this.boundCard))
            PlayKeywordGlow(CardKeyword.Taunt).Forget();
    }

    #endregion

    #region Visual State
    public void Render(CardInstance _card, SynergyState _synergy = null)
    {
        // 슬롯 점유 카드가 바뀌면(사망→새 카드 스폰 등) 이전 피격 연출 잔여 제거 → 새 카드에 이월 방지.
        if (this.boundCard != _card)
        {
            this.cardAnim.ResetHitEffect();
            this.armedVfxView?.Hide();   // 이전 카드의 무장 이펙트가 새 카드에 남지 않게
            // 표기 굴림/유예도 카드에 속한 상태다 — 이월되면 새 카드가 남의 체력에서 굴러 내려온다.
            KillHpRoll();
            this.hpPendingHeal = 0;
        }

        this.boundCard = _card;
        this.cardAnim.SetBoundCard(_card);
        bool t_isEmpty = _card == null;

        this.emptyOverlay.SetActive(t_isEmpty);
        SetHighlight(false);

        if (t_isEmpty)
        {
            SetFaceDownLook(false);
            SetupWeapon(null);
            Decor.Refresh(null, null);   // 빈 슬롯: 아이콘·프레임 장식·배지 전부 없음.
            return;
        }

        bool t_isFaceDown = !_card.isRevealed;

        // 뒷면은 수치 자체가 비밀, 유예 중이면 **일부러 옛 값**(연출이 아직 안 왔다), 그 외엔 최신값 스냅.
        if (t_isFaceDown)
        {
            KillHpRoll();
            this.hpPendingHeal = 0;
            SetHpDisplay("?", "");
        }
        else if (this.hpPendingHeal > 0 || (this.hpRollSeq != null && this.hpRollSeq.IsActive()))
            WriteHpDisplay(this.shownHp, this.shownBonusHp);
        else SnapHpDisplay(_card);
        this.nameText.text = t_isFaceDown ? "???" : _card.data.displayName;

        // 뒷면이면 덱 뒷면 그림으로 갈아 끼운다 — 앞면 일러스트가 남아 있으면 뒷면 그림 밖으로 비친다.
        if (this.illustration != null && !t_isFaceDown && _card.data.battleImage != null)
            this.illustration.sprite = _card.data.battleImage;

        SetFaceDownLook(t_isFaceDown);

        // 배치 엠블럼이 볼 스냅샷. CardDecorView는 배지 슬롯을 키워드가 쓰면 즉시 return 해서
        // LastBadgeState를 못 채운다 — 그 경로에서도 엠블럼은 떠야 하므로 별도 필드에 따로 잡는다.
        this.activeSynergyState = _synergy;

        SetupWeapon(_card.data);
        Decor.Refresh(_card, _synergy);   // 뒷면 은닉·표시 대상 판정은 CardDecorView 안에서.
    }

    // 이 카드가 속한 필드의 확정 시너지 스냅샷(BattleFieldView가 Render로 주입). 배치 엠블럼 판정용.
    SynergyState activeSynergyState;

    Vector3 illustrationBaseScale = Vector3.one;   // 앞면 복귀용. Awake에서 1회 캡처.
    bool    illustrationScaleCached;

    /// <summary>뒷면/앞면 겉모습 전환. 뒷면이면 테두리·정보를 숨기고 일러스트를 덱 뒷면 그림으로 바꾼다.
    ///
    /// 뒷면 그림은 카드 아트와 원본 크기가 달라(덱 더미용 이미지) 그대로 넣으면 카드 밖으로 삐져나온다.
    /// 그래서 **테두리 높이에 맞춰 스케일을 계산**한다 — 매직넘버를 두면 뒷면 이미지를 교체할 때마다 어긋난다.</summary>
    void SetFaceDownLook(bool _faceDown)
    {
        if (!this.illustrationScaleCached && this.illustration != null)
        {
            this.illustrationBaseScale  = this.illustration.transform.localScale;
            this.illustrationScaleCached = true;
        }

        if (this.frameRoot != null) this.frameRoot.SetActive(!_faceDown);
        if (this.infoRoot  != null) this.infoRoot.SetActive(!_faceDown);

        if (this.illustration == null) return;

        if (!_faceDown || this.cardBackSprite == null)
        {
            this.illustration.transform.localScale = this.illustrationBaseScale;
            return;
        }

        this.illustration.sprite = this.cardBackSprite;
        this.illustration.transform.localScale = FitBackScale();
    }

    /// <summary>뒷면 그림을 카드 테두리 높이에 맞추는 로컬 스케일. 테두리가 없으면 원래 스케일 유지.</summary>
    Vector3 FitBackScale()
    {
        SpriteRenderer t_frame = this.frameRoot != null ? this.frameRoot.GetComponent<SpriteRenderer>() : null;
        if (t_frame == null || t_frame.sprite == null) return this.illustrationBaseScale;

        float t_target  = t_frame.sprite.bounds.size.y * t_frame.transform.lossyScale.y;
        Transform t_parent = this.illustration.transform.parent;
        float t_parentY = t_parent != null ? t_parent.lossyScale.y : 1f;
        float t_natural = this.cardBackSprite.bounds.size.y * t_parentY;
        if (t_natural <= 0.0001f) return this.illustrationBaseScale;

        float t_k = t_target / t_natural;
        return new Vector3(t_k, t_k, this.illustrationBaseScale.z);
    }

    // ── 무기 / 무장 이펙트 위임 셰임 ──────────────────────────────────────
    // 실제 구현은 CardWeaponView(무기 수명)와 CardArmedVfx(풀 대여/반납)가 소유한다.
    // 아래는 기존 호출부(AttackSequence / AttackAnimTester / BattleSelection)를 위한 전달일 뿐이다.

    // TODO: 호출부 이관 후 삭제
    void SetupWeapon(CardData _data) => WeaponView.Setup(_data, this.boundCard);

    // 무장 이펙트는 여기 얹지 않는다 — ResolveHits가 접촉 직후 FocusWeapon(false)를 부르기 때문에
    // 같이 묶으면 반동이 끝나기도 전에 이펙트가 꺼진다. 무장/해제 시점에서 SetArmedVfx를 직접 부른다.
    // TODO: 호출부 이관 후 삭제
    public void FocusWeapon(bool _active) => WeaponView.Focus(_active);

    /// <summary>무장(포커스) 이펙트 토글. 카드 자식으로 붙어 공격 이동/기울기를 그대로 따라간다.
    /// 켜지는 시점 = 무장(FocusWeapon(true)), 꺼지는 시점 = 적에 닿는 순간(AttackSequence가 false로 호출).
    /// 중복 호출은 무시한다 — 드래그 중 여러 경로에서 불린다.</summary>
    // TODO: 호출부 이관 후 삭제
    public void SetArmedVfx(bool _active)
        => ArmedVfxView.SetActive(_active, this.boundCard, IsEnemySide, VfxSortingLayerId);

    /// <summary>무장 이펙트 프리팹을 갈아끼운다(null이면 카드의 AttackEffect가 정의한 Armed 항목 사용).
    /// AttackAnimTester가 후보를 넘겨보며 고를 때 쓴다 — 카드 에셋을 건드리지 않는 런타임 오버라이드.
    /// 켜져 있는 상태에서 바꾸면 즉시 교체된다.</summary>
    // TODO: 호출부 이관 후 삭제
    public void SetArmedVfxPrefab(GameObject _prefab)
        => ArmedVfxView.SetPrefabOverride(_prefab, this.boundCard, IsEnemySide, VfxSortingLayerId);

    public void SetHighlight(bool _active)
    {
        if (this.selectedHighlight != null)
            this.selectedHighlight.enabled = _active;
    }

    // 조준 포커스: 이 카드를 확대(_on)/원복. 드래그 타겟 전환·탭 무장/해제 시 호출.
    // **transform.DOKill 금지** — 해제 호출이 공격 발동 직후에 오는 경로가 있어(OnMouseUp: OnAttack → ClearTargetPreview)
    // 전체 DOKill을 하면 막 시작한 시네마 이동(DOMove)까지 같이 죽는다. 실제로 피격자만 제자리에 남는 버그가 그것.
    // 그래서 이 확대 트윈만 따로 들고 있다가 그것만 끈다.
    // _instant: 공격 발동 직전 원복처럼 뒤이어 AttackSequence의 DOKill이 들어오는 경로 — 트윈이 중간에 죽어
    // 확대된 채 고착되지 않도록 스케일을 즉시 되돌린다.
    Tween focusTween;

    public void SetTargetFocus(bool _on, bool _instant = false)
    {
        this.focusTween?.Kill();
        this.focusTween = null;

        float t_scale = _on ? this.targetFocusScale : 1f;
        if (_instant) { transform.localScale = Vector3.one * t_scale; return; }
        this.focusTween = transform.DOScale(t_scale, this.targetFocusDur)
                                   .SetEase(Ease.OutBack).SetLink(gameObject);
    }

    // HP 라벨 폴백 프리뷰가 켜져 있는지. damagePreviewText가 배선돼 있으면 HP 라벨은 건드리지 않으므로
    // 해제 때 색·텍스트를 되돌리면 안 된다(원본 색을 캡처한 적이 없어 검정으로 굳는다).
    bool hpFallbackPreview;

    /// <summary>공격 선택 시 이 카드가 받을 **예상 데미지**를 표시한다. HP 라벨은 현재 체력을 그대로 유지 —
    /// "맞은 뒤 남을 체력"을 HP 자리에 쓰면 현재 체력과 구분이 안 돼 혼란했다.
    /// 표시 수치는 실제 적용값(비늘·성벽 감소 + 체력 상한)이며 그 규칙은 CardInstance 단독 소유 —
    /// 뷰는 ClampDamage 호출만 한다(수식 복제 금지).</summary>
    public void ShowAttackPreview(int _damage, bool _wouldDie, bool _isAttackHit = true)
    {
        if (this.boundCard == null) return;

        // _isAttackHit=직격(공격) 프리뷰면 비늘 감소 반영, 반격 프리뷰면 false → 실제 TakeDamage와 일치.
        int t_dmg = this.boundCard.ClampDamage(_damage, _isAttackHit);

        if (this.damagePreviewText != null)
        {
            this.damagePreviewText.DOKill();
            this.damagePreviewText.text = $"-{t_dmg}";
            this.damagePreviewText.gameObject.SetActive(true);
        }
        else if (this.hpText != null)
        {
            // 폴백(프리팹 미배선): HP 라벨 자리에 데미지를 빨갛게. 해제 때 원복한다.
            this.hpTextOriginalColor = this.hpText.color;
            this.hpText.DOKill();
            this.hpText.text  = $"-{t_dmg}";
            this.hpText.color = Color.red;
            this.hpFallbackPreview = true;
        }

        // 치사 예고(카드 흐려짐 + HP 점멸)는 BattleUxFlags.DeathPreview로 블라인드 —
        // "이 카드는 못 잡는다"가 확정처럼 읽히고 흐려진 카드가 미관을 깬다는 판단. 되살릴 땐 플래그만 true.
        if (_wouldDie && BattleUxFlags.DeathPreview)
        {
            if (this.hpText != null)
                this.hpText.DOFade(0f, GameTiming.Battle.AttackPreviewFlash).SetLoops(-1, LoopType.Yoyo).SetLink(gameObject);
            this.cardAnim.ShowDeathPreview();
        }
    }

    public void HideAttackPreview()
    {
        if (this.damagePreviewText != null)
        {
            this.damagePreviewText.DOKill();
            this.damagePreviewText.text = string.Empty;
            this.damagePreviewText.gameObject.SetActive(false);
        }

        if (this.hpFallbackPreview && this.hpText != null && this.boundCard != null)
        {
            this.hpText.DOKill();
            Color t_c = this.hpTextOriginalColor;
            // 알파는 카드 몸통 상태 × 이 라벨의 기준 알파 — 1로 못박으면 흐려진 카드에서 숫자만 선명해진다.
            t_c.a = (this.cardAnim != null ? this.cardAnim.FadeTarget : 1f) * CardFadeAlpha.Of(this.hpText);
            this.hpText.color = t_c;
            RestoreHpDisplay();
            this.hpFallbackPreview = false;
        }

        // 플래그로 꺼져 있어도 호출 — 과거 상태/플래그 전환 직후의 잔존 오버레이 정리.
        this.cardAnim.HideDeathPreview();
    }

    /// <summary>표시용 HP를 임의 값으로 덮어쓴다. 규칙상 hp는 이미 확정됐는데(결정론: 상태변이 선행)
    /// 연출이 여러 번에 나눠 그 피해를 보여줄 때, 숫자만 단계적으로 따라오게 하는 용도다.
    /// **표시 전용** — CardInstance는 건드리지 않는다. 다음 Render/PlayHitAnim이 실제 값으로 되돌린다.</summary>
    public void OverrideHpDisplay(int _hp, int _bonusHp) => AnimateHpDisplay(Mathf.Max(0, _hp), _bonusHp);

    /// <summary>회복 표기를 **연출이 도착할 때까지** 미룬다. <see cref="CardInstance.Heal"/>(_showEffect:false) 전용 —
    /// 그 경로는 수치를 지금 적용하고(결정론: 상태는 동기) 표기는 투사체가 닿을 때 한다. 이 사이에 Render가 돌면
    /// 숫자만 먼저 올라가 "결과가 반영된 뒤에 이펙트가 오는" 그림이 된다 — 그걸 막는 래치다.
    /// 해제는 셋 중 먼저 오는 것: 도착(PlayHealEffect) / 다른 HP 변동 연출 / 슬롯 카드 교체·뒷면화.
    /// _amount는 **이 호출분의 회복량**이다 — 힐러가 둘이면 몫이 쌓이고 투사체마다 자기 몫만 올라간다
    /// (합계로 두면 첫 투사체가 남의 몫까지 올려버려 두 번째 투사체는 숫자가 안 움직인다).</summary>
    public void DeferHpDisplay(int _amount) => this.hpPendingHeal += Mathf.Max(0, _amount);

    /// <summary>HP 표기를 _hp까지 **굴린다**: 아이콘이 커지고 → 숫자가 빠르게 오르내리고 → 다시 작아진다.
    /// 시작점은 모델이 아니라 현재 표기값(shownHp) — 규칙은 이미 끝나 있고 여기선 그 차이를 보여줄 뿐이다.
    /// 순수 연출: RNG/게임상태 무관, 활성 클라 표시만.</summary>
    void AnimateHpDisplay(int _hp, int _bonusHp, bool _clearPending = true)
    {
        if (_clearPending) this.hpPendingHeal = 0;
        this.hpDisplayTarget = _hp;

        // 시작점은 **지금 눈에 보이는 숫자**다. 목표값을 먼저 shownHp에 넣어두면, 굴리는 도중 다음 갱신이
        // 들어왔을 때(무리 다단 착탄) 아직 화면에 뜨지도 않은 값에서 굴러 내려간다.
        KillHpRoll();
        int t_from = this.shownHp;

        // 변화가 없으면 팝도 굴림도 생략 — 0 피해(무적 소멸)·만피 회복에 아이콘만 들썩이지 않게.
        if (t_from == _hp && this.shownBonusHp == _bonusHp)
        {
            WriteHpDisplay(_hp, _bonusHp);
            return;
        }

        float t_popDur  = Mathf.Max(0.01f, GameTiming.Battle.HpPopDuration);
        float t_rollDur = Mathf.Clamp(Mathf.Abs(_hp - t_from) * GameTiming.Battle.HpRollPerStep,
                                      0.03f, Mathf.Max(0.03f, GameTiming.Battle.HpRollMax));

        // 보너스HP는 즉시 확정하고 숫자만 굴린다(둘 다 굴리면 어느 쪽이 움직이는지 안 읽힌다).
        WriteHpDisplay(t_from, _bonusHp);

        this.hpRollSeq = DOTween.Sequence().SetLink(gameObject);

        // 아이콘과 숫자가 **함께** 부푼다(Insert(0f) — Append로 이어 붙이면 대상마다 순서대로 늦게 커진다).
        foreach (HpPop t_pop in EnsureHpPops())
        {
            this.hpRollSeq.Insert(0f, t_pop.target.DOScale(t_pop.home * t_pop.scale, t_popDur).SetEase(Ease.OutBack));
            this.hpRollSeq.Insert(t_popDur + t_rollDur, t_pop.target.DOScale(t_pop.home, t_popDur).SetEase(Ease.InQuad));
        }

        // 숫자는 아이콘이 커진 **뒤부터** 굴러야 "아이콘이 커지고 → 체력이 달고 → 작아진다"로 읽힌다.
        this.hpRollSeq.Insert(t_popDur,
            DOVirtual.Int(t_from, _hp, t_rollDur, _v => WriteHpDisplay(_v, _bonusHp)).SetEase(Ease.Linear));
        this.hpRollSeq.OnComplete(() => WriteHpDisplay(_hp, _bonusHp));
    }

    /// <summary>표기를 즉시 _card 값으로 맞춘다(굴림 없음). 슬롯 재구성·바인딩 교체처럼 **연출이 아닌** 갱신 전용.</summary>
    void SnapHpDisplay(CardInstance _card)
    {
        KillHpRoll();
        this.hpDisplayTarget = _card.hp;
        WriteHpDisplay(_card.hp, _card.bonusHp);
    }

    /// <summary>프리뷰 등으로 덮어썼던 HP 라벨을 **현재 표기값**으로 되돌린다(모델값이 아니다 —
    /// 굴림/유예 중이면 모델은 이미 앞서 있어서 숫자가 튄다). 뒷면 카드는 다시 감춘다.</summary>
    void RestoreHpDisplay()
    {
        if (this.boundCard != null && !this.boundCard.isRevealed) SetHpDisplay("?", "");
        else WriteHpDisplay(this.shownHp, this.shownBonusHp);
    }

    /// <summary>팝 대상 한 건. home은 **원래 스케일**이다 — 1이라는 보장이 없어(아이콘은 프리팹에서 2배)
    /// 복귀값을 대상별로 들고 있어야 한다. 부푼 중간값을 기준으로 잡으면 굴릴 때마다 커진 채 남는다.</summary>
    struct HpPop
    {
        public Transform target;
        public Vector3   home;
        public float     scale;
    }
    HpPop[] hpPops;

    /// <summary>팝 대상 = 하트 아이콘 + HP 숫자. **둘 다** 커졌다 작아진다 —
    /// 아이콘만 부풀면 옆의 숫자가 그 자리에 못 박힌 것처럼 보인다.
    /// 배선은 프리팹마다 고정이라 1회만 만들고, 그때의 스케일을 복귀 기준으로 캡처한다.</summary>
    HpPop[] EnsureHpPops()
    {
        if (this.hpPops != null) return this.hpPops;

        int t_count = (this.hpIcon != null ? 1 : 0) + (this.hpText != null ? 1 : 0);
        var t_pops  = new HpPop[t_count];
        int t_i     = 0;
        if (this.hpIcon != null)
            t_pops[t_i++] = new HpPop { target = this.hpIcon, home = this.hpIcon.localScale, scale = this.hpIconPopScale };
        if (this.hpText != null)
            t_pops[t_i] = new HpPop { target = this.hpText.transform, home = this.hpText.transform.localScale, scale = this.hpTextPopScale };

        this.hpPops = t_pops;
        return this.hpPops;
    }

    /// <summary>진행 중인 굴림을 끊고 팝 대상을 전부 원래 크기로 되돌린다. 끊긴 트윈은 OnComplete를 안 타므로
    /// 스케일 복원을 여기서 직접 한다 — 안 하면 커진 아이콘·숫자가 그대로 굳는다.</summary>
    void KillHpRoll()
    {
        this.hpRollSeq?.Kill();
        this.hpRollSeq = null;

        if (this.hpPops == null) return;   // 한 번도 굴린 적 없으면 되돌릴 것도 없다(파괴 경로 포함)
        foreach (HpPop t_pop in this.hpPops)
        {
            if (t_pop.target == null) continue;
            t_pop.target.DOKill();
            t_pop.target.localScale = t_pop.home;
        }
    }

    /// <summary>숫자를 실제로 찍는 유일한 지점. 찍은 값이 곧 shownHp다 — 굴림 중간값도 포함해서
    /// **화면과 shownHp가 갈라지지 않는다**(다음 굴림의 시작점이 여기서 나온다).</summary>
    void WriteHpDisplay(int _hp, int _bonus)
    {
        this.shownHp      = _hp;
        this.shownBonusHp = _bonus;
        SetHpDisplay(_hp.ToString(), _bonus > 0 ? $"+{_bonus}" : "");
    }

    void SetHpDisplay(string _hp, string _bonus)
    {
        if (this.hpText != null) this.hpText.text = _hp;
        if (this.bonusHpText != null)
        {
            this.bonusHpText.text = _bonus;
            this.bonusHpText.gameObject.SetActive(_bonus.Length > 0);
        }
    }

    // ── 장식 계층(키워드 아이콘·프레임 장식·시너지 배지·글로우) 위임 셰임 ─────────
    // 실제 구현은 CardDecorView가 소유한다. 아래는 기존 호출부
    // (SynergyTriggers / CardPassive / AttackSequence / AttackFlow / HealerEffect / VfxDebugWindow /
    //  CardInputController)를 위한 전달일 뿐이다.

    // TODO: 호출부 이관 후 삭제
    public UniTask PlayPassiveGlow() => Decor.PlayPassiveGlow();

    // ── 입력 컨트롤러 전용 노출 2개 ──────────────────────────────────────
    // 롱프레스가 "누른 지점 아래에 시너지 배지가 있나 / 그 시너지를 몇 장 갖고 있나"를 물어야 해서 열어둔다.
    // TODO: CardInputController가 CardDecorView를 직접 보게 되면 여기서 삭제.

    /// <summary>배지 세로열 앵커(없으면 null). 배지엔 콜라이더가 없어 bounds 판정에 자식 순회가 필요하다.
    /// 배선 필드가 여기 남으므로 셰임이 아니라 그대로 읽는다(장식 뷰 생성을 강요하지 않는다).</summary>
    public Transform SynergyBadgeRoot => this.synergyBadgeRoot;

    /// <summary>이 카드에 마지막으로 그려진 확정 시너지 스냅샷(없으면 null). 보유 장수 조회용 — 재계산 금지.</summary>
    // TODO: 호출부 이관 후 삭제
    public SynergyState LastBadgeState => Decor.LastBadgeState;

    // TODO: 호출부 이관 후 삭제 — 본체는 BattleBoardView.
    public static CardView GetView(CardInstance _card) => BattleBoardView.GetView(_card);

    /// <summary>시너지 효과가 실제 발동한 순간 해당 배지를 pop(순수 연출, 게임상태/RNG 무관).</summary>
    // TODO: 호출부 이관 후 삭제
    public void PopSynergyBadge(SynergyData _synergy) => Decor.PopSynergyBadge(_synergy);

    /// <summary>키워드 글로우 재생(아이콘 pop 동반). 색·유지시간·프리팹은 KeywordIconConfig(SO)가 소유한다.</summary>
    // TODO: 호출부 이관 후 삭제
    public UniTask PlayKeywordGlow(CardKeyword _kw) => Decor.PlayKeywordGlow(_kw);
    #endregion

    #region Animation delegates
    public Vector3 SlotPosition                    => this.cardAnim.SlotPosition;

    /// <summary>카드 아래 중앙(월드). "카드 밑에서 뭔가 나오는" 연출의 발사점(힐 투사체 등).
    /// 콜라이더가 없으면 카드 원점 폴백. z는 카드 평면을 그대로 쓴다(투사체가 카드 뒤로 빠지지 않게).</summary>
    public Vector3 BottomCenter => this.selfCollider != null
        ? new Vector3(this.selfCollider.bounds.center.x, this.selfCollider.bounds.min.y, transform.position.z)
        : transform.position;

    /// <summary>이 카드가 쓰는 정렬 레이어. 구매 에셋 VFX(Default 레이어)를 카드 앞으로 올릴 때 기준.</summary>
    public int VfxSortingLayerId => this.illustration != null ? this.illustration.sortingLayerID : 0;

    /// <summary>적 진영 카드인가. VFX 오프셋/회전 flip 판정의 단일 기준 — 아군/적 배치가 위아래로 뒤집혀 있다.</summary>
    public bool IsEnemySide => this.boundCard != null && this.boundCard.ownerIndex != TurnState.LocalOwnerIndex;
    public UniTask MoveToCenter()                  => this.cardAnim.MoveToCenter();
    public UniTask MoveToCinemaSlot()              => this.cardAnim.MoveToCinemaSlot();
    public UniTask MoveToCinemaPosition(int _posIndex, int _totalCount) => this.cardAnim.MoveToCinemaPosition(_posIndex, _totalCount);
    public UniTask MoveTo(Vector3 _pos)            => this.cardAnim.MoveTo(_pos);
    public UniTask MoveToSlot()                    => this.cardAnim.MoveToSlot();
    /// <summary>_hitFrom = 때린 쪽의 뷰(없으면 환경 피해). 먼지처럼 방향을 따르는 항목이
    /// "맞은 방향의 반대"로 튀도록 진행 방향을 넘긴다.</summary>
    public async UniTask PlayHitAnim(float _d = 0.15f, int _damage = 0, CardView _hitFrom = null)
    {
        // 숫자는 즉시 최종값으로 튀지 않고 굴러 내려간다(아이콘 팝 → 6·5·4·3 → 복귀).
        if (this.boundCard != null)
            AnimateHpDisplay(this.boundCard.hp, this.boundCard.bonusHp);
        // 피격 파티클은 라이브러리 소유(미배선이면 무동작). 붐/숫자는 프리팹의 HitEffectView가 계속 담당 —
        // 그쪽은 카드에 상주하며 상태(시퀀스/숫자)를 가지므로 1회성 파티클과 축이 다르다.
        Vector3 t_awayDir = _hitFrom != null ? transform.position - _hitFrom.transform.position : default;
        t_awayDir.z = 0f;   // 화면 평면 방향만 — 시네마 중 z가 벌어져 있으면 먼지가 카메라 쪽으로 튄다
        // 먼지의 양·속도도 화면 흔들림·카드 반동과 같은 세기(피해/최대체력)를 따른다 — 세 연출이 갈리면
        // "센 공격"이 한쪽에서만 세게 읽힌다. 세기 반응이 배선된 항목만 이 값을 쓴다.
        BattleVfx.PlayAttached(BattleVfxId.Hit, transform, IsEnemySide, VfxSortingLayerId, t_awayDir,
                               HitImpact.Strength01(_damage, this.boundCard));
        // 먼지가 튀는 방향과 카드가 밀리는 방향은 같아야 한다 — 같은 t_awayDir를 그대로 넘긴다.
        await this.cardAnim.PlayHitAnim(_d, _damage, t_awayDir);
    }
    /// <summary>사망 연출. **HP 굴림이 끝난 뒤에** 시작한다 — 카드가 줄어들며 사라지는 도중에 숫자가
    /// 0까지 굴러가면 얼마를 맞고 죽었는지가 안 읽히고, 페이드로 흐려진 숫자 위에서 굴림만 헛돈다.
    /// 순수 연출 대기다 — 규칙(hp·사망 판정)은 이미 확정된 뒤라 이 대기가 게임 판정을 미루지 않는다.</summary>
    public async UniTask PlayDeathAnim(float _d = 0.4f)
    {
        await WaitHpRollSettled();
        await this.cardAnim.PlayDeathAnim(_d);
    }

    /// <summary>진행 중인 HP 굴림이 끝날 때까지 기다린다(없으면 즉시 반환).
    /// 굴림이 중간에 끊기면(카드 교체·파괴·다음 변동) 취소로 빠져나오며 기다림도 거기서 끝난다 —
    /// 대기가 연출 하나에 매달려 턴 흐름을 붙잡지 않게.</summary>
    public async UniTask WaitHpRollSettled()
    {
        Sequence t_seq = this.hpRollSeq;
        if (t_seq == null || !t_seq.IsActive()) return;
        await t_seq.ToUniTask().SuppressCancellationThrow();
    }

    /// <summary>회복 연출(회복 파티클 + "+N") + HP 표기 갱신. CardInstance.Heal/ReviveAtHalf가 실제 회복량으로 호출.
    /// 회복이면 경로(힐러/돌보미/청소부/유산/부활) 불문 여기 하나로 수렴한다.</summary>
    public void PlayHealEffect(int _amount, bool _consumeDeferred = false)
    {
        // 힐러 경로는 여기가 **표기의 발화점**이다 — 수치는 턴 시작에 이미 들어갔고(결정론),
        // 숫자는 투사체가 닿는 지금부터 굴러 오른다(그때까지는 DeferHpDisplay가 붙잡고 있었다).
        // 유예분이 남아 있으면 이번 도착 몫(_amount)만 올린다. 모델 hp를 넘지 않게 잘라 두면
        // 도중에 맞아서 hp가 내려간 경우에도 숫자가 실제보다 높이 뜨지 않는다.
        if (this.boundCard != null)
        {
            int t_consumed = _consumeDeferred ? Mathf.Min(this.hpPendingHeal, _amount) : 0;
            this.hpPendingHeal -= t_consumed;

            // 즉시 회복은 자기 몫을 바로 목표에 더하고, 지연 회복은 실제로 소비한 pending 몫만 더한다.
            // 모델 hp에서 아직 남은 pending을 뺀 값을 상한으로 삼아, 다음 투사체 몫을 먼저 노출하지 않는다.
            int t_step      = _consumeDeferred ? t_consumed : _amount;
            int t_revealed  = this.boundCard.hp - this.hpPendingHeal;
            int t_target    = Mathf.Min(this.hpDisplayTarget + t_step, t_revealed);
            AnimateHpDisplay(t_target, this.boundCard.bonusHp, _clearPending: false);
        }
        BattleVfx.PlayAttached(BattleVfxId.Heal, transform, IsEnemySide, VfxSortingLayerId);
        this.cardAnim.PlayHealEffect(_amount);   // 숫자("+N") — 붐 스프라이트는 프리팹에서 비우면 파티클만 남는다
    }
    public void FadeView(float _alpha, float _dur) => this.cardAnim.FadeView(_alpha, _dur);

    public void HideSlot()
    {
        this.emptyOverlay.SetActive(false);
        this.cardAnim.FadeView(0f, 0.3f);
    }

    /// <summary>슬롯 배치 연출. 카드별 등장 연출 분기점 — 판정은 CardData.cinemaAttackStyle 하나로,
    /// **공격 시네마와 같은 축을 쓴다**(같은 에너지 구체를 공유하는 한 몸 연출이라 배선을 갈라두지 않는다).
    /// 호출부(BattleFieldView·BattleIntro)는 분기를 몰라도 되도록 여기 한 곳에서만 갈린다.</summary>
    public async UniTask PlayDealAnim(Vector3 _from, Vector3 _mid, Vector3 _dest, float _duration = 0.6f)
    {
        if (!UsesOrbAppear)
        {
            await this.cardAnim.PlayDealAnim(_from, _mid, _dest, _duration);
            PlayPlacedEmblems();
            return;
        }

        // 구체 등장도 앞 토막(덱에서 나와 중앙 확대·정지)을 그대로 탄다 — 카드 정보를 보여주는 구간이
        // 이 연출에만 없으면 고등급 카드가 오히려 뭔지 모른 채 지나간다. 중간 정지는 일반 배치와 같은 값.
        await PlayDealToMid(_from, _mid, _dest, _duration);
        bool t_cancelled = await UniTask.Delay((int)(GameTiming.Battle.DealMidPause * 1000),
                cancellationToken: this.GetCancellationTokenOnDestroy())
            .SuppressCancellationThrow();
        if (t_cancelled) return;

        await PlayDealToSlot(_mid, _dest, _duration);
    }

    /// <summary>롱프레스로 이 카드의 정보를 보는 동안 살짝 떠오르게 한다(손 떼면 false로 복귀).
    /// 뜨는 폭·크기·시간은 CardAnimator의 인스펙터 값이 정한다.</summary>
    public void SetLongPressLift(bool _on) => this.cardAnim?.SetLongPressLift(_on);

    /// <summary>배치 연출을 **중앙에서 끊어** 두 토막으로 쓰는 경로(등장 컷씬용).
    /// 앞 토막은 화면 밖 → 중앙까지만 가고 거기 멈춘다. 컷씬이 끝나면 PlayDealToSlot이 이어받는다.
    /// 구체 등장도 같은 앞 토막을 쓴다 — 중앙에 선 카드가 뒤 토막에서 구체로 변신한다.</summary>
    public UniTask PlayDealToMid(Vector3 _from, Vector3 _mid, Vector3 _dest, float _duration = 0.6f)
        => this.cardAnim.PlayDealToMid(_from, _mid, _dest, _duration);

    /// <summary>뒤 토막: 중앙 → 슬롯. 구체 등장 카드는 중앙에 선 카드가 구체로 변신한 뒤 날아간다
    /// (_morphFromCard) — 카드가 이미 중앙에 있으므로 구체를 새로 "생성"하면 카드가 순간 사라져 보인다.</summary>
    public async UniTask PlayDealToSlot(Vector3 _mid, Vector3 _dest, float _duration = 0.6f)
    {
        await (UsesOrbAppear
            ? CardAppearVfx.PlayOrbCurve(this, _mid, _dest, _duration, _morphFromCard: true)
            : this.cardAnim.PlayDealToSlot(_dest, _duration));
        PlayPlacedEmblems();
    }

    /// <summary>[Placed] 배치 상징 발화점. 카드가 슬롯에 <b>실제로 내려앉은 뒤</b> 한 번 —
    /// 규칙 쪽(SynergyTriggers.Placed)은 뷰가 생기기 전에 돌아 못 쓴다(SynergyEmblemVfx.PlayPlaced 주석 참조).
    /// 구체 등장 경로는 PlayDealAnim이 PlayDealToSlot을 거쳐 가므로 여기 한 번만 걸린다(이중 재생 없음).
    /// 어느 시너지가 뜰지는 SynergyData.emblemTiming이 정한다 — 여기선 타이밍만 알린다.</summary>
    void PlayPlacedEmblems()
        => SynergyEmblemVfx.PlayPlaced(this, this.boundCard, this.activeSynergyState);

    bool UsesOrbAppear => this.boundCard?.data != null
                       && this.boundCard.data.cinemaAttackStyle == CinemaAttackStyle.EnergyOrbDash;

    public UniTask RestoreAfterAttack() => this.cardAnim.MoveToSlot();
    public void InitializeAnimator()    => this.cardAnim.Initialize();

    // 보드 전체 페이드 셰임. 본체는 BattleBoardView.
    // TODO: 호출부 이관 후 삭제
    public static float ForcedDimAlpha
    {
        get => BattleBoardView.ForcedDimAlpha;
        set => BattleBoardView.ForcedDimAlpha = value;
    }

    // TODO: 호출부 이관 후 삭제
    public static void FadeAll(float _alpha) => BattleBoardView.FadeAll(_alpha);

    // TODO: 호출부 이관 후 삭제
    public static void FadeTeam(float _alpha, int _ownerIndex) => BattleBoardView.FadeTeam(_alpha, _ownerIndex);

    // TODO: 호출부 이관 후 삭제
    public static void FadeCards(float _alpha, params CardView[] _cards) => BattleBoardView.FadeCards(_alpha, _cards);

    // TODO: 호출부 이관 후 삭제
    public static void RestoreAllFades() => BattleBoardView.RestoreAllFades();

    /// <summary>이 슬롯 자리를 감싸는 월드 AABB. 카드가 연출로 움직이거나 확대돼 있어도
    /// **슬롯 좌표 + 기본 카드 크기** 기준이라, 이걸로 뚫은 튜토리얼 포커스 구멍이 흔들리지 않는다.
    /// 크기는 강조 스프라이트(sliced size)에서 얻고 미배선이면 상수로 폴백한다.</summary>
    public Bounds SlotWorldBounds
    {
        get
        {
            Vector2 t_size = this.selectedHighlight != null
                          && this.selectedHighlight.drawMode != SpriteDrawMode.Simple
                ? this.selectedHighlight.size
                : DefaultCardSize;
            return new Bounds(this.SlotPosition, new Vector3(t_size.x, t_size.y, 0.01f));
        }
    }

    static readonly Vector2 DefaultCardSize = new Vector2(2f, 3f);

    /// <summary>이 카드 한 장을 감싸는 화면 px 사각. 튜토리얼 카드 포커스가 뚫을 구멍.</summary>
    public Rect ScreenBounds(float _paddingPx = 0f)
        => CameraUtil.WorldBoundsToScreenRect(this.SlotWorldBounds, _paddingPx);

    public static void Cleanup()
    {
        BattleSelection.Cleanup();   // 탭 무장 상태/공격 이벤트 구독 리셋(호출 지점은 여기 하나뿐)
        BattleBoardView.Cleanup();   // 보드 레지스트리/페이드 정책 리셋(호출 지점은 여기 하나뿐)
    }

    // TODO: 호출부 이관 후 삭제
    public void PlayAttackAnim() => WeaponView.PlayAttackAnim(this.boundCard);

#endregion

    #region Debug
    // 그리는 내용이 전부 입력 상태(드래그 방향/데드존/조준 타깃)라 본문은 CardInputController가 갖는다 —
    // 여기 두면 dragState/activeGesture/조준 좌표를 도로 노출해야 한다.
    void OnDrawGizmos() => this.inputCtrl?.DrawGizmos();
    #endregion
}

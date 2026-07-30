using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;
using TMPro;

[RequireComponent(typeof(CardAnimator))]
public class CardView : MonoBehaviour
{
    #region Static / Events
    public static event System.Action<CardView, CardView> OnAttack;

    static readonly List<CardView> allViews = new List<CardView>();
    static bool s_anyDragging;
    static CardView s_selectedAttacker;   // 탭 공격(제스처3)으로 무장된 공격자. null=미무장.
    static List<CardView> s_previewTargets;   // 무장 시 HP 프리뷰를 켠 타겟들(해제 시 끄기용).
    static bool s_tauntNoticeShown;   // 이번 무장에서 도발 차단 안내를 이미 띄웠나(연타 배너 스팸 방지).

    const string TAUNT_BLOCKED_TEXT = "도발 카드를 먼저 공격해야 합니다";
    const float  REJECT_SHAKE_DUR      = 0.22f;
    const float  REJECT_SHAKE_STRENGTH = 0.12f;

    // ForcedAttacker 활성 시 나머지 로컬 카드에 적용할 암전 alpha. 일반 전투(처형 재무장)는 0.3,
    // 튜토리얼은 "그 카드 말고 다 검게" 위해 더 낮은 값으로 덮어쓴다(PlayerTurn이 설정).
    public static float ForcedDimAlpha = 0.3f;

    enum DragState { Idle, AttackDrag }

    // 한 터치의 제스처 종류. 손가락이 dragThreshold를 넘는 순간 초기 세로 방향으로 확정, 그 터치 끝까지 고정.
    // DragUp=위로 떠서 적에게 끌기(콜라이더 타깃). DragDown=아래로 끌기(좌우반전 방향 타깃). None=미확정/탭.
    enum Gesture { None, DragUp, DragDown }
    #endregion

    #region Fields
    [Header("Core")]
    [SerializeField] CardAnimator cardAnim;

    [Header("UI")]
    [SerializeField] TMP_Text hpText;
    [SerializeField] TMP_Text bonusHpText;
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
    // true면 키워드 아이콘을 시너지 배지 자리(좌측 세로열)에 그리고, 시너지 배지는 표시하지 않는다.
    // 한 자리에 둘 다 그리면 겹치므로 "그 자리의 주인"은 이 스위치 하나가 정한다(양쪽 분기의 단일 진실원).
    // false로 되돌리면 종전대로 키워드=우하단 가로줄 + 시너지 배지 복귀.
    [SerializeField] bool keywordIconsUseSynergySlot = true;

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

    Collider2D selfCollider;   // 롱프레스 팝업을 끌 때 "카드 범위를 벗어났는지" 판정용
    [SerializeField] float dirThreshold = 0.35f;
    [SerializeField] HintArrow hintArrow;
    [SerializeField] SwipeGuide swipeGuide;
    [SerializeField] LineRenderer dragLine;

    CardInstance boundCard;
    Vector3 centerPos;
    Vector2 dragStartScreenPos;
    Vector2 currentDragScreenPos;
    Vector2 touchStartScreenPos;
    DragState dragState;
    Gesture activeGesture;   // 이번 터치의 제스처(위/아래 드래그). 탭은 None 유지.
    CardView currentTarget;
    CardView rejectedTarget;   // 직전에 거절 연출을 띄운 무효 타깃(프레임마다 반복 발화 방지). 대상이 바뀌면 다시 발화.
    GameObject weaponInstance;
    bool       hasAimTilt;    // 조준 기울기가 걸려 있나(복원 필요 여부)
    Quaternion aimTiltBase;   // 기울기 걸기 직전의 로컬 회전 — 조준 종료 시 여기로 되돌린다

    // 무장 중 카드에 붙어 있는 이펙트(풀 대여분 + 반납 키). 비어 있으면 꺼진 상태.
    readonly List<(string Id, GameObject Go)> armedVfx = new List<(string, GameObject)>();
    GameObject armedVfxPrefabOverride;  // 테스터가 후보를 갈아끼울 때만 사용. null=카드 에셋 값
    Animator weaponAnimator;
    Quaternion weaponBaseRot;
    bool longPressFired;
    bool longPressSynergyShown;   // true면 카드 정보가 아니라 시너지 설명 팝업을 띄운 상태
    CancellationTokenSource longPressCts;
    Color hpTextOriginalColor;
    readonly Dictionary<CardKeyword, GameObject> iconMap = new Dictionary<CardKeyword, GameObject>();

    public CardInstance BoundCard => this.boundCard;
    #endregion

    #region Unity Lifecycle
    void Awake()
    {
        allViews.Add(this);
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
    }
    void OnDestroy()
    {
        allViews.Remove(this);
        HideArmedVfx();   // 풀 대여분을 물고 죽으면 풀이 파괴된 오브젝트를 들고 있게 된다
        if (this.hpText != null) this.hpText.DOKill();
    }

    // 튜토리얼: hintArrow를 강제 표시(정상 Update 로직 무시). 안내 중 "여기서 드래그" 포인터.
    bool tutorialPointer;
    public void SetTutorialPointer(bool _on)
    {
        this.tutorialPointer = _on;
        if (this.hintArrow != null) this.hintArrow.SetVisible(false);   // 화살표 완전 비표시
    }

    void Update()
    {
        // 입력이 닫히면(턴 종료/타임아웃 등) 무장된 탭 공격자 강조가 고착되지 않게 해제.
        if (s_selectedAttacker != null && !TurnState.InputAllowed)
            ClearAttackerSelection();

        // 들고(드래그) 있다가 입력이 닫히면(생각시간 초과/턴 종료) 슬롯으로 복귀 — 센터에 고착 방지.
        // 이 카드가 그대로 공격자가 되면 AttackSequence의 DOKill이 이 이동을 덮으므로 충돌 안 남.
        if (this.dragState != DragState.Idle && !TurnState.InputAllowed)
        {
            s_anyDragging  = false;
            this.dragState = DragState.Idle;
            this.swipeGuide?.SetVisible(false);
            HideDragLine();
            ClearTargetPreview();
            RestoreAllFades();
            FocusWeapon(false);
            SetArmedVfx(false);   // 입력이 닫혀 무장 해제 — 공격으로 이어지지 않으므로 여기서 정리
            ResetAimTilt();
            this.cardAnim.MoveToSlot().Forget();
        }

        if (this.hintArrow == null) return;
        this.hintArrow.SetVisible(false);   // 가이드 화살표 완전 비표시(일반+튜토리얼)
    }
    #endregion

    #region Input
    void OnMouseDown()
    {
        if (!TurnState.InputAllowed || this.boundCard == null) return;
        if (TurnState.ForcedAttacker != null && this.boundCard.ownerIndex == TurnState.LocalOwnerIndex
            && this.boundCard != TurnState.ForcedAttacker) return;   // 적 카드는 통과(탭 공격 발사 위해)

        CancelLongPress();
        this.longPressCts = CancellationTokenSource.CreateLinkedTokenSource(
            this.GetCancellationTokenOnDestroy());
        WaitLongPress(this.longPressCts.Token).Forget();

        this.touchStartScreenPos  = (Vector2)Input.mousePosition;
        this.dragStartScreenPos   = this.touchStartScreenPos;
        this.currentDragScreenPos = this.touchStartScreenPos;
        this.activeGesture        = Gesture.None;   // 새 터치 — 제스처 미확정(탭/드래그 판정 대기).

        if (this.boundCard.ownerIndex != TurnState.LocalOwnerIndex) return;

        this.dragState   = DragState.Idle;


        // 떠오르는 자리는 CardAnimator.MoveToCenter와 같은 규칙으로 잡는다 — 슬롯의 z·y를 그대로 두고
        // 화면 가로 중앙만 취한다. transform.position.z에서 0.5를 빼면 원근 때문에 그 점이 소실점 쪽으로
        // 끌려가 카드가 자기 줄보다 위로 떠 보인다(= 필드 중앙이 아니라 화면 중앙으로 가는 증상).
        this.centerPos   = CameraUtil.ScreenFractionToWorld(0.5f, 0.5f, this.cardAnim.SlotPosition.z);
        this.centerPos.y = this.cardAnim.SlotPosition.y;

        float t_destY = Camera.main.WorldToScreenPoint(this.centerPos).y;
        this.dragStartScreenPos   = new Vector2(Screen.width * 0.5f, t_destY);
        this.currentDragScreenPos = this.dragStartScreenPos;

        // 무장 연출(무기 조준 + 이펙트)은 드래그 조준일 때만 누르는 순간 켠다.
        // 드래그가 꺼진 지금은 "선택된 공격자"만 무장 상태여야 하므로 ToggleSelectAttacker가 켠다 —
        // 여기서 켜면 선택되지 않은 카드에 이펙트가 남아 누가 공격자인지 흐려진다.
        if (!BattleUxFlags.DragAimAttack) return;

        FocusWeapon(true);
        SetArmedVfx(true);   // 무장(드래그) 시작
    }

    void OnMouseDrag()
    {
        if (!TurnState.InputAllowed || this.boundCard == null) return;

        // 처형/튜토리얼 지정 공격자 외 카드는 조작 불가(완전 무반응).
        if (TurnState.ForcedAttacker != null && this.boundCard.ownerIndex == TurnState.LocalOwnerIndex
            && this.boundCard != TurnState.ForcedAttacker) return;   // 적 카드는 통과(탭 공격 발사 위해)

        this.currentDragScreenPos = (Vector2)Input.mousePosition;

        // 튜토리얼 Inspect: 적 카드 롱프레스 팝업은 손 뗄 때까지 유지 — 작은 드리프트/데드존으로
        // 사라지지 않게 취소 로직을 건너뛴다(실제 소비는 OnMouseUp의 NotifyInspected).
        if (this.longPressFired && IsTutorialInspectTarget()) return;

        // 카드 범위를 벗어나면 즉시 설명 팝업을 닫는다.
        // dragThreshold 조기 return보다 **앞**에 둬야 한다 — 적 카드는 dragStartScreenPos가
        // 터치 지점이라 작은 드래그가 아래 return에 걸려 취소 판정 자체를 못 받는다.
        if (this.longPressFired && PointerLeftSelf())
            CancelLongPress();

        // 드래그 조준 공격 블라인드: 기본 UX(탭 공격)만 남기기로 해서 제스처 확정 이전에 끊는다.
        // 여기서 끊으면 HandleAimDrag/UpdateTarget/SwipeGuide/조준 기울기가 아예 실행되지 않고,
        // activeGesture가 계속 None이라 손 뗄 때 탭 판정이 그대로 성립한다.
        // 위쪽 롱프레스 취소 로직은 통과시킨다 — 정보 팝업은 공격 UX가 아니므로 유지.
        if (!BattleUxFlags.DragAimAttack)
        {
            if (Vector2.Distance(this.currentDragScreenPos, this.touchStartScreenPos) > this.deadZoneRadius)
                CancelLongPress();
            return;
        }

        Vector2 t_touchDrag = this.currentDragScreenPos - this.touchStartScreenPos;

        // 제스처 미확정: 손가락 이동이 dragThreshold를 넘는 순간 초기 세로 방향으로 확정(위=적에게 끌기, 아래=좌우반전).
        if (this.activeGesture == Gesture.None)
        {
            if (t_touchDrag.magnitude < this.dragThreshold) return;   // 아직 탭 범위 — 대기.
            Gesture t_new = t_touchDrag.y >= 0f ? Gesture.DragUp : Gesture.DragDown;
            // 튜토리얼: 이번 스텝이 가르치는 조작이 아니면 확정하지 않고 무반응 —
            // 미확정(None)으로 남으므로 이후 반대 방향으로 끌면 그때 정상 확정된다.
            if (!GestureAllowed(t_new)) return;
            this.activeGesture = t_new;
            // 드래그 시작 — 대기 중인 탭 무장 취소(입력 상호배타).
            // 무장돼 있던 카드가 곧 드래그될 이 카드면 즉시 원복(_instant): 바로 뒤 MoveTo의 DOKill이
            // 축소 트윈을 잘라 1.15배로 굳는다(탭 무장 → 그대로 드래그 공격 시 확대 잔류 버그).
            ClearAttackerSelection(_instant: s_selectedAttacker == this);
        }
        else
        {
            // 드래그 중 세로 방향이 크게 반대로 바뀌면 모드 전환(히스테리시스 = deadZoneRadius).
            // 예: 드래그-백(아래) 중 손가락을 시작점보다 위로 올리면 적-드래그로 전환.
            Gesture t_switch = this.activeGesture;
            if (this.activeGesture == Gesture.DragDown && t_touchDrag.y >  this.deadZoneRadius) t_switch = Gesture.DragUp;
            else if (this.activeGesture == Gesture.DragUp && t_touchDrag.y < -this.deadZoneRadius) t_switch = Gesture.DragDown;

            if (!GestureAllowed(t_switch)) t_switch = this.activeGesture;   // 튜토리얼: 차단된 모드로는 전환 금지

            if (t_switch != this.activeGesture)
            {
                SwitchGesture();               // 이전 모드 정리 + 새 핸들러 재초기화 유도.
                this.activeGesture = t_switch;
            }
        }

        if (Vector2.Distance(this.currentDragScreenPos, this.touchStartScreenPos) > this.deadZoneRadius)
            CancelLongPress();

        if (this.boundCard == null || this.boundCard.ownerIndex != TurnState.LocalOwnerIndex) return;

        Vector2 t_drag = this.currentDragScreenPos - this.dragStartScreenPos;   // DragBack 조준용(화면 중앙 기준).

        HandleAimDrag(t_drag, _forward: this.activeGesture != Gesture.DragDown);
    }

    /// <summary>조준 방향으로 카드를 살짝 기울인다. _aimX는 조준 방향의 좌우 성분(-1~1).
    /// 오른쪽을 겨누면 오른쪽으로 눕는다 — Z축은 반시계가 +라 부호를 뒤집는다(돌진 lean과 같은 규약).
    ///
    /// 트윈 대신 매 프레임 보간이다. 드래그 프레임마다 DOTween을 새로 걸면 이전 트윈과 겹쳐 튀고,
    /// SetTargetFocus 등의 DOKill에 조용히 잘린다. 지수 감쇠라 프레임레이트가 달라도 수렴 속도가 같다.</summary>
    void ApplyAimTilt(float _aimX)
    {
        if (!this.hasAimTilt)
        {
            this.hasAimTilt = true;
            this.aimTiltBase = transform.localRotation;   // 조준 시작 각도 = 복원 지점
        }

        float      t_angle  = -Mathf.Clamp(_aimX, -1f, 1f) * this.aimTiltMaxAngle;
        Quaternion t_target = this.aimTiltBase * Quaternion.Euler(0f, 0f, t_angle);
        float      t_t      = 1f - Mathf.Exp(-this.aimTiltSpeed * Time.deltaTime);
        transform.localRotation = Quaternion.Slerp(transform.localRotation, t_target, t_t);
    }

    /// <summary>조준 기울기 해제 — 시작 각도로 즉시 복원. 조준이 끝나는 모든 경로에서 부른다.
    /// **공격 발동 전에도 반드시** 호출해야 한다: Headbutt이 현재 각도를 baseRot으로 잡아
    /// 복귀 목표로 쓰므로, 기울어진 채 넘기면 공격 후 카드가 비스듬히 굳는다.</summary>
    void ResetAimTilt()
    {
        if (!this.hasAimTilt) return;
        this.hasAimTilt = false;
        transform.localRotation = this.aimTiltBase;
    }

    /// <summary>튜토리얼 조작 게이트: 이번 스텝이 가르치는 제스처만 통과. 탭은 Gesture.None으로 표현한다.
    /// Any면 전부 허용(일반 전투). 차단된 제스처는 상태를 남기지 않고 완전 무반응 처리한다.</summary>
    static bool GestureAllowed(Gesture _g)
    {
        switch (TurnState.AllowedGesture)
        {
            case InputGesture.DragUp:        return _g == Gesture.DragUp;
            case InputGesture.DragDown:      return _g == Gesture.DragDown;
            case InputGesture.Tap:           return _g == Gesture.None;
            case InputGesture.LongPressOnly: return false;
            default:                         return true;
        }
    }

    // 드래그 모드 전환 시 이전 모드 조준만 정리. 카드는 센터에 그대로 둔다(dragState 유지) —
    // 예전엔 Idle로 리셋해 재초기화를 유도했지만, 그러면 전환 때마다 슬롯 복귀→센터 재이동 왕복이 보인다.
    // 위/아래가 둘 다 같은 조준 모드가 된 뒤로는 자리를 바꿀 이유가 없다.
    void SwitchGesture()
    {
        HideDragLine();
        ClearTargetPreview();
    }

    /// <summary>조준 드래그 공통 처리. 드래그-백/포워드는 딱 하나만 다르다:
    /// 조준 방향 부호(밀기 = 그 방향, 당기기 = 반대 방향). 가이드는 조준 방향(t_aimDir.x)을 그대로 받으므로
    /// 모드가 바뀌면 자연히 반전되고, 가이드가 놓이는 쪽(위/아래)만 _forward를 따른다.
    ///
    /// 손가락이 시작점 기준 위/아래 어디에 있든 조준을 유지한다 — 제스처는 OnMouseDrag가 이미 확정했고,
    /// 양쪽 다 조준 모드라 "반대편으로 넘어감 = 취소"가 성립하지 않는다. 예전의 슬롯 복귀(ReturnDrag)는
    /// 모드 전환 구간에서 슬롯→센터 왕복으로만 보였다. 취소는 그냥 손을 떼면 된다(OnMouseUp이 복귀시킨다).</summary>
    void HandleAimDrag(Vector2 _drag, bool _forward)
    {
        UIPoolManager.Instance?.HideUI<PooledCardElement>();

        if (this.dragState != DragState.AttackDrag)
        {
            this.dragState = DragState.AttackDrag;
            s_anyDragging  = true;
            BeginTargeting();
            var t_validTargets = GetValidEnemyViews();
            foreach (var t_cv in t_validTargets)
                if (t_cv.boundCard.HasKeyword(CardKeyword.Taunt))
                    t_cv.PlayKeywordGlow(CardKeyword.Taunt).Forget();
            ApplyDragTargetFade(t_validTargets);
            this.cardAnim.MoveTo(this.centerPos).Forget();
            this.swipeGuide?.SetVisible(true);
        }

        // 포워드(위로 끌기)는 가이드도 카드 위, 드래그-백은 아래. 모드가 바뀌는 순간 자리가 따라오게
        // 매 프레임 갱신한다(같은 쪽이면 SetAbove가 내부에서 무시).
        this.swipeGuide?.SetAbove(_forward);

        Vector2 t_aimDir = (_forward ? _drag : -_drag).normalized;
        this.swipeGuide?.UpdateDirection(t_aimDir.x);
        ApplyAimTilt(t_aimDir.x);

        if (_drag.magnitude < this.deadZoneRadius)
        {
            ClearTargetPreview();
            return;
        }

        UpdateTarget(t_aimDir);
    }

    Vector3 GetMouseWorldOnCardPlane()
    {
        Ray t_ray = Camera.main.ScreenPointToRay(this.currentDragScreenPos);
        Plane t_plane = new Plane(-Camera.main.transform.forward, transform.position);
        if (t_plane.Raycast(t_ray, out float t_enter))
        {
            Vector3 t_world = t_ray.GetPoint(t_enter);
            t_world.z = transform.position.z;
            return t_world;
        }
        return transform.position;
    }

    void HideDragLine()
    {
        if (this.dragLine != null) this.dragLine.enabled = false;
    }

    void OnMouseUp()
    {
        // 처형/튜토리얼 지정 공격자 외 카드는 완전 무반응(클릭·탭 무시). 드래그를 시작한 적 없으니
        // s_anyDragging도 건드리지 않는다.
        if (TurnState.ForcedAttacker != null && this.boundCard != null
            && this.boundCard.ownerIndex == TurnState.LocalOwnerIndex
            && this.boundCard != TurnState.ForcedAttacker)
            return;   // 적 카드는 통과(무장된 공격자 → 적 탭 발사 위해)

        // 드래그 중 턴 종료·카드 사망으로 early-return해도 정적 드래그 상태(s_anyDragging)가
        // true로 고착되지 않도록 가드 통과 전에 반드시 해제한다.
        if (!TurnState.InputAllowed || this.boundCard == null)
        {
            s_anyDragging  = false;
            this.dragState = DragState.Idle;
            this.swipeGuide?.SetVisible(false);
            HideDragLine();
            return;
        }
        s_anyDragging = false;
        this.swipeGuide?.SetVisible(false);
        HideDragLine();
        bool t_wasLongPress = this.longPressFired;
        CancelLongPress();

        if (t_wasLongPress)
        {
            UIPoolManager.Instance?.HideUI<PooledCardElement>();

            // 튜토리얼 Inspect 스텝: 적 카드 롱프레스 = "상대 정보 확인" 레슨. 팝업이 뜨는 순간이 아니라
            // **손을 뗀 순간**에 인정한다 — 뜨자마자 스텝이 소비되어 팝업을 못 읽는 버그 방지.
            if (TutorialConfig.IsActive && this.boundCard != null
                && this.boundCard.ownerIndex != TurnState.LocalOwnerIndex)
                TutorialOverlayUI.Instance?.NotifyInspected();

            if (this.boundCard?.ownerIndex == TurnState.LocalOwnerIndex)
            {
                if (this.currentTarget != null)
                {
                    ResetAimTilt();   // 기울어진 채 넘기면 Headbutt이 그 각도를 복귀 목표로 잡는다
                    // 조준 정리를 **공격 발동보다 먼저** — 발동 뒤에 정리하면 정리 쪽 트윈/Kill이
                    // 막 시작한 연출 이동을 건드린다(피격자가 제자리에 남던 원인).
                    CardView t_target = this.currentTarget;
                    ClearTargetPreview();
                    OnAttack?.Invoke(this, t_target);
                    this.dragState = DragState.Idle;
                    return;
                }
                ClearTargetPreview();
                FocusWeapon(false);
                SetArmedVfx(false);   // 공격 없이 손 뗌
                ResetAimTilt();
                RestoreAllFades();
                this.cardAnim.MoveToSlot().Forget();
            }
            this.dragState = DragState.Idle;
            return;
        }

        // 탭 판정: 이 터치가 드래그로 확정되지 않았고(제스처 None) 손가락 이동이 데드존 이내.
        // 드래그 조준이 꺼져 있으면(BattleUxFlags.DragAimAttack=false) 데드존 조건을 뺀다 —
        // 드래그로 해석될 경로가 아예 없는데 데드존을 넘겼다고 무동작이 되면 "눌렀는데 반응 없음"이 된다
        // (같은 카드 재탭 해제·다른 카드로 선택 전환이 손 떨림만으로 죽던 원인).
        bool t_isTap = !BattleUxFlags.DragAimAttack
            || (this.activeGesture == Gesture.None
                && Vector2.Distance((Vector2)Input.mousePosition, this.touchStartScreenPos) < this.deadZoneRadius);

        // 튜토리얼: 탭이 이번 스텝의 조작이 아니면 무반응(무장·발사 둘 다 차단).
        if (t_isTap && !GestureAllowed(Gesture.None))
        {
            this.dragState     = DragState.Idle;
            this.activeGesture = Gesture.None;
            return;
        }

        // 적/비로컬 카드: 탭이면 "무장된 공격자 → 이 적" 공격(제스처3 2단계).
        if (this.boundCard.ownerIndex != TurnState.LocalOwnerIndex)
        {
            if (t_isTap) HandleEnemyTap();
            this.dragState     = DragState.Idle;
            this.activeGesture = Gesture.None;
            return;
        }

        // 로컬 카드 탭: 공격자 무장 토글(제스처3 1단계). ForcedAttacker 중에도 이 카드는 상단 가드를
        // 통과한 지정 공격자이므로 그대로 무장 허용(튜토리얼 탭 공격 스텝 진행).
        if (t_isTap)
        {
            ToggleSelectAttacker();
            this.dragState     = DragState.Idle;
            this.activeGesture = Gesture.None;
            return;
        }

        if (this.dragState != DragState.Idle)
        {
            CardView t_target  = this.currentTarget;
            bool     t_attacked = t_target != null;
            ResetAimTilt();   // 공격이든 취소든 조준 기울기는 여기서 끝(공격이면 Headbutt이 기준각을 다시 잡는다)

            // 조준 정리를 먼저, 공격 발동은 그 다음 — 순서가 반대면 정리 쪽 트윈/Kill이
            // 막 시작한 연출 이동을 죽인다(피격자가 중앙으로 안 오던 원인).
            ClearTargetPreview();
            if (t_attacked)
                OnAttack?.Invoke(this, t_target);

            if (!t_attacked)
            {
                RestoreAllFades();
                FocusWeapon(false);
                SetArmedVfx(false);   // 조준만 하다 놓음
                this.cardAnim.MoveToSlot().Forget();
            }
            // 공격으로 이어진 경우 무장 이펙트는 끄지 않는다 — 반동 끝에서 AttackSequence가 끈다.
        }
        else
        {
            FocusWeapon(false);
            SetArmedVfx(false);
            ResetAimTilt();
            this.cardAnim.MoveToSlot().Forget();
        }
        this.dragState     = DragState.Idle;
        this.activeGesture = Gesture.None;
    }

    async UniTask WaitLongPress(CancellationToken _ct)
    {
        try
        {
            await UniTask.Delay((int)(GameTiming.Battle.LongPress * 1000), cancellationToken: _ct);
            if (this.boundCard == null || !this.boundCard.isRevealed) return;

            // 누른 지점이 시너지 배지 위면 카드 정보 대신 시너지 설명을 띄운다.
            SynergyBadgeView t_badge = FindBadgeAt(this.touchStartScreenPos);
            if (t_badge != null && t_badge.Synergy != null)
            {
                UIPoolManager.Instance?.AddOrUpdateUI<SynergyExplainPopupUI>(new SynergyExplainData
                {
                    synergy        = t_badge.Synergy,
                    hasWorldAnchor = true,                       // 배지는 월드 스페이스라 RectTransform이 없다
                    worldAnchor    = t_badge.transform.position,
                    worldHalfWidth = BadgeHalfWidth(t_badge),
                    ownedCount     = OwnedCountOf(t_badge.Synergy),
                });
                this.longPressSynergyShown = true;
            }
            else
            {
                UIPoolManager.Instance?.AddOrUpdateUI<PooledCardElement>(
                    new PooledCardElementData { card = this.boundCard.data });
            }
            this.longPressFired = true;
            // Inspect 통지는 손을 뗀 순간(OnMouseUp)으로 이동 — 팝업이 뜨자마자 스텝이 넘어가지 않도록.
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>화면 좌표 아래에 있는 시너지 배지. 없으면 null.
    /// 배지에 콜라이더를 붙이면 카드 드래그 입력을 가로채므로, 스프라이트 bounds로만 판정한다.</summary>
    SynergyBadgeView FindBadgeAt(Vector2 _screenPos)
    {
        if (this.synergyBadgeRoot == null || Camera.main == null) return null;

        Vector3 t_world = Camera.main.ScreenToWorldPoint(
            new Vector3(_screenPos.x, _screenPos.y, -Camera.main.transform.position.z));

        foreach (Transform t_child in this.synergyBadgeRoot)
        {
            SynergyBadgeView t_badge = t_child.GetComponent<SynergyBadgeView>();
            if (t_badge == null || !t_child.gameObject.activeInHierarchy) continue;

            foreach (SpriteRenderer t_sr in t_child.GetComponentsInChildren<SpriteRenderer>())
            {
                if (!t_sr.enabled || !t_sr.gameObject.activeInHierarchy) continue;
                Bounds b = t_sr.bounds;
                if (t_world.x >= b.min.x && t_world.x <= b.max.x &&
                    t_world.y >= b.min.y && t_world.y <= b.max.y)
                    return t_badge;
            }
        }
        return null;
    }

    static float BadgeHalfWidth(SynergyBadgeView _badge)
    {
        float t_max = 0.15f;
        foreach (SpriteRenderer t_sr in _badge.GetComponentsInChildren<SpriteRenderer>())
            if (t_sr.enabled && t_sr.bounds.extents.x > t_max) t_max = t_sr.bounds.extents.x;
        return t_max;
    }

    /// <summary>이 필드의 확정 시너지 스냅샷에서 보유 장수. 없으면 -1(팝업이 ●/○ 마커를 생략).</summary>
    int OwnedCountOf(SynergyData _synergy)
    {
        if (this._lastBadgeState?.Active == null) return -1;
        foreach (var t_a in this._lastBadgeState.Active)
            if (t_a != null && t_a.Synergy == _synergy) return t_a.Count;
        return -1;
    }

    /// <summary>롱프레스 취소. **이미 떠 있던 설명 팝업도 같이 닫는다.**
    /// 예전엔 플래그만 지워서, 드래그로 취소된 경우 OnMouseUp의 t_wasLongPress가 false가 되어
    /// 팝업을 닫는 분기를 건너뛰고 화면에 그대로 남았다(잔류 버그).</summary>
    void CancelLongPress()
    {
        if (this.longPressFired)
        {
            // 카드 정보 / 시너지 설명 중 실제로 띄운 쪽을 닫는다.
            if (this.longPressSynergyShown) UIPoolManager.Instance?.HideUI<SynergyExplainPopupUI>();
            else                            UIPoolManager.Instance?.HideUI<PooledCardElement>();
        }

        this.longPressSynergyShown = false;
        this.longPressFired = false;
        this.longPressCts?.Cancel();
        this.longPressCts?.Dispose();
        this.longPressCts = null;
    }

    /// <summary>현재 튜토리얼 스텝이 Inspect이고 이 카드가 적(정보확인 대상)인가.
    /// 참이면 롱프레스 팝업을 드리프트로 닫지 않고 손 뗄 때까지 유지한다.</summary>
    bool IsTutorialInspectTarget()
    {
        if (!TutorialConfig.IsActive || this.boundCard == null) return false;
        if (this.boundCard.ownerIndex == TurnState.LocalOwnerIndex) return false;
        return TutorialConfig.TryPeekPlayerStep(out var t_step)
            && t_step.kind == TutorialScenarioData.StepKind.Inspect;
    }

    /// <summary>현재 드래그 좌표가 이 카드의 콜라이더 밖인가. 카메라/콜라이더 없으면 false(취소 안 함).</summary>
    bool PointerLeftSelf()
    {
        if (this.selfCollider == null || Camera.main == null) return false;
        Vector3 t_screen = new Vector3(this.currentDragScreenPos.x, this.currentDragScreenPos.y,
                                       -Camera.main.transform.position.z);
        return !this.selfCollider.OverlapPoint(Camera.main.ScreenToWorldPoint(t_screen));
    }


    void ClearTargetPreview()
    {
        if (this.currentTarget != null)
        {
            this.currentTarget.SetHighlight(false);
            this.currentTarget.HideAttackPreview();
            this.currentTarget.SetTargetFocus(false);
        }
        HideAttackPreview();
        this.currentTarget = null;
        ClearRejectFocus();   // 조준 해제 → 확대 원복 + 같은 카드에 다시 갖다 대면 거절 연출 재발화.
    }

    // ── 탭 공격(제스처3): 내 카드 탭으로 공격자 무장 → 적 카드 탭으로 발사(2단계) ──

    // 내 카드 탭: 공격자 무장 토글. 무장 시 자신+유효 적 강조(무기 조준 텔레그래프). 이미 무장이면 해제.
    void ToggleSelectAttacker()
    {
        if (s_selectedAttacker == this) { ClearAttackerSelection(); return; }
        ClearAttackerSelection();

        s_selectedAttacker = this;
        BeginTargeting();   // 무장 1회 = 안내 1회.
        SetHighlight(true);
        SetTargetFocus(true);   // 무장된 내 카드 살짝 확대 — 지금 누가 공격자인지 즉시 보이게.
        FocusWeapon(true);
        SetArmedVfx(true);      // 무장(탭) — 해제 또는 공격 반동 끝까지 유지

        var t_targets = GetValidEnemyViews();   // 지정 타깃이면 그 하나, 아니면 도발 있을 때 도발 카드만.
        FadeAll(ForcedDimAlpha);
        FadeCards(1f, this);
        FadeCards(1f, t_targets.ToArray());
        foreach (var t_cv in t_targets)
            if (t_cv.boundCard.HasKeyword(CardKeyword.Taunt))
                t_cv.PlayKeywordGlow(CardKeyword.Taunt).Forget();

        // 유효 타겟 각각에 공격 HP 프리뷰 표시(맞으면 남는 체력/치사 점멸).
        s_previewTargets = t_targets;
        foreach (var t_cv in t_targets)
        {
            if (t_cv.boundCard == null || this.boundCard == null) continue;
            AttackPreview t_p = AttackPreview.Compute(this.boundCard, t_cv.boundCard);
            t_cv.ShowAttackPreview(t_p.attackDamage, t_p.defenderWouldDie);
        }
    }

    // 무장 해제: 강조/확대/무기/페이드 원복. _instant=공격 발동 직전(뒤이어 AttackSequence가 transform 장악).
    static void ClearAttackerSelection(bool _instant = false)
    {
        if (s_selectedAttacker == null) return;
        CardView t_prev = s_selectedAttacker;
        s_selectedAttacker = null;

        if (s_previewTargets != null)
        {
            foreach (var t_cv in s_previewTargets)
                if (t_cv != null) t_cv.HideAttackPreview();
            s_previewTargets = null;
        }

        t_prev.SetHighlight(false);
        t_prev.SetTargetFocus(false, _instant);
        t_prev.FocusWeapon(false);
        t_prev.SetArmedVfx(false);   // 무장 해제 = 이펙트도 끝(공격으로 이어지는 경우는 HandleEnemyTap이 다시 켠다)
        RestoreAllFades();
    }

    // 적 카드 탭: 무장된 공격자가 있고 이 적이 유효 타깃이면 공격 발동(유효 필터는 공격자 기준).
    void HandleEnemyTap()
    {
        if (s_selectedAttacker == null) return;                       // 미무장 — 무동작(정보는 롱프레스).
        if (s_selectedAttacker.boundCard == null) { ClearAttackerSelection(); return; }

        var t_valid = s_selectedAttacker.GetValidEnemyViews();
        if (!t_valid.Contains(this))
        {
            RejectAsTarget(t_valid);   // 침묵 무시 금지 — "저기 말고 여기"를 흔들기+펄스+문구로.
            return;
        }

        CardView t_attacker = s_selectedAttacker;
        ClearAttackerSelection(_instant: true);   // 확대 즉시 원복 — 공격 연출의 DOKill에 트윈이 잘려 커진 채 굳는 것 방지.
        // 위 해제가 무장 이펙트도 끈다. 탭 공격은 여기서 바로 공격이 이어지므로 다시 켜서
        // 반동이 끝나는 지점(AttackSequence)까지 유지한다 — 드래그 공격 경로와 수명을 맞춘다.
        t_attacker.SetArmedVfx(true);
        t_attacker.ResetAimTilt();
        OnAttack?.Invoke(t_attacker, this);
    }

    // ── 무효 타깃 거절 피드백 ────────────────────────────────────────────
    // 못 치는 적을 탭했을 때 무반응이면 "버그"로 읽힌다. 거절 대상은 흔들고, 쳐야 할 카드는 펄스로 끌어당긴다.
    // 도발로 막힌 경우에 한해 이유 문구까지(무장 1회당 1번 — 연타 배너 스팸 방지).
    // _keepFocus: 조준이 그 카드에 머무는 동안 확대 유지(드래그). 탭은 머무는 개념이 없어 false.
    void RejectAsTarget(List<CardView> _validTargets, bool _keepFocus = false)
    {
        PlayRejectShake(_keepFocus);
        if (_validTargets != null)
            foreach (CardView t_cv in _validTargets)
                if (t_cv != null) t_cv.PlayAttentionPulse();

        if (s_tauntNoticeShown || _validTargets == null || _validTargets.Count == 0) return;

        // 지정 타깃(튜토리얼)로 걸러진 경우는 스크립트가 따로 안내한다 → 도발로 막힌 경우만 문구.
        CardView t_taunt = _validTargets.Find(cv => cv?.boundCard != null && cv.boundCard.HasKeyword(CardKeyword.Taunt));
        if (t_taunt == null) return;

        s_tauntNoticeShown = true;
        ShowTauntBlockedNotice(t_taunt.boundCard);
    }

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
        if (this.boundCard != null && this.boundCard.HasKeyword(CardKeyword.Taunt))
            PlayKeywordGlow(CardKeyword.Taunt).Forget();
    }

    /// <summary>도발 차단 안내 배너. 초상화는 도발 아이콘(있으면), 없으면 도발 카드 초상화.
    /// BattleUxFlags.EffectNotifyBanner로 블라인드 중 — 우측 슬라이드 배너는 가독성·학습성이 낮다는 판단.
    /// 배너가 없어도 거절 피드백은 남는다: 거절 대상 흔들기 + 도발 카드 펄스/글로우(RejectAsTarget).</summary>
    static void ShowTauntBlockedNotice(CardInstance _tauntCard)
    {
        if (!BattleUxFlags.EffectNotifyBanner) return;
        if (_tauntCard?.data == null) return;

        Sprite t_icon = DataLibrary.instance?.keywordIconConfig?.GetIcon(CardKeyword.Taunt);
        UIPoolManager.Instance?.AddOrUpdateUI<EffectNotifyUI>(new EffectNotifyData
        {
            portrait       = t_icon != null ? t_icon : _tauntCard.data.fullImage,
            preserveAspect = t_icon != null,
            cardName       = _tauntCard.data.displayName,
            effectLabel    = TAUNT_BLOCKED_TEXT,
        });
    }

    /// <summary>드래그 시작 시 유효 타깃 강조 페이드 — 밝기는 곧 "유효 타깃"의 시각화다.
    /// 유효 목록은 GetValidEnemyViews 하나가 정하므로(지정 타깃/도발 필터 포함) 여기엔 별도 분기가 없다.
    /// 지정 타깃 중엔 유효 타깃이 그 하나뿐 → 결과가 RestoreAllFades의 강제 상태와 같아진다.</summary>
    void ApplyDragTargetFade(List<CardView> _validTargets)
    {
        FadeAll(ForcedDimAlpha);
        FadeCards(1f, this);
        FadeCards(1f, _validTargets.ToArray());
    }

    // 타겟 하나에 집중: 나머지(다른 유효 타겟 포함) 약간 fade, 자신+포커스 타겟만 밝게.
    // 기존 fade 파이프 재사용 → 드래그 종료 시 RestoreAllFades가 일괄 복원(튜토리얼 dim 포함). 새 fade 상태 없음.
    // 암전 alpha는 ForcedDimAlpha 공유 — 튜토리얼이 이 값을 덮어써도 드래그 중 밝기가 튀지 않게.
    void ApplyFocusFade(CardView _target)
    {
        FadeAll(ForcedDimAlpha);
        FadeCards(1f, this);
        if (_target != null) FadeCards(1f, _target);
    }

    /// <summary>"유효 타깃 = 밝게 = 프리뷰 표시 = 공격 가능"의 단일 진실원.
    /// 필터 우선순위 ① 지정 타깃(ForcedTarget) ② 도발. ①이 우선 — 튜토리얼 스크립트가 규칙이다.
    /// ForcedTarget 존재 자체가 조건(IsActive 등 별도 조건 금지) — RestoreAllFades의 암전 기준과 동일해야 개념이 안 쪼개진다.</summary>
    List<CardView> GetValidEnemyViews()
    {
        var t_enemies = GetEnemyViews();

        // ① 지정 타깃이 이 공격자의 적 목록에 있으면 그 하나만 유효(도발보다 우선).
        if (TurnState.ForcedTarget != null)
        {
            CardView t_forced = GetView(TurnState.ForcedTarget);
            if (t_forced != null && t_enemies.Contains(t_forced))
                return new List<CardView> { t_forced };
        }

        // ② 도발이 있으면 도발 카드만.
        var t_taunt = t_enemies.FindAll(cv => cv.boundCard.HasKeyword(CardKeyword.Taunt));
        return t_taunt.Count > 0 ? t_taunt : t_enemies;
    }

    /// <summary>좌/중앙/우 조준 방향이 가리키는 적 1장. 목록은 x좌표로 정렬해 판단(기존 규칙 그대로).</summary>
    CardView PickByAimDirection(List<CardView> _candidates, Vector2 _aimDir)
    {
        if (_candidates == null || _candidates.Count == 0) return null;
        _candidates.Sort((a, b) => a.transform.position.x.CompareTo(b.transform.position.x));

        if (_aimDir.x >  this.dirThreshold) return _candidates[_candidates.Count - 1];
        if (_aimDir.x < -this.dirThreshold) return _candidates[0];
        return _candidates[_candidates.Count / 2];
    }

    /// <summary>필터 전 적 카드 전체. 거절 피드백은 "필터로 빠진 적"을 알아야 하므로 이 목록이 기준.</summary>
    List<CardView> GetEnemyViews()
    {
        var t_enemies = new List<CardView>();
        foreach (CardView t_cv in allViews)
        {
            if (t_cv == this || t_cv.boundCard == null) continue;
            if (t_cv.boundCard.ownerIndex == this.boundCard?.ownerIndex) continue;
            t_enemies.Add(t_cv);
        }
        return t_enemies;
    }

    /// <summary>도발 때문에 타깃이 좁혀졌나(지정 타깃 필터는 제외). 지정 타깃은 튜토리얼 스크립트 소관이라
    /// 기존 스냅 동작을 유지하고, 도발일 때만 "그쪽 아님"을 강하게 알린다.</summary>
    bool IsTauntFiltered(List<CardView> _valid, List<CardView> _all)
        => TurnState.ForcedTarget == null && _valid.Count < _all.Count;

    /// <summary>조준 시작(드래그 진입/탭 무장) 공통 리셋 — 이번 조준에서 거절 이력/안내 1회 카운트를 새로 시작.</summary>
    void BeginTargeting()
    {
        ClearRejectFocus();
        s_tauntNoticeShown = false;
    }

    /// <summary>같은 대상에 대해 프레임마다 거절 연출이 반복되지 않게 1회만 발화.
    /// 못 치는 카드도 조준 중엔 확대 포커스 유지 — 조준이 먹었다는 사실 자체는 보여준다.</summary>
    void RejectOnce(CardView _rejected, List<CardView> _valid)
    {
        if (this.rejectedTarget == _rejected) return;
        ClearRejectFocus();
        this.rejectedTarget = _rejected;
        _rejected.RejectAsTarget(_valid, _keepFocus: true);
    }

    /// <summary>거절 포커스 해제(조준이 벗어남/조준 종료). 확대만 원복, 다른 상태 없음.</summary>
    void ClearRejectFocus()
    {
        if (this.rejectedTarget != null) this.rejectedTarget.SetTargetFocus(false);
        this.rejectedTarget = null;
    }

    void UpdateTarget(Vector2 _aimDir)
    {
        var t_valid = GetValidEnemyViews();
        var t_all   = GetEnemyViews();

        // 도발로 좁혀진 상태면 조준은 전체 적 기준으로 판정한다 — 못 치는 쪽을 겨누면 스냅 대신 거절.
        // (지정 타깃/필터 없음이면 종전대로 유효 목록에서 스냅.)
        bool t_strict = IsTauntFiltered(t_valid, t_all);
        CardView t_aimed = PickByAimDirection(t_strict ? t_all : t_valid, _aimDir);
        CardView t_best  = (t_aimed != null && t_valid.Contains(t_aimed)) ? t_aimed : null;

        if (t_strict && t_aimed != null && t_best == null) RejectOnce(t_aimed, t_valid);
        else if (t_best != null)                           ClearRejectFocus();

        if (this.currentTarget == t_best) return;

        if (this.currentTarget != null)
        {
            this.currentTarget.SetHighlight(false);
            this.currentTarget.HideAttackPreview();
            this.currentTarget.SetTargetFocus(false);
            HideAttackPreview();
        }

        this.currentTarget = t_best;

        if (this.currentTarget != null)
        {
            this.currentTarget.SetHighlight(true);
            this.currentTarget.SetTargetFocus(true);
            ShowPreviewForTarget(this.currentTarget);
            ApplyFocusFade(this.currentTarget);
        }
        else
        {
            ApplyDragTargetFade(GetValidEnemyViews());   // 타겟 없음 → 드래그 기본(유효타겟 다 밝게)로 복귀.
        }
    }

    void ShowPreviewForTarget(CardView _target)
    {
        CardInstance t_atk = this.boundCard;
        CardInstance t_def = _target.BoundCard;
        if (t_atk == null || t_def == null) return;

        // 계산은 AttackPreview가 단독 소유(실제 전투와 갈라지지 않도록). 여기선 표시만.
        AttackPreview t_p = AttackPreview.Compute(t_atk, t_def);

        _target.ShowAttackPreview(t_p.attackDamage, t_p.defenderWouldDie);   // 직격(공격): 비늘 감소 반영(기본 true)
        if (!t_p.hasCounter) return;
        // 반격 맥락: 비늘 감소 없음(false). 실제 반격 TakeDamage(false)와 사망 프리뷰/HP표시 일치.
        ShowAttackPreview(t_p.counterDamage, t_p.attackerWouldDie, false);
    }
    #endregion

    #region Visual State
    public void Render(CardInstance _card, SynergyState _synergy = null)
    {
        // 슬롯 점유 카드가 바뀌면(사망→새 카드 스폰 등) 이전 피격 연출 잔여 제거 → 새 카드에 이월 방지.
        if (this.boundCard != _card)
        {
            this.cardAnim.ResetHitEffect();
            HideArmedVfx();   // 이전 카드의 무장 이펙트가 새 카드에 남지 않게
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
            RefreshKeywordIcons(null);   // 빈 슬롯: 아이콘 없음.
            RefreshKeywordFrames(null);
            RefreshSynergyBadges(null);
            return;
        }

        bool t_isFaceDown = !_card.isRevealed;

        if (t_isFaceDown) SetHpDisplay("?", "");
        else SetHpDisplay(_card.hp.ToString(), _card.bonusHp > 0 ? $"+{_card.bonusHp}" : "");
        this.nameText.text = t_isFaceDown ? "???" : _card.data.displayName;

        // 뒷면이면 덱 뒷면 그림으로 갈아 끼운다 — 앞면 일러스트가 남아 있으면 뒷면 그림 밖으로 비친다.
        if (this.illustration != null && !t_isFaceDown && _card.data.battleImage != null)
            this.illustration.sprite = _card.data.battleImage;

        SetFaceDownLook(t_isFaceDown);

        SetupWeapon(_card.data);
        RefreshKeywordIcons(_card);   // 뒷면 은닉·표시 대상 판정은 RefreshKeywordIcons 안에서.
        RefreshKeywordFrames(_card);
        RefreshSynergyBadges(_synergy);
    }

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

    void SetupWeapon(CardData _data)
    {
        if (this.weaponInstance != null)
        {
            // 무기 자식 스프라이트가 FadeView tween 대상으로 잡혀있을 수 있음.
            // 루트(SetLink 대상)는 살아있어 kill이 안 걸리므로 파괴 전 직접 DOKill.
            foreach (SpriteRenderer t_sr in this.weaponInstance.GetComponentsInChildren<SpriteRenderer>(true))
                t_sr.DOKill();
            this.weaponInstance.transform.SetParent(null);
            Destroy(this.weaponInstance);
            this.weaponInstance = null;
            this.weaponAnimator = null;
        }
        if (_data == null || _data.weaponPrefab == null) return;

        Transform t_anchor = this.weaponAnchor != null ? this.weaponAnchor : transform;
        this.weaponInstance = Instantiate(_data.weaponPrefab, t_anchor);
        this.weaponInstance.transform.localPosition = Vector3.zero;
        if (this.boundCard?.ownerIndex != TurnState.LocalOwnerIndex)
            this.weaponInstance.transform.localRotation = Quaternion.Euler(_data.enemyWeaponEuler);
        this.weaponBaseRot = this.weaponInstance.transform.localRotation;
        this.weaponAnimator = this.weaponInstance.GetComponent<Animator>();
        this.weaponInstance.SetActive(false);
    }

    // 무장 이펙트는 여기 얹지 않는다 — ResolveHits가 접촉 직후 FocusWeapon(false)를 부르기 때문에
    // 같이 묶으면 반동이 끝나기도 전에 이펙트가 꺼진다. 무장/해제 시점에서 SetArmedVfx를 직접 부른다.
    public void FocusWeapon(bool _active)
    {
        if (this.weaponInstance == null) return;
        if (_active)
        {
            this.weaponInstance.SetActive(true);
            if (this.weaponAnimator != null)
            {
                this.weaponAnimator.enabled = true;
                this.weaponAnimator.Rebind();
            }
        }
        else
        {
            if (this.weaponAnimator != null)
            {
                this.weaponAnimator.Rebind();
                this.weaponAnimator.enabled = false;
            }
            this.weaponInstance.SetActive(false);
        }
    }

    /// <summary>무장(포커스) 이펙트 토글. 카드 자식으로 붙어 공격 이동/기울기를 그대로 따라간다.
    /// 켜지는 시점 = 무장(FocusWeapon(true)), 꺼지는 시점 = 적에 닿는 순간(AttackSequence가 false로 호출).
    /// 중복 호출은 무시한다 — 드래그 중 여러 경로에서 불린다.</summary>
    public void SetArmedVfx(bool _active)
    {
        if (_active) ShowArmedVfx();
        else         HideArmedVfx();
    }

    /// <summary>무장 이펙트 프리팹을 갈아끼운다(null이면 카드의 AttackEffect가 정의한 Armed 항목 사용).
    /// AttackAnimTester가 후보를 넘겨보며 고를 때 쓴다 — 카드 에셋을 건드리지 않는 런타임 오버라이드.
    /// 켜져 있는 상태에서 바꾸면 즉시 교체된다.</summary>
    public void SetArmedVfxPrefab(GameObject _prefab)
    {
        if (this.armedVfxPrefabOverride == _prefab) return;
        bool t_wasOn = this.armedVfx.Count > 0;
        HideArmedVfx();
        this.armedVfxPrefabOverride = _prefab;
        if (t_wasOn) ShowArmedVfx();
    }

    void ShowArmedVfx()
    {
        if (this.armedVfx.Count > 0) return;                                // 이미 켜져 있음
        if (this.boundCard == null || !this.boundCard.isRevealed) return;   // 뒷면/빈 슬롯은 노출 금지

        // 적 카드는 위아래가 뒤집힌 배치라 오프셋/회전도 뒤집는다(AttackEffect.particles와 같은 flip 규약).
        bool t_flip    = IsEnemySide;
        int  t_layerId = VfxSortingLayerId;

        if (this.armedVfxPrefabOverride != null)
        {
            // 테스터 오버라이드: 배치값 없이 프리팹만 교체해 본다.
            Spawn(this.armedVfxPrefabOverride, Vector3.zero, Vector3.zero);
            return;
        }

        AttackEffect t_fx = this.boundCard.data?.attackEffect;
        if (t_fx == null) return;
        foreach (ParticleEntry t_entry in t_fx.ArmedEntries())
            Spawn(t_entry.prefab, t_entry.localOffset, t_entry.initialRotation);

        void Spawn(GameObject _prefab, Vector3 _offset, Vector3 _euler)
        {
            GameObject t_go = BattleVfx.SpawnAttached(_prefab, transform, _offset, _euler, t_flip, out string t_id);
            if (t_go == null) return;
            BattleVfx.ApplySorting(t_go, t_layerId, this.armedVfxSortingOrder);
            this.armedVfx.Add((t_id, t_go));
        }
    }

    void HideArmedVfx()
    {
        // 부모가 아직 나일 때만 반납 — 자기반납형(PooledParticle) 프리팹과 이중 반납 충돌 방지.
        foreach ((string t_id, GameObject t_go) in this.armedVfx)
            BattleVfx.Release(t_id, t_go, transform);
        this.armedVfx.Clear();
    }

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
            t_c.a = 1f;
            this.hpText.color = t_c;
            SetHpDisplay(this.boundCard.hp.ToString(), this.boundCard.bonusHp > 0 ? $"+{this.boundCard.bonusHp}" : "");
            this.hpFallbackPreview = false;
        }

        // 플래그로 꺼져 있어도 호출 — 과거 상태/플래그 전환 직후의 잔존 오버레이 정리.
        this.cardAnim.HideDeathPreview();
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

    public async UniTask PlayPassiveGlow()
    {
        if (this.passiveGlowSystem == null) return;
        this.passiveGlowSystem.Play();
        float t_dur = this.passiveGlowSystem.main.duration;
        await UniTask.Delay((int)(t_dur * 1000), cancellationToken: this.GetCancellationTokenOnDestroy()).SuppressCancellationThrow();
    }

    // 아이콘 줄에는 캐릭터 고유 특성만 그린다. 일회용/디버프(무적·추가체력·전투 중 걸린 표식)는
    // 아예 표시하지 않는다 — 무엇을 띄울지 판정은 CardVisualRules 단독(아웃게임과 같은 호출).
    /// <summary>키워드 아이콘이 실제로 붙는 앵커. 시너지 자리를 쓰면 synergyBadgeRoot(좌측 세로열),
    /// 아니면 종전 keywordIconRoot(우하단 가로줄). 앵커 미배선이면 기존 루트로 폴백한다.</summary>
    Transform KeywordAnchor => this.keywordIconsUseSynergySlot && this.synergyBadgeRoot != null
        ? this.synergyBadgeRoot : this.keywordIconRoot;

    void RefreshKeywordIcons(CardInstance _card)
    {
        Transform t_root = KeywordAnchor;
        if (t_root == null || this.keywordIconPrefab == null || this.keywordIconConfig == null) return;

        foreach (Transform t_child in t_root)
        {
            // 아이콘 스프라이트가 FadeView tween 대상일 수 있음. 파괴 전 DOKill (루트 SetLink는 안 걸림).
            foreach (SpriteRenderer t_sr in t_child.GetComponentsInChildren<SpriteRenderer>(true))
                t_sr.DOKill();
            Destroy(t_child.gameObject);
        }

        this.iconMap.Clear();

        // 뒷면/빈 슬롯이면 아무것도 노출하지 않는다(정보 은닉).
        if (_card == null || !_card.isRevealed) return;

        // 여기 남는 건 월드좌표 배치와 스프라이트 주입뿐. None/아이콘 미등록은 규칙 쪽에서 걸러져 빈 리스트가 온다.
        List<CardVisualRules.KeywordIcon> t_icons =
            CardVisualRules.CollectKeywordIcons(CardVisualRules.IconKeywords(_card), this.keywordIconConfig);

        // 배치 두 가지. 시너지 자리: 배지와 동일한 세로열 좌표(같은 필드를 써야 "그 자리 그대로"가 성립).
        // 기존 자리: keywordIconRoot를 카드 오른쪽 아래 코너에 두고 원점에서 왼쪽으로 가로 정렬.
        for (int t_i = 0; t_i < t_icons.Count; t_i++)
        {
            GameObject t_obj = Instantiate(this.keywordIconPrefab, t_root);
            t_obj.transform.localPosition = this.keywordIconsUseSynergySlot && this.synergyBadgeRoot != null
                ? new Vector3(this.synergyBadgeXPos, this.synergyBadgeYStart + this.synergyBadgeYStep * t_i, 0f)
                : new Vector3(-t_i * this.iconSpacing, 0f, 0f);
            // prefab = 배경(루트 SpriteRenderer) + 아이콘(자식 SpriteRenderer). 배경 유지, 자식에만 키워드 스프라이트 주입.
            SpriteRenderer t_iconSr = t_obj.transform.childCount > 0
                ? t_obj.transform.GetChild(0).GetComponent<SpriteRenderer>()
                : t_obj.GetComponent<SpriteRenderer>();
            if (t_iconSr != null) t_iconSr.sprite = t_icons[t_i].Icon;
            this.iconMap[t_icons[t_i].Keyword] = t_obj;
        }
    }

    // 프레임 키워드 장식. 기준은 TraitKeywords(아이콘 줄은 여기서 IconRowExcluded만 더 빼는 IconKeywords) —
    // 즉 표식은 프레임엔 뜨고 아이콘 줄엔 안 뜬다. 그 차이의 유일한 선언 지점은 CardVisualRules.IconRowExcluded다.
    // 빈 슬롯/뒷면은 전부 끈다(아이콘 줄과 동일한 정보 은닉).
    void RefreshKeywordFrames(CardInstance _card)
    {
        if (this.keywordFrames == null) return;

        CardKeyword t_keywords = _card != null && _card.isRevealed
            ? CardVisualRules.TraitKeywords(_card) : CardKeyword.None;

        foreach (KeywordFrame t_frame in this.keywordFrames)
        {
            if (t_frame.overlay == null) continue;
            // None 배선은 항상 꺼짐 — HasFlag(None)은 늘 true라 그대로 두면 모든 카드에서 켜진다.
            bool t_on = t_frame.keyword != CardKeyword.None && (t_keywords & t_frame.keyword) != 0;
            t_frame.overlay.SetActive(t_on);
        }
    }

    // 카드의 synergies 배열(있는 것만, 중복 제외)을 색+텍스트 배지로 세로 정렬 표시(최대 synergyMaxBadges개).
    // 선택·정렬 규칙(활성 우선 → requiredCount 내림차순)은 CardVisualRules 소유 — 아웃게임 타일과 순서가 갈라지지 않게.
    // 활성/티어 판정은 확정 SynergyState.Active 참조 조회(재계산·집계 금지).
    // _synergy는 이 카드가 속한 BattleField.Synergy(BattleFieldView가 Render로 주입). null이면 전부 비활성 취급.
    CardInstance _lastBadgeCard;
    SynergyState _lastBadgeState;

    void RefreshSynergyBadges(SynergyState _synergy)
    {
        // 그 자리를 키워드 아이콘이 쓰는 모드면 배지를 아예 만들지 않는다(겹침 방지).
        // 배지가 존재하지 않으므로 FindBadgeAt은 null → 롱프레스는 카드 정보 팝업으로, PopSynergyBadge는 no-op.
        if (this.keywordIconsUseSynergySlot && this.synergyBadgeRoot != null) return;
        if (this.synergyBadgeRoot == null || this.synergyBadgePrefab == null) return;

        // 시너지는 덱 확정이라 전투 중 불변. 같은 카드+같은 SynergyState면 재생성 스킵 →
        // 매 Render(턴 시작 Refresh)마다 배지가 재-Set되어 pop이 반복되는 문제 방지.
        // 배지가 이미 존재할 때만 스킵(없으면 재생성 필요). 첫 등장/리바인드 시에만 rebuild+pop.
        if (this.boundCard == this._lastBadgeCard && _synergy == this._lastBadgeState
            && this.synergyBadgeRoot.childCount > 0)
            return;
        this._lastBadgeCard  = this.boundCard;
        this._lastBadgeState = _synergy;

        // 기존 배지 정리. 배경 SpriteRenderer/라벨 TMP_Text가 CardAnimator FadeView tween 대상일 수 있어
        // 파괴 전 직접 DOKill(SetLink는 CardView GO 기준이라 자식 단독 파괴 시 안 걸림). 키워드 아이콘과 동일 규약.
        foreach (Transform t_child in this.synergyBadgeRoot)
        {
            foreach (SpriteRenderer t_sr in t_child.GetComponentsInChildren<SpriteRenderer>(true))
                t_sr.DOKill();
            foreach (TMP_Text t_tx in t_child.GetComponentsInChildren<TMP_Text>(true))
                t_tx.DOKill();
            Destroy(t_child.gameObject);
        }

        // 튜토리얼: 시너지 개념 미도입 구간은 배지 숨김. SynergyEnabled(3편~)이면 정상 표시.
        if (TutorialConfig.IsActive && !TutorialConfig.SynergyEnabled) return;

        // 빈 슬롯·뒷면 카드는 배지 없음(뒷면 적의 종족/직업 정보 노출 방지).
        if (this.boundCard == null || this.boundCard.data == null || !this.boundCard.isRevealed) return;

        // 표시 대상·순서(중복 제외 → 활성 우선 → requiredCount 내림차순 → 상한 적용)는 CardVisualRules 단독.
        // 여기 남는 건 세로 배치와 배지 Set뿐. 활성 판정도 같은 규칙 헬퍼를 재사용해 정렬과 아이콘이 어긋나지 않게 한다.
        List<SynergyData> t_tags = CardVisualRules.CollectSynergyBadges(this.boundCard.data.synergies, _synergy, this.synergyMaxBadges);

        for (int t_i = 0; t_i < t_tags.Count; t_i++)
        {
            SynergyBadgeView t_badge = Instantiate(this.synergyBadgePrefab, this.synergyBadgeRoot);
            t_badge.transform.localPosition = new Vector3(this.synergyBadgeXPos, this.synergyBadgeYStart + this.synergyBadgeYStep * t_i, 0f);
            t_badge.Set(t_tags[t_i], CardVisualRules.IsSynergyActive(_synergy, t_tags[t_i]));
        }
    }

    public static CardView GetView(CardInstance _card)
    {
        foreach (CardView t_cv in allViews)
            if (t_cv.boundCard == _card) return t_cv;
        return null;
    }

    // 시너지 효과가 실제 발동한 순간, 이 카드의 해당 시너지 배지를 pop시킨다(순수 연출, 게임상태/RNG 무관).
    // synergyBadgeRoot 자식에는 활성 배지만 존재하므로 Synergy 참조 일치 배지를 찾아 PlayPop. null/미발견이면 no-op.
    public void PopSynergyBadge(SynergyData _synergy)
    {
        if (this.synergyBadgeRoot == null || _synergy == null) return;
        foreach (Transform t_child in this.synergyBadgeRoot)
        {
            SynergyBadgeView t_badge = t_child.GetComponent<SynergyBadgeView>();
            if (t_badge != null && t_badge.Synergy == _synergy)
            {
                t_badge.PlayPop();
                return;
            }
        }
    }

    public async UniTask PlayKeywordGlow(CardKeyword _kw)
    {
        if (this.keywordGlowPrefab == null || _kw == CardKeyword.None) return;

        var t_spawned = new List<GameObject>();
        foreach (CardKeyword t_flag in System.Enum.GetValues(typeof(CardKeyword)))
        {
            if (t_flag == CardKeyword.None) continue;
            if (!_kw.HasFlag(t_flag)) continue;
            if (!this.iconMap.TryGetValue(t_flag, out GameObject t_icon)) continue;

            Color t_startCol = Color.white;
            Color t_endCol   = Color.clear;
            this.keywordIconConfig?.TryGetGlowColors(t_flag, out t_startCol, out t_endCol);

            GameObject t_glow = Instantiate(this.keywordGlowPrefab, t_icon.transform.position, Quaternion.identity);
            var t_ps = t_glow.GetComponent<ParticleSystem>();
            if (t_ps != null)
            {
                var t_col  = t_ps.colorOverLifetime;
                t_col.enabled = true;
                var t_grad = new Gradient();
                t_grad.SetKeys(
                    new GradientColorKey[] { new GradientColorKey(t_startCol, 0f), new GradientColorKey(t_endCol, 1f) },
                    new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) }
                );
                t_col.color = new ParticleSystem.MinMaxGradient(t_grad);
                t_ps.Play();
            }
            t_spawned.Add(t_glow);
        }

        if (t_spawned.Count == 0) return;

        try
        {
            await UniTask.Delay((int)(GameTiming.Battle.KeywordGlowHold * 1000), cancellationToken: this.GetCancellationTokenOnDestroy());
        }
        catch (OperationCanceledException) { }

        foreach (GameObject t_g in t_spawned)
            if (t_g != null) Destroy(t_g);
    }
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
        if (this.boundCard != null)
            SetHpDisplay(this.boundCard.hp.ToString(), this.boundCard.bonusHp > 0 ? $"+{this.boundCard.bonusHp}" : "");
        // 피격 파티클은 라이브러리 소유(미배선이면 무동작). 붐/숫자는 프리팹의 HitEffectView가 계속 담당 —
        // 그쪽은 카드에 상주하며 상태(시퀀스/숫자)를 가지므로 1회성 파티클과 축이 다르다.
        Vector3 t_awayDir = _hitFrom != null ? transform.position - _hitFrom.transform.position : default;
        t_awayDir.z = 0f;   // 화면 평면 방향만 — 시네마 중 z가 벌어져 있으면 먼지가 카메라 쪽으로 튄다
        BattleVfx.PlayAttached(BattleVfxId.Hit, transform, IsEnemySide, VfxSortingLayerId, t_awayDir);
        await this.cardAnim.PlayHitAnim(_d, _damage);
    }
    public UniTask PlayDeathAnim(float _d = 0.4f)  => this.cardAnim.PlayDeathAnim(_d);

    /// <summary>회복 연출(회복 파티클 + "+N") + HP 표기 갱신. CardInstance.Heal/ReviveAtHalf가 실제 회복량으로 호출.
    /// 회복이면 경로(힐러/돌보미/청소부/유산/부활) 불문 여기 하나로 수렴한다.</summary>
    public void PlayHealEffect(int _amount)
    {
        if (this.boundCard != null)
            SetHpDisplay(this.boundCard.hp.ToString(), this.boundCard.bonusHp > 0 ? $"+{this.boundCard.bonusHp}" : "");
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
    public UniTask PlayDealAnim(Vector3 _from, Vector3 _mid, Vector3 _dest, float _duration = 0.6f)
        => UsesOrbAppear
            ? CardAppearVfx.PlayOrbCurve(this, _mid, _dest, _duration)
            : this.cardAnim.PlayDealAnim(_from, _mid, _dest, _duration);

    /// <summary>배치 연출을 **중앙에서 끊어** 두 토막으로 쓰는 경로(등장 컷씬용).
    /// 앞 토막은 화면 밖 → 중앙까지만 가고 거기 멈춘다. 컷씬이 끝나면 PlayDealToSlot이 이어받는다.
    ///
    /// 구체 등장(EnergyOrbCurve)은 쪼갤 지점이 없다 — 카드가 중앙에 서는 구간 자체가 없고 구체가 날아온다.
    /// 그래서 앞 토막은 무동작이고 뒤 토막이 통째로 구체 연출을 돌린다(컷씬 뒤에 등장하는 순서는 같다).</summary>
    public UniTask PlayDealToMid(Vector3 _from, Vector3 _mid, Vector3 _dest, float _duration = 0.6f)
        => UsesOrbAppear ? UniTask.CompletedTask : this.cardAnim.PlayDealToMid(_from, _mid, _dest, _duration);

    public UniTask PlayDealToSlot(Vector3 _mid, Vector3 _dest, float _duration = 0.6f)
        => UsesOrbAppear
            ? CardAppearVfx.PlayOrbCurve(this, _mid, _dest, _duration)
            : this.cardAnim.PlayDealToSlot(_dest, _duration);

    bool UsesOrbAppear => this.boundCard?.data != null
                       && this.boundCard.data.cinemaAttackStyle == CinemaAttackStyle.EnergyOrbDash;

    public UniTask RestoreAfterAttack() => this.cardAnim.MoveToSlot();
    public void InitializeAnimator()    => this.cardAnim.Initialize();

    static bool IsAliveView(CardView _cv)
        => _cv != null && _cv.BoundCard != null && _cv.BoundCard.IsAlive;

    public static void FadeAll(float _alpha)
    {
        foreach (CardView t_cv in allViews)
        {
            if (!IsAliveView(t_cv)) continue;
            t_cv.FadeView(_alpha, GameTiming.Battle.FadeViewDuration);
        }
    }

    public static void FadeTeam(float _alpha, int _ownerIndex)
    {
        foreach (CardView t_cv in allViews)
        {
            if (!IsAliveView(t_cv)) continue;
            if (t_cv.boundCard.ownerIndex == _ownerIndex)
                t_cv.FadeView(_alpha, GameTiming.Battle.FadeViewDuration);
        }
    }

    public static void FadeCards(float _alpha, params CardView[] _cards)
    {
        foreach (CardView t_cv in _cards)
        {
            if (!IsAliveView(t_cv)) continue;
            t_cv.FadeView(_alpha, GameTiming.Battle.FadeViewDuration);
        }
    }

    public static void RestoreAllFades()
    {
        FadeAll(1f);

        // 공격자 지정: 로컬 팀을 암전하고 공격자만 밝게.
        if (TurnState.ForcedAttacker != null)
        {
            FadeTeam(ForcedDimAlpha, TurnState.LocalOwnerIndex);
            CardView t_forced = GetView(TurnState.ForcedAttacker);
            if (t_forced != null) FadeCards(1f, t_forced);
        }

        // 타깃 지정(튜토리얼): 적 팀을 암전하고 지정 타깃만 밝게 — "이 적을 쳐라" 집중 유도.
        if (TurnState.ForcedTarget != null)
        {
            FadeTeam(ForcedDimAlpha, 1 - TurnState.LocalOwnerIndex);
            CardView t_target = GetView(TurnState.ForcedTarget);
            if (t_target != null) FadeCards(1f, t_target);
        }
    }

    public static void Cleanup()
    {
        OnAttack      = null;
        s_anyDragging = false;
        s_selectedAttacker = null;
        s_tauntNoticeShown = false;
        ForcedDimAlpha = 0.3f;
        TurnState.Reset();
        allViews.Clear();
    }

    public void PlayAttackAnim()
    {
        if (this.weaponInstance == null) return;
        string t_trigger = this.boundCard?.data.attackEffect?.animTrigger;
        if (string.IsNullOrEmpty(t_trigger)) return;
        this.weaponInstance.SetActive(true);
        this.weaponInstance.transform.localRotation = this.weaponBaseRot;
        if (this.weaponAnimator == null) return;
        this.weaponAnimator.enabled = true;
        this.weaponAnimator.Play(t_trigger, 0, 0f);
    }

#endregion

    #region Debug
    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;

        Vector3 t_edgeWorld   = Camera.main.ScreenToWorldPoint(new Vector3(Screen.width * 0.5f + this.deadZoneRadius, Screen.height * 0.5f, 10f));
        Vector3 t_centerWorld = Camera.main.ScreenToWorldPoint(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 10f));
        float t_worldRadius   = Vector3.Distance(t_centerWorld, t_edgeWorld);
        Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, t_worldRadius);

        if (this.dragState == DragState.Idle) return;
        Vector2 t_drag = this.currentDragScreenPos - this.dragStartScreenPos;
        if (t_drag.magnitude < this.dragThreshold) return;

        Vector3 t_a = Camera.main.ScreenToWorldPoint(new Vector3(this.dragStartScreenPos.x,   this.dragStartScreenPos.y,   10f));
        Vector3 t_b = Camera.main.ScreenToWorldPoint(new Vector3(this.currentDragScreenPos.x, this.currentDragScreenPos.y, 10f));
        Vector3 t_dragWorldDir = (t_b - t_a).normalized;

        Gizmos.color = Color.gray;
        Gizmos.DrawRay(transform.position, t_dragWorldDir * 2f);
        Gizmos.color = Color.red;
        Gizmos.DrawRay(transform.position, -t_dragWorldDir * 3f);

        if (this.currentTarget != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(transform.position, this.currentTarget.transform.position);
        }

        if (this.dragState == DragState.AttackDrag && this.activeGesture == Gesture.DragDown)
        {
            float t_boundY    = Mathf.Sqrt(Mathf.Max(0f, 1f - this.dirThreshold * this.dirThreshold));
            Vector3 t_cardScreen = Camera.main.WorldToScreenPoint(transform.position);
            Vector3 ScreenDirToWorld(Vector2 _sd)
            {
                Vector3 t_o = Camera.main.ScreenToWorldPoint(new Vector3(t_cardScreen.x, t_cardScreen.y, 10f));
                Vector3 t_e = Camera.main.ScreenToWorldPoint(new Vector3(t_cardScreen.x + _sd.x * 200f, t_cardScreen.y + _sd.y * 200f, 10f));
                return (t_e - t_o).normalized;
            }
            Vector3 t_leftBound  = ScreenDirToWorld(new Vector2(-this.dirThreshold, -t_boundY));
            Vector3 t_rightBound = ScreenDirToWorld(new Vector2( this.dirThreshold, -t_boundY));

            Gizmos.color = new Color(0f, 0.8f, 1f, 0.8f);
            Gizmos.DrawRay(transform.position, t_leftBound  * 4f);
            Gizmos.color = new Color(1f, 0.4f, 0f, 0.8f);
            Gizmos.DrawRay(transform.position, t_rightBound * 4f);
        }
    }
    #endregion
}

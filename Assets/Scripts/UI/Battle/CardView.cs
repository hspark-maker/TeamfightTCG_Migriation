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

    enum DragState { Idle, AttackDrag, ReturnDrag }

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
    [SerializeField] TMP_Text nameText;
    [SerializeField] SpriteRenderer illustration;
    [SerializeField] GameObject faceDownOverlay;
    [SerializeField] GameObject emptyOverlay;

    [Header("Highlight / Glow")]
    [SerializeField] SpriteRenderer selectedHighlight;
    [SerializeField] ParticleSystem passiveGlowSystem;
    [SerializeField] GameObject keywordGlowPrefab;
    [SerializeField] float targetFocusScale = 1.15f;   // 드래그 조준 시 타겟 적 카드 확대 배율.
    [SerializeField] float targetFocusDur   = 0.15f;

    [Header("Weapon")]
    [SerializeField] Transform weaponAnchor;

    [Header("Keywords")]
    [SerializeField] Transform keywordIconRoot;
    [SerializeField] GameObject keywordIconPrefab;
    [SerializeField] KeywordIconConfig keywordIconConfig;
    [SerializeField] float iconSpacing = 0.3f;
    // true면 키워드 아이콘을 시너지 배지 자리(좌측 세로열)에 그리고, 시너지 배지는 표시하지 않는다.
    // 한 자리에 둘 다 그리면 겹치므로 "그 자리의 주인"은 이 스위치 하나가 정한다(양쪽 분기의 단일 진실원).
    // false로 되돌리면 종전대로 키워드=우하단 가로줄 + 시너지 배지 복귀.
    [SerializeField] bool keywordIconsUseSynergySlot = true;

    [Header("Synergy")]
    [SerializeField] Transform synergyBadgeRoot;         // 배지들을 붙일 앵커(자식 루트). keywordIconRoot와 동일 패턴.
    [SerializeField] SynergyBadgeView synergyBadgePrefab; // 색+텍스트 배지 프리팹.
    [SerializeField] float synergyBadgeXPos   = 0.55f;  // 배지 세로열 X(synergyBadgeRoot 기준).
    [SerializeField] float synergyBadgeYStart = 0.95f;  // 첫 배지(i=0) Y.
    [SerializeField] float synergyBadgeYStep  = -0.5f;  // 배지 간 Y 간격(아래로 쌓기).
    // 표시 최대 배지 수(초과분 드롭). 기본값은 CardVisualRules 상수 하나에서 — 프리팹 오버라이드는 남지만
    // 아웃게임 타일과 기본값이 따로 놀지 않게 코드 소스를 통일한다.
    [SerializeField] int   synergyMaxBadges   = CardVisualRules.MaxSynergyBadges;

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


        this.centerPos = CameraUtil.ScreenFractionToWorld(0.5f, 0.5f, transform.position.z - 0.5f);
        this.centerPos.y = transform.position.y;

        float t_destY = Camera.main.WorldToScreenPoint(this.centerPos).y;
        this.dragStartScreenPos   = new Vector2(Screen.width * 0.5f, t_destY);
        this.currentDragScreenPos = this.dragStartScreenPos;

        FocusWeapon(true);
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
            ClearAttackerSelection();   // 드래그 시작 — 대기 중인 탭 무장 취소(입력 상호배타).
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

        HandleAimDrag(t_drag, t_touchDrag, _forward: this.activeGesture != Gesture.DragDown);
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

    // 드래그 모드 전환 시 이전 모드 UI/상태 정리. dragState=Idle로 리셋 → 새 핸들러가 첫 프레임에 카드 이동/페이드를 재초기화.
    void SwitchGesture()
    {
        this.swipeGuide?.SetVisible(false);
        HideDragLine();
        ClearTargetPreview();
        this.dragState = DragState.Idle;
    }

    /// <summary>조준 드래그 공통 처리. 드래그-백/포워드는 딱 두 가지만 다르다:
    /// ① 조준이 켜지는 손가락 방향(_forward=위로 / false=아래로) ② 조준 방향 부호(밀기 = 그 방향, 당기기 = 반대 방향).
    /// 가이드는 조준 방향(t_aimDir.x)을 그대로 받으므로 모드가 바뀌면 자연히 반전된다.
    /// 데드존 진입 시 타깃 취소, 카드 센터 이동, 되돌리기 복귀는 두 모드가 동일하다.</summary>
    void HandleAimDrag(Vector2 _drag, Vector2 _touchDrag, bool _forward)
    {
        if (_forward ? _touchDrag.y > 0f : _touchDrag.y < 0f)
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


            Vector2 t_aimDir = (_forward ? _drag : -_drag).normalized;
            this.swipeGuide?.UpdateDirection(t_aimDir.x);

            if (_drag.magnitude < this.deadZoneRadius)
            {
                ClearTargetPreview();
                return;
            }

            UpdateTarget(t_aimDir);
        }
        else
        {
            this.swipeGuide?.SetVisible(false);
            ClearTargetPreview();
            if (this.dragState == DragState.AttackDrag || this.dragState == DragState.ReturnDrag)
            {
                this.dragState = DragState.ReturnDrag;
                float t_screenDist = Vector2.Distance(
                    Camera.main.WorldToScreenPoint(this.cardAnim.SlotPosition),
                    Camera.main.WorldToScreenPoint(this.centerPos));
                float t_alpha = t_screenDist > 0f ? Mathf.Clamp01(_touchDrag.magnitude / t_screenDist) : 0f;
                transform.DOKill();
                transform.position = Vector3.Lerp(this.centerPos, this.cardAnim.SlotPosition, t_alpha);
            }
        }
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
                    OnAttack?.Invoke(this, this.currentTarget);
                    ClearTargetPreview();
                    this.dragState = DragState.Idle;
                    return;
                }
                ClearTargetPreview();
                FocusWeapon(false);
                RestoreAllFades();
                this.cardAnim.MoveToSlot().Forget();
            }
            this.dragState = DragState.Idle;
            return;
        }

        // 탭 판정: 이 터치가 드래그로 확정되지 않았고(제스처 None) 손가락 이동이 데드존 이내.
        bool t_isTap = this.activeGesture == Gesture.None
            && Vector2.Distance((Vector2)Input.mousePosition, this.touchStartScreenPos) < this.deadZoneRadius;

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
            bool t_attacked = this.currentTarget != null;
            if (t_attacked)
                OnAttack?.Invoke(this, this.currentTarget);

            ClearTargetPreview();

            if (!t_attacked)
            {
                RestoreAllFades();
                FocusWeapon(false);
                this.cardAnim.MoveToSlot().Forget();
            }
        }
        else
        {
            FocusWeapon(false);
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

    /// <summary>도발 차단 안내 배너. 초상화는 도발 아이콘(있으면), 없으면 도발 카드 초상화.</summary>
    static void ShowTauntBlockedNotice(CardInstance _tauntCard)
    {
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
        if (this.boundCard != _card) this.cardAnim.ResetHitEffect();

        this.boundCard = _card;
        this.cardAnim.SetBoundCard(_card);
        bool t_isEmpty = _card == null;

        this.emptyOverlay.SetActive(t_isEmpty);
        SetHighlight(false);

        if (t_isEmpty)
        {
            this.faceDownOverlay.SetActive(false);
            SetupWeapon(null);
            RefreshKeywordIcons(null);   // 빈 슬롯: 아이콘 없음.
            RefreshSynergyBadges(null);
            return;
        }

        bool t_isFaceDown = !_card.isRevealed;
        this.faceDownOverlay.SetActive(t_isFaceDown);

        if (t_isFaceDown) SetHpDisplay("?", "");
        else SetHpDisplay(_card.hp.ToString(), _card.bonusHp > 0 ? $"+{_card.bonusHp}" : "");
        this.nameText.text = t_isFaceDown ? "???" : _card.data.displayName;

        if (this.illustration != null && _card.data.battleImage != null)
            this.illustration.sprite = _card.data.battleImage;

        SetupWeapon(_card.data);
        RefreshKeywordIcons(_card);   // 뒷면 은닉·표시 대상 판정은 RefreshKeywordIcons 안에서.
        RefreshSynergyBadges(_synergy);
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

    public void SetHighlight(bool _active)
    {
        if (this.selectedHighlight != null)
            this.selectedHighlight.enabled = _active;
    }

    // 조준 포커스: 이 카드를 확대(_on)/원복. 드래그 타겟 전환·탭 무장/해제 시 호출.
    // 카드 이동 tween과 겹치지 않는 idle 상태에서만 사용.
    // _instant: 공격 발동 직전 원복처럼 뒤이어 AttackSequence의 DOKill이 들어오는 경로 — 트윈이 중간에 죽어
    // 확대된 채 고착되지 않도록 스케일을 즉시 되돌린다.
    public void SetTargetFocus(bool _on, bool _instant = false)
    {
        transform.DOKill();
        float t_scale = _on ? this.targetFocusScale : 1f;
        if (_instant) { transform.localScale = Vector3.one * t_scale; return; }
        transform.DOScale(t_scale, this.targetFocusDur)
                 .SetEase(Ease.OutBack).SetLink(gameObject);
    }

    public void ShowAttackPreview(int _damage, bool _wouldDie, bool _isAttackHit = true)
    {
        if (this.hpText == null || this.boundCard == null) return;
        this.hpTextOriginalColor = this.hpText.color;
        this.hpText.DOKill();
        // _isAttackHit=직격(공격) 프리뷰면 비늘 감소 반영, 반격 프리뷰면 false → WouldDieFrom과 동일 소스로 HP표시 일치.
        (int t_hpAfter, int t_bonusAfter) = this.boundCard.PreviewAfterDamage(_damage, _isAttackHit);
        SetHpDisplay(t_hpAfter.ToString(), t_bonusAfter > 0 ? $"+{t_bonusAfter}" : "");
        this.hpText.color = Color.red;

        if (_wouldDie)
        {
            this.hpText.DOFade(0f, GameTiming.Battle.AttackPreviewFlash).SetLoops(-1, LoopType.Yoyo).SetLink(gameObject);
            this.cardAnim.ShowDeathPreview();
        }
    }

    public void HideAttackPreview()
    {
        if (this.hpText == null || this.boundCard == null) return;
        this.hpText.DOKill();
        Color t_c = this.hpTextOriginalColor;
        t_c.a = 1f;
        this.hpText.color = t_c;
        SetHpDisplay(this.boundCard.hp.ToString(), this.boundCard.bonusHp > 0 ? $"+{this.boundCard.bonusHp}" : "");
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
            CardVisualRules.CollectKeywordIcons(CardVisualRules.TraitKeywords(_card), this.keywordIconConfig);

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
    public UniTask MoveToCenter()                  => this.cardAnim.MoveToCenter();
    public UniTask MoveToCinemaSlot()              => this.cardAnim.MoveToCinemaSlot();
    public UniTask MoveToCinemaPosition(int _posIndex, int _totalCount) => this.cardAnim.MoveToCinemaPosition(_posIndex, _totalCount);
    public UniTask MoveTo(Vector3 _pos)            => this.cardAnim.MoveTo(_pos);
    public UniTask MoveToSlot()                    => this.cardAnim.MoveToSlot();
    public async UniTask PlayHitAnim(float _d = 0.15f, int _damage = 0)
    {
        if (this.boundCard != null)
            SetHpDisplay(this.boundCard.hp.ToString(), this.boundCard.bonusHp > 0 ? $"+{this.boundCard.bonusHp}" : "");
        await this.cardAnim.PlayHitAnim(_d, _damage);
    }
    public UniTask PlayDeathAnim(float _d = 0.4f)  => this.cardAnim.PlayDeathAnim(_d);

    /// <summary>회복 연출(HealEffect: 붐 + "+N") + HP 표기 갱신. CardInstance.Heal/ReviveAtHalf가 실제 회복량으로 호출.</summary>
    public void PlayHealEffect(int _amount)
    {
        if (this.boundCard != null)
            SetHpDisplay(this.boundCard.hp.ToString(), this.boundCard.bonusHp > 0 ? $"+{this.boundCard.bonusHp}" : "");
        this.cardAnim.PlayHealEffect(_amount);
    }
    public void FadeView(float _alpha, float _dur) => this.cardAnim.FadeView(_alpha, _dur);

    public void HideSlot()
    {
        this.emptyOverlay.SetActive(false);
        this.cardAnim.FadeView(0f, 0.3f);
    }

    public UniTask PlayDealAnim(Vector3 _from, Vector3 _mid, Vector3 _dest, float _duration = 0.6f)
        => this.cardAnim.PlayDealAnim(_from, _mid, _dest, _duration);

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

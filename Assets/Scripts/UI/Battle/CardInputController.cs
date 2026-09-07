using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>카드 한 장의 **입력 제스처 상태머신 전부**를 소유한다.
/// 터치 시작 → 탭/드래그 판정 → 조준 방향·데드존 → 타깃 추적/거절 → 손 뗌(공격 발동/복귀)까지,
/// 그리고 롱프레스(정보·시너지 팝업) 수명이 여기 있다.
///
/// MonoBehaviour가 아니라 순수 C# 객체다 — <see cref="CardView"/>가 필드로 들고 생성한다.
/// CardView에는 <c>OnMouseDown/Drag/Up</c>·<c>Update</c>·<c>OnDrawGizmos</c>가 얇은 전달 스텁으로만 남는다:
/// Unity의 OnMouse* 메시지는 콜라이더가 달린 GameObject의 컴포넌트에만 가고,
/// 별도 MonoBehaviour로 빼면 프리팹/씬 YAML을 재직렬화해야 하기 때문이다.
///
/// 인스펙터 배선/튜닝값(dragThreshold·deadZoneRadius·dirThreshold·hintArrow·swipeGuide·dragLine·aimTilt*)은
/// 전부 CardView의 SerializeField에 그대로 남고, 여기엔 생성자로 **값만** 주입된다.
///
/// 경계는 단방향이다: CardInputController → CardView / BattleSelection / BattleBoardView.
/// CardView는 이 클래스의 내부 상태를 읽지 않는다(전달 스텁 + 다른 카드의 조준 기울기 원복만).</summary>
public class CardInputController
{
    enum DragState { Idle, AttackDrag }

    // 한 터치의 제스처 종류. 손가락이 dragThreshold를 넘는 순간 초기 세로 방향으로 확정, 그 터치 끝까지 고정.
    // DragUp=위로 떠서 적에게 끌기(콜라이더 타깃). DragDown=아래로 끌기(좌우반전 방향 타깃). None=미확정/탭.
    enum Gesture { None, DragUp, DragDown }

    #region Fields
    readonly CardView  owner;
    readonly Transform ownerTransform;

    // ── 주입값(단일 진실원은 CardView의 SerializeField) ──
    readonly Collider2D   selfCollider;   // 롱프레스 팝업을 끌 때 "카드 범위를 벗어났는지" 판정용
    readonly HintArrow    hintArrow;
    readonly SwipeGuide   swipeGuide;
    readonly LineRenderer dragLine;
    readonly float        dragThreshold;
    readonly float        deadZoneRadius;
    readonly float        dirThreshold;
    readonly float        aimTiltMaxAngle;
    readonly float        aimTiltSpeed;

    // ── 제스처 상태 ──
    Vector3 centerPos;
    Vector2 dragStartScreenPos;
    Vector2 currentDragScreenPos;
    Vector2 touchStartScreenPos;
    DragState dragState;
    Gesture activeGesture;   // 이번 터치의 제스처(위/아래 드래그). 탭은 None 유지.
    CardView currentTarget;
    CardView rejectedTarget;   // 직전에 거절 연출을 띄운 무효 타깃(프레임마다 반복 발화 방지). 대상이 바뀌면 다시 발화.
    bool       hasAimTilt;    // 조준 기울기가 걸려 있나(복원 필요 여부)
    Quaternion aimTiltBase;   // 기울기 걸기 직전의 로컬 회전 — 조준 종료 시 여기로 되돌린다

    bool longPressFired;
    bool longPressSynergyShown;   // true면 카드 정보가 아니라 시너지 설명 팝업을 띄운 상태
    bool longPressDimShown;       // 누르는 동안 배경 어둡기를 띄웠나(팝업이 뜨기 전에도 켜지므로 따로 센다)
    CancellationTokenSource longPressCts;
    #endregion

    public CardInputController(
        CardView     _owner,
        Collider2D   _selfCollider,
        HintArrow    _hintArrow,
        SwipeGuide   _swipeGuide,
        LineRenderer _dragLine,
        float        _dragThreshold,
        float        _deadZoneRadius,
        float        _dirThreshold,
        float        _aimTiltMaxAngle,
        float        _aimTiltSpeed)
    {
        this.owner           = _owner;
        this.ownerTransform  = _owner.transform;
        this.selfCollider    = _selfCollider;
        this.hintArrow       = _hintArrow;
        this.swipeGuide      = _swipeGuide;
        this.dragLine        = _dragLine;
        this.dragThreshold   = _dragThreshold;
        this.deadZoneRadius  = _deadZoneRadius;
        this.dirThreshold    = _dirThreshold;
        this.aimTiltMaxAngle = _aimTiltMaxAngle;
        this.aimTiltSpeed    = _aimTiltSpeed;
    }

    #region Frame
    /// <summary>CardView.Update가 매 프레임 그대로 전달. 입력이 닫혔을 때의 복귀 처리가 전부다.</summary>
    public void Tick()
    {
        // 입력이 닫히면(턴 종료/타임아웃 등) 무장된 탭 공격자 강조가 고착되지 않게 해제.
        if (BattleSelection.SelectedAttacker != null && !TurnState.CardInputAllowed)
            BattleSelection.Clear(_instant: true);   // 아래 MoveTo의 루트 DOKill에 축소 트윈이 잘리지 않게 즉시 확정.

        if (this.longPressCts != null && (!TurnState.CardInputAllowed || this.owner.BoundCard == null))
            CancelLongPress();

        // 들고(드래그) 있다가 입력이 닫히면(생각시간 초과/턴 종료) 슬롯으로 복귀 — 센터에 고착 방지.
        // 이 카드가 그대로 공격자가 되면 AttackSequence의 DOKill이 이 이동을 덮으므로 충돌 안 남.
        if (this.dragState != DragState.Idle && !TurnState.CardInputAllowed)
        {
            this.dragState = DragState.Idle;
            this.swipeGuide?.SetVisible(false);
            HideDragLine();
            ClearTargetPreview();
            BattleBoardView.RestoreAllFades();
            this.owner.FocusWeapon(false);
            ResetAimTilt();
            this.owner.MoveToSlot().Forget();
        }

        if (this.hintArrow == null) return;
        this.hintArrow.SetVisible(false);   // 가이드 화살표 완전 비표시(일반+튜토리얼)
    }
    #endregion

    #region Pointer
    public void OnMouseDown()
    {
        if (!TurnState.CardInputAllowed || this.owner.BoundCard == null) return;
        if (TurnState.ForcedAttacker != null && this.owner.BoundCard.ownerIndex == TurnState.LocalOwnerIndex
            && this.owner.BoundCard != TurnState.ForcedAttacker) return;   // 적 카드는 통과(탭 공격 발사 위해)

        CancelLongPress();
        this.touchStartScreenPos  = (Vector2)Input.mousePosition;
        this.dragStartScreenPos   = this.touchStartScreenPos;
        this.currentDragScreenPos = this.touchStartScreenPos;
        this.activeGesture        = Gesture.None;   // 새 터치 — 제스처 미확정(탭/드래그 판정 대기).

        this.longPressCts = CancellationTokenSource.CreateLinkedTokenSource(
            this.owner.GetCancellationTokenOnDestroy());
        WaitLongPress(this.longPressCts.Token).Forget();

        if (this.owner.BoundCard.ownerIndex != TurnState.LocalOwnerIndex) return;

        this.dragState   = DragState.Idle;


        // 떠오르는 자리는 CardAnimator.MoveToCenter와 같은 규칙으로 잡는다 — 슬롯의 z·y를 그대로 두고
        // 화면 가로 중앙만 취한다. transform.position.z에서 0.5를 빼면 원근 때문에 그 점이 소실점 쪽으로
        // 끌려가 카드가 자기 줄보다 위로 떠 보인다(= 필드 중앙이 아니라 화면 중앙으로 가는 증상).
        this.centerPos   = CameraUtil.ScreenFractionToWorld(0.5f, 0.5f, this.owner.SlotPosition.z);
        this.centerPos.y = this.owner.SlotPosition.y;

        float t_destY = Camera.main.WorldToScreenPoint(this.centerPos).y;
        this.dragStartScreenPos   = new Vector2(Screen.width * 0.5f, t_destY);
        this.currentDragScreenPos = this.dragStartScreenPos;

        // 무장 연출(무기 조준 + 이펙트)은 드래그 조준일 때만 누르는 순간 켠다.
        // 드래그가 꺼진 지금은 "선택된 공격자"만 무장 상태여야 하므로 ToggleSelectAttacker가 켠다 —
        // 여기서 켜면 선택되지 않은 카드에 이펙트가 남아 누가 공격자인지 흐려진다.
        if (!BattleUxFlags.DragAimAttack) return;

        this.owner.FocusWeapon(true);
    }

    public void OnMouseDrag()
    {
        if (!TurnState.CardInputAllowed || this.owner.BoundCard == null) return;

        // 처형/튜토리얼 지정 공격자 외 카드는 조작 불가(완전 무반응).
        if (TurnState.ForcedAttacker != null && this.owner.BoundCard.ownerIndex == TurnState.LocalOwnerIndex
            && this.owner.BoundCard != TurnState.ForcedAttacker) return;   // 적 카드는 통과(탭 공격 발사 위해)

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
            BattleSelection.Clear(_instant: BattleSelection.SelectedAttacker == this.owner);
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

        if (this.owner.BoundCard == null || this.owner.BoundCard.ownerIndex != TurnState.LocalOwnerIndex) return;

        Vector2 t_drag = this.currentDragScreenPos - this.dragStartScreenPos;   // DragBack 조준용(화면 중앙 기준).

        HandleAimDrag(t_drag, _forward: this.activeGesture != Gesture.DragDown);
    }

    public void OnMouseUp()
    {
        // 처형/튜토리얼 지정 공격자 외 카드는 완전 무반응(클릭·탭 무시).
        if (TurnState.ForcedAttacker != null && this.owner.BoundCard != null
            && this.owner.BoundCard.ownerIndex == TurnState.LocalOwnerIndex
            && this.owner.BoundCard != TurnState.ForcedAttacker)
        {
            CancelLongPress();
            return;   // 적 카드는 통과(무장된 공격자 → 적 탭 발사 위해)
        }

        // 드래그 중 턴 종료·카드 사망으로 early-return해도 드래그 상태가
        // 고착되지 않도록 가드 통과 전에 반드시 해제한다.
        if (!TurnState.CardInputAllowed || this.owner.BoundCard == null)
        {
            this.dragState = DragState.Idle;
            this.swipeGuide?.SetVisible(false);
            HideDragLine();
            CancelLongPress();
            return;
        }
        this.swipeGuide?.SetVisible(false);
        HideDragLine();
        bool t_wasLongPress = this.longPressFired;
        CancelLongPress();

        if (t_wasLongPress)
        {
            UIPoolManager.Instance?.HideUI<PooledCardElement>();

            // 튜토리얼 Inspect 스텝: 적 카드 롱프레스 = "상대 정보 확인" 레슨. 팝업이 뜨는 순간이 아니라
            // **손을 뗀 순간**에 인정한다 — 뜨자마자 스텝이 소비되어 팝업을 못 읽는 버그 방지.
            if (TutorialConfig.IsActive && this.owner.BoundCard != null
                && this.owner.BoundCard.ownerIndex != TurnState.LocalOwnerIndex)
                TutorialOverlayUI.Instance?.NotifyInspected();

            if (this.owner.BoundCard?.ownerIndex == TurnState.LocalOwnerIndex)
            {
                if (this.currentTarget != null)
                {
                    ResetAimTilt();   // 기울어진 채 넘기면 Headbutt이 그 각도를 복귀 목표로 잡는다
                    // 조준 정리를 **공격 발동보다 먼저** — 발동 뒤에 정리하면 정리 쪽 트윈/Kill이
                    // 막 시작한 연출 이동을 건드린다(피격자가 제자리에 남던 원인).
                    CardView t_target = this.currentTarget;
                    ClearTargetPreview();
                    BattleSelection.NotifyAttack(this.owner, t_target);
                    this.dragState = DragState.Idle;
                    return;
                }
                ClearTargetPreview();
                ResetAimTilt();
                if (BattleSelection.SelectedAttacker != this.owner)
                {
                    this.owner.FocusWeapon(false);
                    BattleBoardView.RestoreAllFades();
                    this.owner.MoveToSlot().Forget();
                }
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
        if (this.owner.BoundCard.ownerIndex != TurnState.LocalOwnerIndex)
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
                BattleSelection.NotifyAttack(this.owner, t_target);

            if (!t_attacked)
            {
                BattleBoardView.RestoreAllFades();
                this.owner.FocusWeapon(false);
                this.owner.MoveToSlot().Forget();
            }
            // 공격으로 이어진 경우 무장 이펙트는 끄지 않는다 — 반동 끝에서 AttackSequence가 끈다.
        }
        else
        {
            this.owner.FocusWeapon(false);
            ResetAimTilt();
            this.owner.MoveToSlot().Forget();
        }
        this.dragState     = DragState.Idle;
        this.activeGesture = Gesture.None;
    }
    #endregion

    #region Aim / Gesture
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
            this.aimTiltBase = this.ownerTransform.localRotation;   // 조준 시작 각도 = 복원 지점
        }

        float      t_angle  = -Mathf.Clamp(_aimX, -1f, 1f) * this.aimTiltMaxAngle;
        Quaternion t_target = this.aimTiltBase * Quaternion.Euler(0f, 0f, t_angle);
        float      t_t      = 1f - Mathf.Exp(-this.aimTiltSpeed * Time.deltaTime);
        this.ownerTransform.localRotation = Quaternion.Slerp(this.ownerTransform.localRotation, t_target, t_t);
    }

    /// <summary>조준 기울기 해제 — 시작 각도로 즉시 복원. 조준이 끝나는 모든 경로에서 부른다.
    /// **공격 발동 전에도 반드시** 호출해야 한다: Headbutt이 현재 각도를 baseRot으로 잡아
    /// 복귀 목표로 쓰므로, 기울어진 채 넘기면 공격 후 카드가 비스듬히 굳는다.
    ///
    /// public인 이유는 하나뿐이다 — 탭 공격(HandleEnemyTap)은 **적 카드의** 컨트롤러에서 돌면서
    /// 공격자 카드의 기울기를 원복해야 한다(CardView.ResetAimTilt 스텁 경유).</summary>
    public void ResetAimTilt()
    {
        if (!this.hasAimTilt) return;
        this.hasAimTilt = false;
        this.ownerTransform.localRotation = this.aimTiltBase;
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
            BeginTargeting();
            var t_validTargets = GetValidEnemyViews(this.owner);
            ApplyDragTargetFade(t_validTargets);
            this.owner.MoveTo(this.centerPos).Forget();
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

    void HideDragLine()
    {
        if (this.dragLine != null) this.dragLine.enabled = false;
    }
    #endregion

    #region Long press
    /// <summary>롱프레스 확정 직전 카메라가 출발할 수 있는 남은 시간.
    /// 비율보다 실제 시간으로 두어 프레임레이트가 달라도 같은 구간에서 확인한다.</summary>
    const float CameraLiftLead = 0.08f;

    async UniTask WaitLongPress(CancellationToken _ct)
    {
        try
        {
            // dim/blur는 확정 뒤에만 켠다. 시너지 배지는 작은 툴팁이라 화면을 어둡게 하지 않는다.
            bool  t_dim     = this.owner.BoundCard != null && this.owner.BoundCard.isRevealed
                           && FindBadgeAt(this.touchStartScreenPos) == null;
            float t_wait    = Mathf.Max(0.01f, GameTiming.Battle.LongPress);
            float t_elapsed = 0f;
            bool  t_cameraLiftPending = false;
            while (t_elapsed < t_wait)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, _ct);

                // 짧은 탭의 Up 이벤트가 저프레임에서 유실돼도 실제 포인터 상태로 즉시 복구한다.
                if (!TurnState.CardInputAllowed || this.owner.BoundCard == null || !IsPointerHeld())
                {
                    CancelLongPress();
                    return;
                }

                // 드래그 이벤트 빈도에 기대지 않고 대기 루프가 직접 이동 취소를 판정한다.
                if (Vector2.Distance((Vector2)Input.mousePosition, this.touchStartScreenPos) > this.deadZoneRadius)
                {
                    CancelLongPress();
                    return;
                }

                t_elapsed += Time.deltaTime;
                if (!t_dim) continue;

                // 임계 직전 구간에 들어온 뒤 다음 프레임에도 눌려 있어야 카메라를 움직인다.
                if (t_wait - t_elapsed <= CameraLiftLead)
                {
                    if (t_cameraLiftPending) BattleCamera.SetLongPressLift(true);
                    else t_cameraLiftPending = true;
                }

            }

            if (this.owner.BoundCard == null || !this.owner.BoundCard.isRevealed)
            {
                CancelLongPress();
                return;
            }

            // dim/blur는 롱프레스 확정 뒤에만 켠다. 저프레임 경계에서 짧은 탭에 반응하면 안 된다.
            if (t_dim)
            {
                ShowPressDim();
                ScreenBlurFeature.Strength = 1f;
            }

            // 누른 지점이 시너지 배지 위면 카드 정보 대신 시너지 설명을 띄운다.
            SynergyBadgeView t_badge = FindBadgeAt(this.touchStartScreenPos);
            if (t_badge != null && t_badge.Synergy != null)
            {
                ExplainPopupData t_data = ExplainPopupData.ForSynergy(
                    t_badge.Synergy, OwnedCountOf(t_badge.Synergy));
                t_data.hasWorldAnchor = true;                    // 배지는 월드 스페이스라 RectTransform이 없다
                t_data.worldAnchor    = t_badge.transform.position;
                t_data.worldHalfWidth = BadgeHalfWidth(t_badge);

                UIPoolManager.Instance?.AddOrUpdateUI<ExplainPopupUI>(t_data);
                this.longPressSynergyShown = true;
            }
            else
            {
                // 시너지 스냅샷을 같이 넘긴다 — 정보창이 활성/비활성을 갈라 그리는 근거다.
                // 재계산하지 않고 이 카드에 마지막으로 그려진 확정 상태를 그대로 쓴다(단일 진실원).
                UIPoolManager.Instance?.AddOrUpdateUI<PooledCardElement>(
                    new PooledCardElementData
                    {
                        cardId   = this.owner.BoundCard.cardId,
                        instance = this.owner.BoundCard,
                        synergy  = this.owner.LastBadgeState,
                    });

                // 정보를 보는 그 카드만 살짝 떠오른다. 시너지 배지 툴팁 쪽은 카드 정보가 아니므로 제외.
                // 카메라는 이미 임계 직전 확인 구간에서 출발했다 — 여기서 다시 걸지 않는다.
                this.owner.SetLongPressLift(true);
            }
            this.longPressFired = true;
            // Inspect 통지는 손을 뗀 순간(OnMouseUp)으로 이동 — 팝업이 뜨자마자 스텝이 넘어가지 않도록.
        }
        catch (OperationCanceledException) { }
    }

    static bool IsPointerHeld()
    {
        if (Input.touchCount == 0) return Input.GetMouseButton(0);

        for (int i = 0; i < Input.touchCount; i++)
        {
            TouchPhase t_phase = Input.GetTouch(i).phase;
            if (t_phase == TouchPhase.Began || t_phase == TouchPhase.Moved || t_phase == TouchPhase.Stationary)
                return true;
        }
        return false;
    }

    /// <summary>롱프레스 확정 뒤 카드 정보창의 배경 어둡기를 켠다.</summary>
    void ShowPressDim()
    {
        UIPoolManager.Instance?.AddOrUpdateUI<PooledCardElement>(new PooledCardElementData
        {
            cardId      = this.owner.BoundCard?.cardId ?? 0,
            dimOnly     = true,
            dimProgress = 1f,
        });
        this.longPressDimShown = true;
    }

    /// <summary>화면 좌표 아래에 있는 시너지 배지. 없으면 null.
    /// 배지에 콜라이더를 붙이면 카드 드래그 입력을 가로채므로, 스프라이트 bounds로만 판정한다.</summary>
    SynergyBadgeView FindBadgeAt(Vector2 _screenPos)
    {
        if (this.owner.SynergyBadgeRoot == null || Camera.main == null) return null;

        Vector3 t_world = Camera.main.ScreenToWorldPoint(
            new Vector3(_screenPos.x, _screenPos.y, -Camera.main.transform.position.z));

        foreach (Transform t_child in this.owner.SynergyBadgeRoot)
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
        if (_synergy == null || this.owner.LastBadgeState?.Active == null) return -1;
        foreach (var t_a in this.owner.LastBadgeState.Active)
            if (t_a?.Runtime != null &&
                string.Equals(t_a.Runtime.SynergyId, _synergy.SynergyId, System.StringComparison.Ordinal)) return t_a.Count;
        return -1;
    }

    /// <summary>롱프레스 취소. **이미 떠 있던 설명 팝업도 같이 닫는다.**
    /// 예전엔 플래그만 지워서, 드래그로 취소된 경우 OnMouseUp의 t_wasLongPress가 false가 되어
    /// 팝업을 닫는 분기를 건너뛰고 화면에 그대로 남았다(잔류 버그).</summary>
    void CancelLongPress()
    {
        // 카드 정보 / 시너지 설명 중 실제로 띄운 쪽을 닫는다.
        if (this.longPressFired && this.longPressSynergyShown)
            UIPoolManager.Instance?.HideUI<ExplainPopupUI>();

        // 팝업이 뜨기 전에 취소돼도(= 다 누르지 못하고 뗌·드래그) 차오르던 배경은 반드시 지운다.
        if (this.longPressDimShown || (this.longPressFired && !this.longPressSynergyShown))
            UIPoolManager.Instance?.HideUI<PooledCardElement>();

        // 배경 블러는 렌더러 전역 상태라 팝업보다 먼저·무조건 끈다 — 여기서 빠뜨리면 화면이 흐린 채로 남는다.
        ScreenBlurFeature.Strength = 0f;
        this.owner.SetLongPressLift(false);   // 뜬 카드도 같이 내린다(안 띄웠으면 무시된다)
        BattleCamera.SetLongPressLift(false); // 카메라 높이도 원복(전역 상태라 무조건 부른다)

        this.longPressDimShown     = false;
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
        if (!TutorialConfig.IsActive || this.owner.BoundCard == null) return false;
        if (this.owner.BoundCard.ownerIndex == TurnState.LocalOwnerIndex) return false;
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
    #endregion

    #region Targeting
    void ClearTargetPreview()
    {
        if (this.currentTarget != null)
        {
            this.currentTarget.SetHighlight(false);
            this.currentTarget.HideAttackPreview();
            this.currentTarget.SetTargetFocus(false);
        }
        this.owner.HideAttackPreview();
        this.currentTarget = null;
        ClearRejectFocus();   // 조준 해제 → 확대 원복 + 같은 카드에 다시 갖다 대면 거절 연출 재발화.
    }

    // ── 탭 공격(제스처3): 내 카드 탭으로 공격자 무장 → 적 카드 탭으로 발사(2단계) ──

    // 내 카드 탭: 공격자 무장 토글. 무장 시 자신+유효 적 강조(무기 조준 텔레그래프). 이미 무장이면 해제.
    void ToggleSelectAttacker()
    {
        if (BattleSelection.SelectedAttacker == this.owner) { BattleSelection.Clear(); return; }
        BattleSelection.Clear(_notify: false);   // 갈아타기 — 해제 통지를 생략해 안내가 깜빡이지 않게

        BeginTargeting();   // 무장 1회 = 안내 1회.
        this.owner.SetHighlight(true);
        this.owner.SetTargetFocus(true);   // 무장된 내 카드 살짝 확대 — 지금 누가 공격자인지 즉시 보이게.
        this.owner.FocusWeapon(true);

        var t_targets = GetValidEnemyViews(this.owner);   // 지정 타깃이면 그 하나, 아니면 도발 있을 때 도발 카드만.
        BattleBoardView.FadeAll(BattleBoardView.ForcedDimAlpha);
        BattleBoardView.FadeCards(1f, this.owner);
        BattleBoardView.FadeCards(1f, t_targets.ToArray());
        foreach (var t_cv in t_targets)
            if (BattleRules.IsTaunt(t_cv.BoundCard))
                PlayTauntGuardVfx(t_cv);

        // 유효 타겟 각각에 공격 HP 프리뷰 표시(맞으면 남는 체력/치사 점멸).
        foreach (var t_cv in t_targets)
        {
            if (t_cv.BoundCard == null || this.owner.BoundCard == null) continue;
            AttackPreview t_p = AttackPreview.Compute(this.owner.BoundCard, t_cv.BoundCard);
            t_cv.ShowAttackPreview(t_p.attackDamage, t_p.defenderWouldDie);
        }

        // 화면이 무장 상태가 된 뒤 상태 확정 + 통지(프리뷰 타겟은 해제 시 되돌리기용으로 넘긴다).
        BattleSelection.Arm(this.owner, t_targets);
        if (t_targets.Exists(cv => BattleRules.IsTaunt(cv?.BoundCard)))
            PlayTauntBlockedVfx(this.owner);
    }

    /// <summary>도발 차단 안내 이펙트를 **무장한 공격자 카드 위에** 띄운다.
    /// flip을 끄는 이유: 이건 타격 방향이 있는 파편류가 아니라 카드 위에 서는 표식이라,
    /// 진영에 따라 뒤집으면 위로 솟는 그림이 아래로 뒤집힌다(무장은 항상 내 카드지만 규약을 명시해 둔다).</summary>
    static void PlayTauntBlockedVfx(CardView _armed)
    {
        if (_armed == null) return;
        BattleVfx.PlayAttached(BattleVfxId.TauntBlocked, _armed.transform,
                               _flip: false, _armed.VfxSortingLayerId);
    }

    /// <summary>도발 보유자 **본인**에게 뜨는 연출. 공격자 쪽 차단 표식(PlayTauntBlockedVfx)과 짝이라
    /// 무장 순간 둘이 같이 난다 — 한쪽만 있으면 "왜 막혔는지"나 "누가 막는지" 중 하나가 빠진다.
    /// flip은 같은 이유로 끈다(카드 위에 서는 표식이라 진영에 따라 뒤집으면 그림이 뒤집힌다).</summary>
    static void PlayTauntGuardVfx(CardView _taunt)
    {
        if (_taunt == null) return;
        BattleVfx.PlayAttached(BattleVfxId.TauntGuard, _taunt.transform,
                               _flip: false, _taunt.VfxSortingLayerId);
    }

    // 적 카드 탭: 무장된 공격자가 있고 이 적이 유효 타깃이면 공격 발동(유효 필터는 공격자 기준).
    void HandleEnemyTap()
    {
        CardView t_armed = BattleSelection.SelectedAttacker;
        if (t_armed == null) return;                       // 미무장 — 무동작(정보는 롱프레스).
        if (t_armed.BoundCard == null) { BattleSelection.Clear(); return; }

        var t_valid = GetValidEnemyViews(t_armed, out BattleRules.TargetFilter t_filter);
        if (!t_valid.Contains(this.owner))
        {
            if (t_filter == BattleRules.TargetFilter.Taunt)
                PlayTauntBlockedVfx(t_armed);
            RejectAsTarget(this.owner, t_valid);   // 침묵 무시 금지 — "저기 말고 여기"를 흔들기+펄스+문구로.
            return;
        }

        CardView t_attacker = t_armed;
        BattleSelection.Clear(_instant: true);   // 확대 즉시 원복 — 공격 연출의 DOKill에 트윈이 잘려 커진 채 굳는 것 방지.
        // 위 해제가 무장 이펙트도 끈다. 탭 공격은 여기서 바로 공격이 이어지므로 다시 켜서
        // 반동이 끝나는 지점(AttackSequence)까지 유지한다 — 드래그 공격 경로와 수명을 맞춘다.
        t_attacker.ResetAimTilt();
        BattleSelection.NotifyAttack(t_attacker, this.owner);
    }

    // ── 무효 타깃 거절 피드백 ────────────────────────────────────────────
    // 못 치는 적을 탭했을 때 무반응이면 "버그"로 읽힌다. 거절 대상은 흔들고, 쳐야 할 카드는 펄스로 끌어당긴다.
    // 도발로 막힌 경우에 한해 이유 문구까지(무장 1회당 1번 — 연타 배너 스팸 방지).
    // _keepFocus: 조준이 그 카드에 머무는 동안 확대 유지(드래그). 탭은 머무는 개념이 없어 false.
    // _rejected를 인자로 받는 이유: 거절 대상은 **다른 카드**라 그쪽 컨트롤러 상태를 건드리지 않는다.
    static void RejectAsTarget(CardView _rejected, List<CardView> _validTargets, bool _keepFocus = false)
    {
        _rejected.PlayRejectShake(_keepFocus);
        if (_validTargets != null)
            foreach (CardView t_cv in _validTargets)
                if (t_cv != null) t_cv.PlayAttentionPulse();

    }

    /// <summary>드래그 시작 시 유효 타깃 강조 페이드 — 밝기는 곧 "유효 타깃"의 시각화다.
    /// 유효 목록은 GetValidEnemyViews 하나가 정하므로(지정 타깃/도발 필터 포함) 여기엔 별도 분기가 없다.
    /// 지정 타깃 중엔 유효 타깃이 그 하나뿐 → 결과가 RestoreAllFades의 강제 상태와 같아진다.</summary>
    void ApplyDragTargetFade(List<CardView> _validTargets)
    {
        BattleBoardView.FadeAll(BattleBoardView.ForcedDimAlpha);
        BattleBoardView.FadeCards(1f, this.owner);
        BattleBoardView.FadeCards(1f, _validTargets.ToArray());
    }

    // 타겟 하나에 집중: 나머지(다른 유효 타겟 포함) 약간 fade, 자신+포커스 타겟만 밝게.
    // 기존 fade 파이프 재사용 → 드래그 종료 시 RestoreAllFades가 일괄 복원(튜토리얼 dim 포함). 새 fade 상태 없음.
    // 암전 alpha는 ForcedDimAlpha 공유 — 튜토리얼이 이 값을 덮어써도 드래그 중 밝기가 튀지 않게.
    void ApplyFocusFade(CardView _target)
    {
        BattleBoardView.FadeAll(BattleBoardView.ForcedDimAlpha);
        BattleBoardView.FadeCards(1f, this.owner);
        if (_target != null) BattleBoardView.FadeCards(1f, _target);
    }

    /// <summary>"유효 타깃 = 밝게 = 프리뷰 표시 = 공격 가능"의 뷰 표현.
    /// 판정은 BattleRules.ValidTargets 단독(지정 타깃 > 도발 > 전체) — 여기선 카드→뷰 변환만 한다.
    /// 규칙 쪽(BattleField.GetValidTargets)과 같은 함수를 쓰므로 "UI는 막는데 규칙은 되는" 상태가 생길 수 없다.
    /// static인 이유: 탭 공격은 **적 카드의** 컨트롤러가 공격자 기준 유효 목록을 물어본다.</summary>
    static List<CardView> GetValidEnemyViews(CardView _attacker) => GetValidEnemyViews(_attacker, out _);

    /// <summary><see cref="GetValidEnemyViews(CardView)"/> + 좁혀진 이유(거절 안내 판단용).</summary>
    static List<CardView> GetValidEnemyViews(CardView _attacker, out BattleRules.TargetFilter _filter)
    {
        var t_enemies = GetEnemyViews(_attacker);

        var t_cards = new List<CardInstance>(t_enemies.Count);
        foreach (CardView t_cv in t_enemies) t_cards.Add(t_cv.BoundCard);

        var t_valid  = BattleRules.ValidTargets(_attacker.BoundCard, t_cards,
                                                TurnState.ForcedTargetFor(_attacker.BoundCard), out _filter);
        var t_result = new List<CardView>(t_valid.Count);
        // 역변환은 반드시 **이 적 목록 안에서** 찾는다. BattleBoardView.GetView는 전역 첫 매치라
        // 같은 카드를 그리는 뷰가 둘이면 다른 뷰를 돌려주고, 그러면 유효 타깃이 조용히 사라진다.
        for (int i = 0; i < t_cards.Count; i++)
            if (t_valid.Contains(t_cards[i])) t_result.Add(t_enemies[i]);
        return t_result;   // 순서 = 적 목록 입력 순서(규칙이 정렬하지 않는다)
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
    static List<CardView> GetEnemyViews(CardView _self)
    {
        var t_enemies = new List<CardView>();
        foreach (CardView t_cv in BattleBoardView.Views)
        {
            if (t_cv == _self || t_cv.BoundCard == null) continue;
            if (t_cv.BoundCard.ownerIndex == _self.BoundCard?.ownerIndex) continue;
            t_enemies.Add(t_cv);
        }
        return t_enemies;
    }

    /// <summary>조준 시작(드래그 진입/탭 무장) 공통 리셋 — 이번 조준에서 거절 이력/안내 1회 카운트를 새로 시작.</summary>
    void BeginTargeting()
    {
        ClearRejectFocus();
    }

    /// <summary>같은 대상에 대해 프레임마다 거절 연출이 반복되지 않게 1회만 발화.
    /// 못 치는 카드도 조준 중엔 확대 포커스 유지 — 조준이 먹었다는 사실 자체는 보여준다.</summary>
    void RejectOnce(CardView _rejected, List<CardView> _valid)
    {
        if (this.rejectedTarget == _rejected) return;
        ClearRejectFocus();
        this.rejectedTarget = _rejected;
        RejectAsTarget(_rejected, _valid, _keepFocus: true);
    }

    /// <summary>거절 포커스 해제(조준이 벗어남/조준 종료). 확대만 원복, 다른 상태 없음.</summary>
    void ClearRejectFocus()
    {
        if (this.rejectedTarget != null) this.rejectedTarget.SetTargetFocus(false);
        this.rejectedTarget = null;
    }

    void UpdateTarget(Vector2 _aimDir)
    {
        var t_valid = GetValidEnemyViews(this.owner, out BattleRules.TargetFilter t_filter);
        var t_all   = GetEnemyViews(this.owner);

        // 도발로 좁혀진 상태면 조준은 전체 적 기준으로 판정한다 — 못 치는 쪽을 겨누면 스냅 대신 거절.
        // 좁혀진 이유는 규칙(BattleRules)이 알려준다 — 지정 타깃은 튜토리얼 스크립트 소관이라 기존 스냅 유지.
        bool t_strict = t_filter == BattleRules.TargetFilter.Taunt;
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
            this.owner.HideAttackPreview();
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
            ApplyDragTargetFade(GetValidEnemyViews(this.owner));   // 타겟 없음 → 드래그 기본(유효타겟 다 밝게)로 복귀.
        }
    }

    void ShowPreviewForTarget(CardView _target)
    {
        CardInstance t_atk = this.owner.BoundCard;
        CardInstance t_def = _target.BoundCard;
        if (t_atk == null || t_def == null) return;

        // 계산은 AttackPreview가 단독 소유(실제 전투와 갈라지지 않도록). 여기선 표시만.
        AttackPreview t_p = AttackPreview.Compute(t_atk, t_def);

        _target.ShowAttackPreview(t_p.attackDamage, t_p.defenderWouldDie);
        if (!t_p.hasCounter) return;
        this.owner.ShowAttackPreview(t_p.counterDamage, t_p.attackerWouldDie);
    }
    #endregion

    #region Debug
    /// <summary>CardView.OnDrawGizmos가 그대로 전달. 그리는 내용이 전부 입력 상태라 여기 있다
    /// (CardView에 두면 dragState/activeGesture/조준 좌표를 도로 노출해야 한다).</summary>
    public void DrawGizmos()
    {
        if (!Application.isPlaying) return;

        Vector3 t_edgeWorld   = Camera.main.ScreenToWorldPoint(new Vector3(Screen.width * 0.5f + this.deadZoneRadius, Screen.height * 0.5f, 10f));
        Vector3 t_centerWorld = Camera.main.ScreenToWorldPoint(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 10f));
        float t_worldRadius   = Vector3.Distance(t_centerWorld, t_edgeWorld);
        Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
        Gizmos.DrawWireSphere(this.ownerTransform.position, t_worldRadius);

        if (this.dragState == DragState.Idle) return;
        Vector2 t_drag = this.currentDragScreenPos - this.dragStartScreenPos;
        if (t_drag.magnitude < this.dragThreshold) return;

        Vector3 t_a = Camera.main.ScreenToWorldPoint(new Vector3(this.dragStartScreenPos.x,   this.dragStartScreenPos.y,   10f));
        Vector3 t_b = Camera.main.ScreenToWorldPoint(new Vector3(this.currentDragScreenPos.x, this.currentDragScreenPos.y, 10f));
        Vector3 t_dragWorldDir = (t_b - t_a).normalized;

        Gizmos.color = Color.gray;
        Gizmos.DrawRay(this.ownerTransform.position, t_dragWorldDir * 2f);
        Gizmos.color = Color.red;
        Gizmos.DrawRay(this.ownerTransform.position, -t_dragWorldDir * 3f);

        if (this.currentTarget != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(this.ownerTransform.position, this.currentTarget.transform.position);
        }

        if (this.dragState == DragState.AttackDrag && this.activeGesture == Gesture.DragDown)
        {
            float t_boundY    = Mathf.Sqrt(Mathf.Max(0f, 1f - this.dirThreshold * this.dirThreshold));
            Vector3 t_cardScreen = Camera.main.WorldToScreenPoint(this.ownerTransform.position);
            Vector3 ScreenDirToWorld(Vector2 _sd)
            {
                Vector3 t_o = Camera.main.ScreenToWorldPoint(new Vector3(t_cardScreen.x, t_cardScreen.y, 10f));
                Vector3 t_e = Camera.main.ScreenToWorldPoint(new Vector3(t_cardScreen.x + _sd.x * 200f, t_cardScreen.y + _sd.y * 200f, 10f));
                return (t_e - t_o).normalized;
            }
            Vector3 t_leftBound  = ScreenDirToWorld(new Vector2(-this.dirThreshold, -t_boundY));
            Vector3 t_rightBound = ScreenDirToWorld(new Vector2( this.dirThreshold, -t_boundY));

            Gizmos.color = new Color(0f, 0.8f, 1f, 0.8f);
            Gizmos.DrawRay(this.ownerTransform.position, t_leftBound  * 4f);
            Gizmos.color = new Color(1f, 0.4f, 0f, 0.8f);
            Gizmos.DrawRay(this.ownerTransform.position, t_rightBound * 4f);
        }
    }
    #endregion
}

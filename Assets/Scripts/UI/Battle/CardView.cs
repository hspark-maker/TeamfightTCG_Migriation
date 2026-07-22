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
    public static event System.Action<CardView> OnAnyClicked;
    public static event System.Action<CardView, CardView> OnAttack;

    static readonly List<CardView> allViews = new List<CardView>();
    static bool s_anyDragging;

    enum DragState { Idle, AttackDrag, ReturnDrag }

    public enum InputMode { DragBack, DragToEnemy }
    public static InputMode currentInputMode = InputMode.DragToEnemy;
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

    [Header("Weapon")]
    [SerializeField] Transform weaponAnchor;

    [Header("Keywords")]
    [SerializeField] Transform keywordIconRoot;
    [SerializeField] GameObject keywordIconPrefab;
    [SerializeField] KeywordIconConfig keywordIconConfig;
    [SerializeField] float iconSpacing = 0.3f;

    [Header("Input")]
    [SerializeField] float dragThreshold = 30f;
    [SerializeField] float deadZoneRadius = 80f;
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
    CardView currentTarget;
    GameObject weaponInstance;
    Animator weaponAnimator;
    Quaternion weaponBaseRot;
    bool longPressFired;
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

    void Update()
    {
        if (this.hintArrow == null) return;
        bool t_show = TurnState.InputAllowed
            && this.boundCard != null
            && this.boundCard.ownerIndex == TurnState.LocalOwnerIndex
            && this.boundCard.isRevealed
            && this.dragState == DragState.Idle
            && !this.longPressFired
            && !s_anyDragging
            && currentInputMode == InputMode.DragBack;
        this.hintArrow.SetVisible(t_show);
    }
    #endregion

    #region Input
    void OnMouseDown()
    {
        if (!TurnState.InputAllowed || this.boundCard == null) return;
        if (TurnState.ForcedAttacker != null && this.boundCard != TurnState.ForcedAttacker) return;

        CancelLongPress();
        this.longPressCts = CancellationTokenSource.CreateLinkedTokenSource(
            this.GetCancellationTokenOnDestroy());
        WaitLongPress(this.longPressCts.Token).Forget();

        this.touchStartScreenPos  = (Vector2)Input.mousePosition;
        this.dragStartScreenPos   = this.touchStartScreenPos;
        this.currentDragScreenPos = this.touchStartScreenPos;

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
        this.currentDragScreenPos = (Vector2)Input.mousePosition;
        Vector2 t_drag = this.currentDragScreenPos - this.dragStartScreenPos;
        if (t_drag.magnitude < this.dragThreshold) return;

        if (Vector2.Distance(this.currentDragScreenPos, this.touchStartScreenPos) > this.deadZoneRadius)
            CancelLongPress();

        if (this.boundCard == null || this.boundCard.ownerIndex != TurnState.LocalOwnerIndex) return;

        Vector2 t_touchDrag = this.currentDragScreenPos - this.touchStartScreenPos;

        if (currentInputMode == InputMode.DragBack)
            HandleDragBack(t_drag, t_touchDrag);
        else
            HandleDragToEnemy(t_touchDrag);
    }

    void HandleDragBack(Vector2 _drag, Vector2 _touchDrag)
    {
        if (_touchDrag.y < 0f)
        {
            UIPoolManager.instance?.HideUI<PooledCardElement>();

            if (this.dragState != DragState.AttackDrag)
            {
                this.dragState = DragState.AttackDrag;
                s_anyDragging  = true;
                var t_validTargets = GetValidEnemyViews();
                foreach (var t_cv in t_validTargets)
                    if (t_cv.boundCard.HasKeyword(CardKeyword.Taunt))
                        t_cv.PlayKeywordGlow(CardKeyword.Taunt).Forget();
                FadeAll(0.3f);
                FadeCards(1f, this);
                FadeCards(1f, t_validTargets.ToArray());
                this.cardAnim.MoveTo(this.centerPos).Forget();
                this.swipeGuide?.SetVisible(true);
            }


            Vector2 t_aimDir = -_drag.normalized;
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

    void HandleDragToEnemy(Vector2 _touchDrag)
    {
        if (this.dragState != DragState.AttackDrag)
        {
            UIPoolManager.instance?.HideUI<PooledCardElement>();
            this.dragState = DragState.AttackDrag;
            s_anyDragging  = true;
            var t_validTargets = GetValidEnemyViews();
            foreach (var t_cv in t_validTargets)
                if (t_cv.boundCard.HasKeyword(CardKeyword.Taunt))
                    t_cv.PlayKeywordGlow(CardKeyword.Taunt).Forget();
            FadeAll(0.3f);
            FadeCards(1f, this);
            FadeCards(1f, t_validTargets.ToArray());
            Vector3 t_liftPos = this.cardAnim.SlotPosition + Vector3.up * 0.25f;
            this.cardAnim.MoveTo(t_liftPos).Forget();
        }

        Vector3 t_mouseWorld = GetMouseWorldOnCardPlane();

        if (this.dragLine != null)
        {
            this.dragLine.enabled = true;
            this.dragLine.SetPosition(0, transform.position);
            this.dragLine.SetPosition(1, t_mouseWorld);
        }

        UpdateTargetByCollider();
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
            UIPoolManager.instance?.HideUI<PooledCardElement>();
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

        if (this.boundCard == null || this.boundCard.ownerIndex != TurnState.LocalOwnerIndex)
        {
            OnAnyClicked?.Invoke(this);
            return;
        }

        Vector2 t_releasePos = (Vector2)Input.mousePosition;
        Vector2 t_deadZoneOrigin = currentInputMode == InputMode.DragToEnemy
            ? this.touchStartScreenPos
            : this.dragStartScreenPos;
        bool t_inDeadZone = Vector2.Distance(t_releasePos, t_deadZoneOrigin) < this.deadZoneRadius;

        if (t_inDeadZone)
        {
            ClearTargetPreview();
            RestoreAllFades();
            FocusWeapon(false);
            this.cardAnim.MoveToSlot().Forget();
            this.dragState = DragState.Idle;
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
            OnAnyClicked?.Invoke(this);
        }
        this.dragState = DragState.Idle;
    }

    async UniTask WaitLongPress(CancellationToken _ct)
    {
        try
        {
            await UniTask.Delay((int)(GameTiming.Battle.LongPress * 1000), cancellationToken: _ct);
            if (this.boundCard == null || !this.boundCard.isRevealed) return;
            UIPoolManager.instance?.AddOrUpdateUI<PooledCardElement>(
                new PooledCardElementData { card = this.boundCard.data });
            this.longPressFired = true;
        }
        catch (OperationCanceledException) { }
    }

    void CancelLongPress()
    {
        this.longPressFired = false;
        this.longPressCts?.Cancel();
        this.longPressCts?.Dispose();
        this.longPressCts = null;
    }


    void ClearTargetPreview()
    {
        if (this.currentTarget != null)
        {
            this.currentTarget.SetHighlight(false);
            this.currentTarget.HideAttackPreview();
        }
        HideAttackPreview();
        this.currentTarget = null;
    }

    List<CardView> GetValidEnemyViews()
    {
        var t_enemies = new List<CardView>();
        foreach (CardView t_cv in allViews)
        {
            if (t_cv == this || t_cv.boundCard == null) continue;
            if (t_cv.boundCard.ownerIndex == this.boundCard?.ownerIndex) continue;
            t_enemies.Add(t_cv);
        }
        var t_taunt = t_enemies.FindAll(cv => cv.boundCard.HasKeyword(CardKeyword.Taunt));
        return t_taunt.Count > 0 ? t_taunt : t_enemies;
    }

    void UpdateTarget(Vector2 _aimDir)
    {
        var t_enemies = GetValidEnemyViews();

        CardView t_best = null;
        if (t_enemies.Count > 0)
        {
            t_enemies.Sort((a, b) => a.transform.position.x.CompareTo(b.transform.position.x));

            int t_idx;
            if (_aimDir.x > this.dirThreshold)
                t_idx = t_enemies.Count - 1;
            else if (_aimDir.x < -this.dirThreshold)
                t_idx = 0;
            else
                t_idx = t_enemies.Count / 2;

            t_best = t_enemies[t_idx];
        }

        if (this.currentTarget == t_best) return;

        if (this.currentTarget != null)
        {
            this.currentTarget.SetHighlight(false);
            this.currentTarget.HideAttackPreview();
            HideAttackPreview();
        }

        this.currentTarget = t_best;

        if (this.currentTarget != null)
        {
            this.currentTarget.SetHighlight(true);
            ShowPreviewForTarget(this.currentTarget);
        }
    }

    void UpdateTargetByCollider()
    {
        Vector3 t_screenPos = new Vector3(this.currentDragScreenPos.x, this.currentDragScreenPos.y, -Camera.main.transform.position.z);
        Vector2 t_worldPos = Camera.main.ScreenToWorldPoint(t_screenPos);
        Collider2D t_hit = Physics2D.OverlapPoint(t_worldPos);
        CardView t_best = null;

        if (t_hit != null)
        {
            CardView t_cv = t_hit.GetComponentInParent<CardView>();
            if (t_cv != null && t_cv != this)
            {
                var t_valid = GetValidEnemyViews();
                if (t_valid.Contains(t_cv))
                    t_best = t_cv;
            }
        }

        if (this.currentTarget == t_best) return;

        if (this.currentTarget != null)
        {
            this.currentTarget.SetHighlight(false);
            this.currentTarget.HideAttackPreview();
            HideAttackPreview();
        }

        this.currentTarget = t_best;
        if (this.currentTarget != null)
        {
            this.currentTarget.SetHighlight(true);
            ShowPreviewForTarget(this.currentTarget);
        }
    }

    void ShowPreviewForTarget(CardView _target)
    {
        CardInstance t_atk = this.boundCard;
        CardInstance t_def = _target.BoundCard;
        if (t_atk == null || t_def == null) return;

        bool t_defInvincible = t_def.HasKeyword(CardKeyword.Invincible);
        int  t_dmg = t_defInvincible ? 0 : t_atk.AttackDamage();
        _target.ShowAttackPreview(t_dmg, t_def.WouldDieFrom(t_atk.AttackDamage()));

        if (!t_atk.TakesCounterFrom(t_def)) return;

        bool t_atkInvincible = t_atk.HasKeyword(CardKeyword.Invincible);
        int  t_counter = t_atkInvincible ? 0 : t_def.AttackDamage();
        ShowAttackPreview(t_counter, t_atk.WouldDieFrom(t_def.AttackDamage()));
    }
    #endregion

    #region Visual State
    public void Render(CardInstance _card)
    {
        this.boundCard = _card;
        this.cardAnim.SetBoundCard(_card);
        bool t_isEmpty = _card == null;

        this.emptyOverlay.SetActive(t_isEmpty);
        SetHighlight(false);

        if (t_isEmpty)
        {
            this.faceDownOverlay.SetActive(false);
            SetupWeapon(null);
            RefreshKeywordIcons(CardKeyword.None);
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
        RefreshKeywordIcons(t_isFaceDown ? CardKeyword.None : (_card.data.keywords | _card.runtimeKeywords));
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

    public void ShowAttackPreview(int _damage, bool _wouldDie)
    {
        if (this.hpText == null || this.boundCard == null) return;
        this.hpTextOriginalColor = this.hpText.color;
        this.hpText.DOKill();
        (int t_hpAfter, int t_bonusAfter) = this.boundCard.PreviewAfterDamage(_damage);
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

    void RefreshKeywordIcons(CardKeyword _keywords)
    {
        if (this.keywordIconRoot == null || this.keywordIconPrefab == null || this.keywordIconConfig == null) return;

        foreach (Transform t_child in this.keywordIconRoot)
        {
            // 아이콘 스프라이트가 FadeView tween 대상일 수 있음. 파괴 전 DOKill (루트 SetLink는 안 걸림).
            foreach (SpriteRenderer t_sr in t_child.GetComponentsInChildren<SpriteRenderer>(true))
                t_sr.DOKill();
            Destroy(t_child.gameObject);
        }

        this.iconMap.Clear();

        if (_keywords == CardKeyword.None) return;

        var t_icons  = new List<Sprite>();
        var t_kwList = new List<CardKeyword>();
        foreach (CardKeyword t_kw in System.Enum.GetValues(typeof(CardKeyword)))
        {
            if (t_kw == CardKeyword.None) continue;
            if (!_keywords.HasFlag(t_kw)) continue;
            Sprite t_icon = this.keywordIconConfig.GetIcon(t_kw);
            if (t_icon != null) { t_icons.Add(t_icon); t_kwList.Add(t_kw); }
        }

        float t_startX = -(t_icons.Count - 1) * this.iconSpacing * 0.5f;
        for (int t_i = 0; t_i < t_icons.Count; t_i++)
        {
            GameObject t_obj = Instantiate(this.keywordIconPrefab, this.keywordIconRoot);
            t_obj.transform.localPosition = new Vector3(t_startX + t_i * this.iconSpacing, -1f, 0f);
            SpriteRenderer t_sr = t_obj.GetComponent<SpriteRenderer>();
            if (t_sr != null) t_sr.sprite = t_icons[t_i];
            this.iconMap[t_kwList[t_i]] = t_obj;
        }
    }

    public static CardView GetView(CardInstance _card)
    {
        foreach (CardView t_cv in allViews)
            if (t_cv.boundCard == _card) return t_cv;
        return null;
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
    public async UniTask PlayHitAnim(float _d = 0.15f)
    {
        if (this.boundCard != null)
            SetHpDisplay(this.boundCard.hp.ToString(), this.boundCard.bonusHp > 0 ? $"+{this.boundCard.bonusHp}" : "");
        await this.cardAnim.PlayHitAnim(_d);
    }
    public UniTask PlayDeathAnim(float _d = 0.4f)  => this.cardAnim.PlayDeathAnim(_d);
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
        if (TurnState.ForcedAttacker == null) return;
        FadeTeam(0.3f, TurnState.LocalOwnerIndex);
        CardView t_forced = GetView(TurnState.ForcedAttacker);
        if (t_forced != null) FadeCards(1f, t_forced);
    }

    public static void Cleanup()
    {
        OnAnyClicked  = null;
        OnAttack      = null;
        s_anyDragging = false;
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

        if (this.dragState == DragState.AttackDrag && currentInputMode == InputMode.DragBack)
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

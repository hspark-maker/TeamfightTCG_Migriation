using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class CurrencyHud : MonoBehaviour
{
    // 활성 HUD를 재화별로 찾는 창구. 같은 GameObject에 종류가 다른 HUD가 여러 장 붙어 있어
    // 타입 탐색(FindFirstObjectByType)으로는 어느 쪽이 잡힐지 보장되지 않는다.
    static readonly Dictionary<ECurrencyType, CurrencyHud> s_huds = new Dictionary<ECurrencyType, CurrencyHud>();

    [FormerlySerializedAs("goldText")]
    [SerializeField] TMP_Text valueText;

    // 표시할 재화 종류. 기본값이 Gold라 기존 골드 HUD 배선은 그대로 동작하고,
    // 다이아 등 다른 재화는 이 값만 바꿔 같은 컴포넌트를 재사용한다(연출 API도 종류를 따라간다).
    [SerializeField] ECurrencyType type = ECurrencyType.Gold;

    [Tooltip("재화 아이콘(옵션). CurrencyLook 표에 그림이 있을 때만 갈아낀다 — 비워두면 프리팹 그림 그대로다.")]
    [SerializeField] Image iconImage;

    [Tooltip("이 HUD를 그 재화의 대표(코인이 날아와 꽂히는 곳)로 등록할지.\n\n" +
             "화면당 대표는 재화별로 딱 한 장이고, 겹치면 마지막에 켜진 쪽이 이긴다. " +
             "그래서 로비 위에 잠깐 겹쳐 뜨는 화면(개봉 오버레이 등)에 잔액을 하나 더 두려면 " +
             "반드시 이 값을 꺼야 한다 — 켜 두면 그 화면이 닫히는 순간 대표 자리가 통째로 비어(본인 등록만 지운다) " +
             "로비 획득 연출의 코인이 날아갈 곳을 잃는다.\n\n" +
             "끈다고 잃는 것은 도착 지점 자격뿐이다. 잔액 표시·소모 롤다운·연출은 그대로 돈다.")]
    [SerializeField] bool registerAsPrimary = true;

    [Tooltip("배율 연출(획득 펄스·소비 눌림)이 물릴 노드. 미배선이면 이 컴포넌트가 붙은 노드(아이콘+숫자 묶음).\n" +
             "숫자 텍스트를 직접 물리지 말 것 — 그 rect는 LayoutGroup·ContentSizeFitter가 잡는 자식이라 " +
             "피벗이 묶음 한쪽으로 치우쳐 있고, 배율 축이 그 피벗이라 숫자가 옆으로 밀리듯 보인다.")]
    [SerializeField] RectTransform punchTarget;

    [Header("소모 연출")]
    [SerializeField, Min(0.01f)] float spendRollDuration = 0.55f;

    [Tooltip("소비 첫 박에 눌리는 깊이. 확대로 곧장 올라가지 않고 한 번 눌렀다 튀어야 '빠져나갔다'가 읽힌다.\n" +
             "1이면 눌림을 건너뛰고 바로 확대 배율로 올라간다.")]
    [SerializeField, Range(0.5f, 1f)] float spendPressScale = 0.92f;

    [Tooltip("롤다운이 도는 내내 유지할 확대 배율. 어느 재화가 빠지는 중인지 눈이 놓치지 않게 " +
             "이 HUD만 커진 채로 숫자가 굴러간다 — 롤이 끝나야 원래 크기로 내려온다.\n" +
             "1이면 커지지 않고 눌렸다 돌아오기만 한다.")]
    [SerializeField, Range(1f, 1.6f)] float spendHoldScale = 1.15f;

    [Tooltip("눌렸다 확대 배율까지 올라가는 데 걸리는 시간. 롤다운보다 훨씬 짧아야 '툭' 하는 한 박으로 들린다.")]
    [SerializeField, Min(0.01f)] float spendPressDuration = 0.14f;

    [Tooltip("롤이 끝나고 원래 크기로 내려오는 데 걸리는 시간. 1 아래로 살짝 지나갔다 돌아오며 '탁' 하고 멎는다.")]
    [SerializeField, Min(0.01f)] float spendReturnDuration = 0.2f;

    [Tooltip("롤다운이 도는 내내 숫자를 물들일 색. 롤이 끝나는 순간 원래 색으로 돌아온다.\n" +
             "반복 펄스를 대신하는 자리다 — 흔들지 않고도 '지금 줄고 있다'를 계속 말해 준다.")]
    [SerializeField] Color spendTint = new Color(1f, 0.42f, 0.35f);

    // 획득 연출 중에는 실제 잔액 대신 연출이 지정한 표시값을 보여준다(코인이 도착하며 숫자가 오르는 구간).
    bool m_held;
    long m_displayedValue;
    int m_displayRevision;
    Tweener m_spendTween;
    long m_spendTarget;
    Tween m_spendMotion;
    Color m_baseTextColor = Color.white;
    bool m_tinted;

    /// <summary>수치 텍스트의 RectTransform. 코인이 날아와 꽂히는 **도착 지점**이다.</summary>
    public RectTransform TextRect => this.valueText != null ? (RectTransform)this.valueText.transform : null;

    /// <summary>이 HUD가 맡은 재화. 결제 재화에 맞는 잔액만 띄우려는 화면이 본다.</summary>
    public ECurrencyType Type => this.type;

    /// <summary>펄스로 튀길 노드. 도착 지점과 갈라 둔다 — 코인은 숫자에 꽂혀야 하지만,
    /// 튀는 것은 아이콘까지 묶은 덩어리여야 축이 그 한가운데에 선다.</summary>
    RectTransform PunchRect => this.punchTarget != null ? this.punchTarget
                             : transform is RectTransform t_self ? t_self
                             : this.TextRect;

    /// <summary>해당 재화의 활성 HUD를 얻는다. 꺼져 있거나 없으면 false(그 재화 연출만 건너뛰면 된다).</summary>
    public static bool TryGet(ECurrencyType _type, out CurrencyHud _hud)
    {
        if (!s_huds.TryGetValue(_type, out _hud)) return false;

        // 파괴됐는데 OnDisable이 오지 않은 잔재를 여기서 걷는다.
        if (_hud == null)
        {
            s_huds.Remove(_type);
            return false;
        }

        return true;
    }

    /// <summary>
    /// 획득 연출용 숫자 롤업을 시작하고, 코인 도착 콜백(도착 장수, 전체 장수)에 물릴 진행 핸들러를 돌려준다.
    /// 잔액이 이미 최종값이라는 전제 — 지급·저장이 끝난 뒤에 부른다(획득분만큼 되돌렸다가 도착에 맞춰 다시 올린다).
    /// 연출이 끊겨도 고정이 풀리도록 호출부는 반환된 해제 콜백을 시퀀스 OnKill에 걸어둘 것.
    /// </summary>
    public Action<int, int> BeginGainRollUp(long _gain, out Action _release,
                                            float _punch = UiPunch.DEFAULT_SCALE)
    {
        long t_target = CurrencyManager.GetBalance(this.type);
        long t_start = m_held ? m_displayedValue : t_target - _gain;

        this.KillSpendTween();
        int t_revision = ++m_displayRevision;
        this.HoldDisplay(t_start);

        _release = () => this.ReleaseDisplay(t_revision);

        return (_arrived, _total) =>
        {
            if (t_revision != m_displayRevision) return;

            if (_total <= 0 || _arrived >= _total) this.ReleaseDisplay(t_revision);
            else this.HoldDisplay(t_start + (long)((t_target - t_start) * (_arrived / (float)_total)));

            UiPunch.Play(this.PunchRect, _punch);
        };
    }

    /// <summary>표시값을 연출용으로 고정한다. 실제 잔액 변경은 ReleaseDisplay까지 화면에 반영되지 않는다.</summary>
    void HoldDisplay(long _value)
    {
        m_held = true;
        this.Render(_value);
    }

    /// <summary>고정을 풀고 실제 잔액으로 되돌린다.</summary>
    void ReleaseDisplay(int _revision)
    {
        if (_revision != m_displayRevision) return;

        m_displayRevision++;
        m_held = false;
        this.Render(CurrencyManager.GetBalance(this.type));
    }

    void Awake()
    {
        if (this.valueText == null) this.valueText = GetComponent<TMP_Text>();

        // 물들이기 전 색을 여기서 잡아 둔다 — 연출 도중 잡으면 소비 색이 기준색으로 굳는다.
        if (this.valueText != null) m_baseTextColor = this.valueText.color;

        // 프리팹을 복제해 type만 바꾼 HUD(조각 등)가 그림까지 따라오게 한다.
        if (this.iconImage != null)
        {
            Sprite t_icon = CurrencyLook.IconOf(this.type);
            if (t_icon != null) this.iconImage.sprite = t_icon;
        }
    }

    void OnEnable()
    {
        // 종류별 1장. 같은 종류가 겹치면 마지막이 이긴다(예외 없이).
        // 종속 표시(registerAsPrimary=false)는 이 자리를 넘보지 않는다 — 자세한 이유는 그 필드 툴팁.
        if (this.registerAsPrimary) s_huds[this.type] = this;

        // 활성화 시점의 실제 잔액으로 먼저 맞춘 뒤 이후 변경을 구독.
        CurrencyManager.OnCurrencyChanged += this.HandleCurrencyChanged;
        CurrencyManager.OnCurrencySpent += this.HandleCurrencySpent;
        this.Render(CurrencyManager.GetBalance(this.type));
    }

    void OnDisable()
    {
        // 씬 전환은 새 HUD의 OnEnable이 먼저 도는 순서가 가능하다 — 본인일 때만 지워야 새 등록을 밟지 않는다.
        if (s_huds.TryGetValue(this.type, out var t_cur) && t_cur == this) s_huds.Remove(this.type);

        CurrencyManager.OnCurrencyChanged -= this.HandleCurrencyChanged;
        CurrencyManager.OnCurrencySpent -= this.HandleCurrencySpent;

        m_displayRevision++;
        this.KillSpendTween();
        this.KillSpendMotion();
        this.ClearTint(false);
        if (this.PunchRect != null) this.PunchRect.DOComplete();
        // 연출 도중 꺼지면 해제 호출이 오지 않는다 — 고정을 여기서 풀어 다음 활성화가 잔액을 못 따라가는 상태를 막는다.
        m_held = false;
    }

    void HandleCurrencySpent(ECurrencyType _type, long _cost, long _balance)
    {
        if (_type != this.type) return;

        this.BeginSpendRollDown(_cost, _balance);
    }

    // 이 HUD가 맡은 재화의 변경만 반영. 다른 종류는 무시.
    void HandleCurrencyChanged(ECurrencyType _type, long _balance)
    {
        if (_type != this.type) return;

        if (m_spendTween != null && m_spendTween.IsActive())
        {
            if (_balance != m_spendTarget)
            {
                m_spendTarget = _balance;
                m_spendTween.ChangeEndValue(_balance, snapStartValue: true);
            }
            return;
        }

        if (m_held) return;
        this.Render(_balance);
    }

    void BeginSpendRollDown(long _cost, long _balance)
    {
        long t_start = m_displayedValue;
        this.KillSpendTween();

        int t_revision = ++m_displayRevision;
        long t_value = Math.Max(t_start, _balance + _cost);
        this.HoldDisplay(t_value);
        m_spendTarget = _balance;

        float t_duration = Mathf.Max(0.01f, this.spendRollDuration);

        // 소비는 두 박이다 — 시작에 '툭'(눌렀다 확대 배율로 튀어 오름), 끝에 '탁'(원래 크기로 내려앉음).
        // 그 사이는 커진 채로 색만 물들여 둔다. 롤다운 자체가 이미 "줄고 있다"를 말하고 있어서
        // 중간에 배율을 더 흔들면 정보가 아니라 노이즈가 된다.
        this.PlayLift();
        this.ApplyTint();

        Tweener t_tween = DOTween.To(() => t_value,
                                    _value =>
                                    {
                                        t_value = _value;
                                        if (t_revision == m_displayRevision) this.Render(_value);
                                    },
                                    _balance, t_duration)
                                .SetEase(Ease.OutCubic)
                                .SetLink(gameObject);

        t_tween.OnComplete(() => this.FinishSpendRollDown(t_revision, _settle: true));
        // 끝까지 돈 뒤에도 OnKill이 이어 오지만, 위 호출이 이미 리비전을 올려 두어 여기서 막힌다.
        // 즉 이 경로가 잡는 것은 '중간에 끊긴 롤' 뿐이고, 그때는 정착 tick 없이 색만 되돌린다.
        t_tween.OnKill(() => this.FinishSpendRollDown(t_revision, _settle: false));
        m_spendTween = t_tween;
    }

    void FinishSpendRollDown(int _revision, bool _settle)
    {
        if (_revision != m_displayRevision) return;

        m_spendTween = null;
        this.ClearTint(_settle);
        // 끊긴 롤은 내려오는 연출 없이 크기를 즉시 되돌린다 — 커진 채로 굳는 것만은 막아야 한다.
        if (_settle) this.PlaySettle();
        else this.KillSpendMotion();
        this.ReleaseDisplay(_revision);
    }

    // 소비 첫 박. 안으로 한 번 눌렸다가 확대 배율까지 튀어 올라 **그대로 머문다** —
    // 롤이 도는 내내 이 HUD만 커져 있어야 어느 재화가 빠지는 중인지 눈이 놓치지 않는다.
    void PlayLift()
    {
        RectTransform t_rect = this.PunchRect;
        if (t_rect == null) return;

        this.KillSpendMotion();

        float t_duration = Mathf.Max(0.01f, this.spendPressDuration);
        float t_hold = Mathf.Max(1f, this.spendHoldScale);
        Sequence t_motion = DOTween.Sequence().SetLink(t_rect.gameObject);

        if (this.spendPressScale < 1f)
            t_motion.Append(t_rect.DOScale(this.spendPressScale, t_duration * 0.35f).SetEase(Ease.OutQuad));

        t_motion.Append(t_rect.DOScale(t_hold, t_duration * 0.65f).SetEase(Ease.OutBack));
        m_spendMotion = t_motion;
    }

    // 롤이 목표값에 닿는 순간 원래 크기로 내려앉는다.
    // OutBack이 1 아래를 살짝 지나갔다 돌아와서, 따로 tick을 쏘지 않아도 '탁' 하고 멎는 소리가 난다.
    void PlaySettle()
    {
        RectTransform t_rect = this.PunchRect;
        if (t_rect == null) return;

        // 배율을 되돌리지 않고 끈다 — 커져 있는 지금 크기가 이 트윈의 출발점이다.
        this.KillSpendMotion(_resetScale: false);

        m_spendMotion = t_rect.DOScale(1f, Mathf.Max(0.01f, this.spendReturnDuration))
                              .SetEase(Ease.OutBack)
                              .SetLink(t_rect.gameObject);
    }

    void ApplyTint()
    {
        if (this.valueText == null) return;

        this.valueText.DOKill();
        this.valueText.color = this.spendTint;
        m_tinted = true;
    }

    /// <summary>물들인 색을 되돌린다. 정상 종료면 한 박에 걸쳐 풀고, 끊긴 경우엔 즉시 되돌린다.</summary>
    void ClearTint(bool _fade)
    {
        if (!m_tinted) return;

        m_tinted = false;
        if (this.valueText == null) return;

        this.valueText.DOKill();
        if (_fade && this.isActiveAndEnabled)
            this.valueText.DOColor(m_baseTextColor, 0.18f).SetLink(this.valueText.gameObject);
        else
            this.valueText.color = m_baseTextColor;
    }

    void KillSpendTween()
    {
        Tweener t_tween = m_spendTween;
        m_spendTween = null;
        if (t_tween != null && t_tween.IsActive()) t_tween.Kill();
    }

    // 배율 연출을 걷는다. 기본은 기준 배율 복귀 — 확대·눌림이 겹쳐도 크기가 그 상태로 굳지 않게.
    // 이어서 지금 크기부터 트윈할 때만 _resetScale을 꺼서 출발점을 남긴다.
    void KillSpendMotion(bool _resetScale = true)
    {
        Tween t_motion = m_spendMotion;
        m_spendMotion = null;
        if (t_motion != null && t_motion.IsActive()) t_motion.Kill();

        if (!_resetScale) return;

        RectTransform t_rect = this.PunchRect;
        if (t_rect != null) t_rect.localScale = Vector3.one;
    }

    // 천단위 콤마 포맷
    void Render(long _amount)
    {
        m_displayedValue = _amount;
        if (this.valueText == null) return;
        this.valueText.text = _amount.ToString("N0");
    }
}

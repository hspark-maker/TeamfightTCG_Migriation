using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;

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

    [Tooltip("펄스로 튀길 노드. 미배선이면 이 컴포넌트가 붙은 노드(아이콘+숫자 묶음).\n" +
             "숫자 텍스트를 직접 물리지 말 것 — 그 rect는 LayoutGroup·ContentSizeFitter가 잡는 자식이라 " +
             "피벗이 묶음 한쪽으로 치우쳐 있고, 배율 축이 그 피벗이라 숫자가 옆으로 밀리듯 보인다.")]
    [SerializeField] RectTransform punchTarget;

    [Header("소모 연출")]
    [SerializeField, Min(0.01f)] float spendRollDuration = 0.55f;
    [SerializeField, Min(1)] int spendPulseCount = 3;
    [SerializeField] float spendPunch = UiPunch.DEFAULT_SCALE;

    // 획득 연출 중에는 실제 잔액 대신 연출이 지정한 표시값을 보여준다(코인이 도착하며 숫자가 오르는 구간).
    bool m_held;
    long m_displayedValue;
    int m_displayRevision;
    Tweener m_spendTween;
    long m_spendTarget;

    /// <summary>수치 텍스트의 RectTransform. 코인이 날아와 꽂히는 **도착 지점**이다.</summary>
    public RectTransform TextRect => this.valueText != null ? (RectTransform)this.valueText.transform : null;

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
    }

    void OnEnable()
    {
        // 종류별 1장. 같은 종류가 겹치면 마지막이 이긴다(예외 없이).
        s_huds[this.type] = this;

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
        int t_pulses = Mathf.Max(1, this.spendPulseCount);

        // 펄스는 숫자가 줄어드는 구간과 **정확히 같은 길이**를 차지해야 한다.
        // 그래서 i번째 펄스를 i/n 지점에 쏘고(첫 발은 0초 = 숫자가 움직이기 시작하는 프레임),
        // 펀치 길이도 한 칸(duration/n)으로 맞춘다 → 마지막 펄스가 끝나는 순간이 곧 롤의 끝이다.
        // (진행률에 floor만 쓰면 첫 발이 1/n만큼 늦고, 마지막 발이 100% 지점에서 시작해 롤보다 오래 남는다.)
        float t_interval = t_duration / t_pulses;
        int   t_pulseIndex = 1;
        UiPunch.Play(this.PunchRect, this.spendPunch, t_interval);

        Tweener t_tween = DOTween.To(() => t_value,
                                    _value =>
                                    {
                                        t_value = _value;
                                        if (t_revision == m_displayRevision) this.Render(_value);
                                    },
                                    _balance, t_duration)
                                .SetEase(Ease.OutCubic)
                                .SetLink(gameObject);
        t_tween.OnUpdate(() =>
        {
            if (t_revision != m_displayRevision) return;

            int t_reached = Mathf.Min(t_pulses,
                                      Mathf.FloorToInt(t_tween.ElapsedPercentage() * t_pulses) + 1);
            while (t_pulseIndex < t_reached)
            {
                t_pulseIndex++;
                UiPunch.Play(this.PunchRect, this.spendPunch, t_interval);
            }
        });

        t_tween.OnComplete(() => this.FinishSpendRollDown(t_revision));
        t_tween.OnKill(() => this.FinishSpendRollDown(t_revision));
        m_spendTween = t_tween;
    }

    void FinishSpendRollDown(int _revision)
    {
        if (_revision != m_displayRevision) return;

        m_spendTween = null;
        this.ReleaseDisplay(_revision);
    }

    void KillSpendTween()
    {
        Tweener t_tween = m_spendTween;
        m_spendTween = null;
        if (t_tween != null && t_tween.IsActive()) t_tween.Kill();
    }

    // 천단위 콤마 포맷
    void Render(long _amount)
    {
        m_displayedValue = _amount;
        if (this.valueText == null) return;
        this.valueText.text = _amount.ToString("N0");
    }
}

using System;
using DG.Tweening;
using UnityEngine;

// 등급 안 진행(0~1)을 그리는 게이지의 계약.
// 값·트윈·마디 통과 판정은 여기서 관리하고, 구현체는 "비율을 어떻게 그리는가"와 "마디를 어디에 앉히는가"만 정한다 —
// 게이지 그림을 갈아끼울 때 별과의 싱크가 함께 갈리지 않게 하려는 분리다(별 점등의 유일한 방아쇠가 이 통지다).
public abstract class RankProgressGauge : MonoBehaviour
{
    [Tooltip("차오르고 물러나는 손맛. 게이지 모양마다 어울리는 곡선이 다를 수 있어 열어 둔다.")]
    [SerializeField] Ease tweenEase = Ease.OutQuad;

    Tween m_tween;

    // 마지막으로 세운 비율. 통과 판정의 기준점이라 SetRatio(무통지 스냅)도 이 값을 갱신한다.
    float m_lastRatio;

    // 통과를 감시할 비율들(오름차순 전제)과 통지 대상
    float[] m_thresholds;
    Action<int, bool> m_onCross;

    /// <summary>지금 세워 둔 비율(0~1).</summary>
    public float Ratio => this.m_lastRatio;

    /// <summary>채움 비율 _ratio 지점의 자리(부모 로컬 anchoredPosition). 마디(별·승급 마커)를 여기에 앉힌다.</summary>
    public abstract Vector2 MarkerPos(float _ratio);

    /// <summary>마디 통과를 통지받는다. _ratios는 오름차순, 통지는 (인덱스, 전진여부).
    /// <b>TweenTo만 통지한다</b> — SetRatio는 연출이 과거로 되감는 자리라 통지하면 마디가 우수수 꺼진다.</summary>
    public void SetThresholds(float[] _ratios, Action<int, bool> _onCross)
    {
        this.m_thresholds = _ratios;
        this.m_onCross = _onCross;
    }

    /// <summary>즉시 _ratio(0~1)로 세운다. 돌던 트윈은 걷는다.</summary>
    public void SetRatio(float _ratio)
    {
        this.Stop();

        this.m_lastRatio = Mathf.Clamp01(_ratio);
        this.ApplyRatio(this.m_lastRatio);
    }

    /// <summary>_ratio까지 차오르는(물러나는) 트윈. 시퀀스에 Join해도 되고 그대로 둬도 된다.
    /// 도는 동안 지나친 마디를 통지한다 — 별은 이 통지로만 켜진다.</summary>
    public Tween TweenTo(float _ratio, float _duration)
    {
        this.Stop();

        // 그림이 아니라 비율 자체를 굴린다 — 구현체가 무엇을 어떻게 그리든 같은 통지가 나가야 한다.
        float t_value  = this.m_lastRatio;
        float t_target = Mathf.Clamp01(_ratio);

        this.m_tween = DOTween.To(() => t_value, _x => { t_value = _x; this.Advance(_x); }, t_target, _duration)
                              .SetEase(this.tweenEase)
                              .SetLink(this.gameObject)
                              // 도착 지점에 정확히 놓인 마디가 부동소수 오차로 새는 것을 막는다(이미 지났으면 무시된다).
                              .OnComplete(() => this.Advance(t_target))
                              .OnKill(() => this.m_tween = null);
        return this.m_tween;
    }

    public void Stop()
    {
        if (this.m_tween == null) return;

        this.m_tween.Kill();
        this.m_tween = null;
    }

    /// <summary>_ratio(0~1)를 그림에 반영한다. 구현체가 정하는 유일한 그리기 지점.</summary>
    protected abstract void ApplyRatio(float _ratio);

    // 그린 다음에 통지한다 — 별이 켜지는 프레임과 채움 머리가 그 자리에 오는 프레임이 같아야 한다.
    void Advance(float _ratio)
    {
        this.ApplyRatio(Mathf.Clamp01(_ratio));
        this.NotifyCrossings(_ratio);
    }

    // 이전 비율과 _ratio 사이를 지난 마디를 전부 통지한다(한 프레임에 여럿을 뛰어넘어도 빠뜨리지 않는다).
    void NotifyCrossings(float _ratio)
    {
        float t_prev = this.m_lastRatio;
        float t_cur  = Mathf.Clamp01(_ratio);
        this.m_lastRatio = t_cur;

        if (this.m_thresholds == null || this.m_onCross == null || t_prev == t_cur) return;

        bool t_forward = t_cur > t_prev;

        // 전진은 오름차순, 후퇴는 내림차순으로 훑는다 — 통지 순서가 곧 화면에 켜지고 꺼지는 순서다.
        for (int t_i = 0; t_i < this.m_thresholds.Length; t_i++)
        {
            int t_index = t_forward ? t_i : this.m_thresholds.Length - 1 - t_i;
            float t_th  = this.m_thresholds[t_index];

            bool t_crossed = t_forward ? (t_prev < t_th && t_cur >= t_th)
                                       : (t_prev >= t_th && t_cur < t_th);
            if (t_crossed) this.m_onCross(t_index, t_forward);
        }
    }
}

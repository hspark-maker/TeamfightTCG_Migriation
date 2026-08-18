using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 배지를 감싸는 진행 호 — 등급 안 4단계를 통틀어 0~1로 채운다.
// 링 스프라이트를 통째로 그리지 않고 fillAmount 상한(Span)이 위쪽 호만 남긴다.
// 호 위 마디(단계 별·승급 마커)의 자리와 통과 판정도 여기서 낸다 — 기하가 두 곳에 있으면
// 손으로 찍은 좌표가 span·회전이 바뀌는 순간 반드시 어긋난다(별이 켜지는 지점과 별이 앉은 지점이 갈린다).
public class RankProgressArc : MonoBehaviour
{
    [Tooltip("아직 안 채워진 구간의 밑판. Image Type=Filled / FillMethod=Radial360 / fillOrigin=Top 전제.")]
    [SerializeField] Image track;

    [Tooltip("채워지는 부분. track과 같은 스프라이트를 같은 자리에 겹쳐 둔다.")]
    [SerializeField] Image fill;

    [Tooltip("호가 그리는 각도. 마디 자리는 이 값에서 파생하므로 바꿔도 별과 칸이 어긋나지 않는다.\n" +
             "호를 위쪽 중심에 세우는 회전도 Awake가 이 값에서 파생한다 — 인스턴스에서 회전을 오버라이드하면 " +
             "에디터에 보이는 그림만 달라지고 런타임엔 덮인다.")]
    [SerializeField] float spanDegrees = 166.6f;

    [Tooltip("마디가 앉을 원의 반지름(부모 좌표계 px). 기본은 호 사각형의 반쪽 = 링 바깥선.")]
    [SerializeField] float markerRadius = 150f;

    Tween m_tween;

    // 마지막으로 세운 채움 비율. 통과 판정의 기준점이라 SetRatio(무통지 스냅)도 이 값을 갱신한다.
    float m_lastRatio;

    // 통과를 감시할 비율들(오름차순 전제)과 통지 대상
    float[] m_thresholds;
    Action<int, bool> m_onCross;

    // 호가 차지하는 fillAmount 범위(1 = 원 한 바퀴)
    float Span => Mathf.Clamp01(this.spanDegrees / 360f);

    /// <summary>채움 비율 _ratio 지점의 자리(부모 로컬 anchoredPosition). 호와 마디는 같은 앵커·피벗(0.5)을 쓴다.</summary>
    public Vector2 MarkerPos(float _ratio)
    {
        // 호가 위쪽 중심에 서 있으므로 비율 0.5가 정북이다. 위쪽 0, 시계방향 +.
        float t_rad = (Mathf.Clamp01(_ratio) - 0.5f) * this.spanDegrees * Mathf.Deg2Rad;
        Vector2 t_center = ((RectTransform)this.transform).anchoredPosition;

        return new Vector2(t_center.x + this.markerRadius * Mathf.Sin(t_rad),
                           t_center.y + this.markerRadius * Mathf.Cos(t_rad));
    }

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
        if (this.fill != null) this.fill.fillAmount = this.Span * this.m_lastRatio;
    }

    /// <summary>_ratio까지 차오르는 트윈. 시퀀스에 Join해도 되고 그대로 둬도 된다.
    /// 도는 동안 지나친 마디를 통지한다 — 별은 이 통지로만 켜진다.</summary>
    public Tween TweenTo(float _ratio, float _duration)
    {
        this.Stop();
        if (this.fill == null) return null;

        float t_span   = this.Span;
        float t_target = Mathf.Clamp01(_ratio);

        this.m_tween = this.fill.DOFillAmount(t_span * t_target, _duration)
                                .SetEase(Ease.OutQuad)
                                .SetLink(this.gameObject)
                                .OnUpdate(() => this.NotifyCrossings(t_span > 0f ? this.fill.fillAmount / t_span : t_target))
                                // 도착 지점에 정확히 놓인 마디가 부동소수 오차로 새는 것을 막는다(이미 통지됐으면 무시된다).
                                .OnComplete(() => this.NotifyCrossings(t_target))
                                .OnKill(() => this.m_tween = null);
        return this.m_tween;
    }

    public void Stop()
    {
        if (this.m_tween == null) return;

        this.m_tween.Kill();
        this.m_tween = null;
    }

    void Awake()
    {
        // 회전이 곧 시작각이다 — 링은 회전 대칭이라 스프라이트가 돌아가도 그림은 같다.
        // Radial360은 fillOrigin=Top에서만 시작해서, 호를 위쪽 중심에 세우려면 절반만큼 되돌려 놓는 수밖에 없다.
        this.transform.localEulerAngles = new Vector3(0f, 0f, this.spanDegrees * 0.5f);

        if (this.track != null) this.track.fillAmount = this.Span;
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

using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 배지를 감싸는 진행 호 — 등급 안 4단계를 통틀어 0~1로 채운다.
// 링 스프라이트를 통째로 그리지 않고 fillAmount 상한(Span)이 위쪽 호만 남긴다.
// 별 넷은 이미 이 원 위에 41.7° 간격으로 얹혀 있어, 166.7°를 4등분하면 별 하나가 각 칸의 정중앙에 온다
// (= 채움 머리가 K번 칸에 있다 ⟺ K단계 ⟺ K번 별이 켜져 있다).
public class RankProgressArc : MonoBehaviour
{
    [Tooltip("아직 안 채워진 구간의 밑판. Image Type=Filled / FillMethod=Radial360 / fillOrigin=Top 전제.")]
    [SerializeField] Image track;

    [Tooltip("채워지는 부분. track과 같은 스프라이트를 같은 자리에 겹쳐 둔다.")]
    [SerializeField] Image fill;

    [Tooltip("호가 그리는 각도. 별 간격이 41.7°라 4칸 × 41.7° = 166.7°이면 별 넷이 각 칸의 정중앙에 온다.\n" +
             "이 값을 바꾸면 별과 칸이 어긋난다. 호를 위쪽 중심에 세우는 회전은 Awake가 이 값에서 파생한다.")]
    [SerializeField] float spanDegrees = 166.7f;

    Tween m_tween;

    // 호가 차지하는 fillAmount 범위(1 = 원 한 바퀴)
    float Span => Mathf.Clamp01(this.spanDegrees / 360f);

    /// <summary>즉시 _ratio(0~1)로 세운다. 돌던 트윈은 걷는다.</summary>
    public void SetRatio(float _ratio)
    {
        this.Stop();
        if (this.fill != null) this.fill.fillAmount = this.Span * Mathf.Clamp01(_ratio);
    }

    /// <summary>_ratio까지 차오르는 트윈. 시퀀스에 Join해도 되고 그대로 둬도 된다.</summary>
    public Tween TweenTo(float _ratio, float _duration)
    {
        this.Stop();
        if (this.fill == null) return null;

        this.m_tween = this.fill.DOFillAmount(this.Span * Mathf.Clamp01(_ratio), _duration)
                                .SetEase(Ease.OutQuad)
                                .SetLink(this.gameObject)
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
}

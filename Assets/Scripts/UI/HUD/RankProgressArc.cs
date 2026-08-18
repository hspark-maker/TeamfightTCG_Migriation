using UnityEngine;
using UnityEngine.UI;

// 게이지 구현 한 종류 — 배지를 감싸는 링을 fillAmount로 채운다.
// 링 스프라이트를 통째로 그리지 않고 fillAmount 상한(Span)이 위쪽 호만 남긴다.
// 값·트윈·마디 통과 판정은 베이스(RankProgressGauge)가 맡는다. 여기 있는 것은 "어떻게 그리는가"와 "마디가 어디에 앉는가"뿐이다.
public class RankProgressArc : RankProgressGauge
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

    // 호가 차지하는 fillAmount 범위(1 = 원 한 바퀴)
    float Span => Mathf.Clamp01(this.spanDegrees / 360f);

    public override Vector2 MarkerPos(float _ratio)
    {
        // 호가 위쪽 중심에 서 있으므로 비율 0.5가 정북이다. 위쪽 0, 시계방향 +.
        float t_rad = (Mathf.Clamp01(_ratio) - 0.5f) * this.spanDegrees * Mathf.Deg2Rad;
        Vector2 t_center = ((RectTransform)this.transform).anchoredPosition;

        return new Vector2(t_center.x + this.markerRadius * Mathf.Sin(t_rad),
                           t_center.y + this.markerRadius * Mathf.Cos(t_rad));
    }

    protected override void ApplyRatio(float _ratio)
    {
        if (this.fill != null) this.fill.fillAmount = this.Span * _ratio;
    }

    void Awake()
    {
        // 회전이 곧 시작각이다 — 링은 회전 대칭이라 스프라이트가 돌아가도 그림은 같다.
        // Radial360은 fillOrigin=Top에서만 시작해서, 호를 위쪽 중심에 세우려면 절반만큼 되돌려 놓는 수밖에 없다.
        this.transform.localEulerAngles = new Vector3(0f, 0f, this.spanDegrees * 0.5f);

        if (this.track != null) this.track.fillAmount = this.Span;
    }
}

using UnityEngine;

// 게이지 구현 한 종류 — 채움 사각의 폭을 줄여 진행을 그린다(마디 없이 이어지는 막대).
// 폭을 굴리는 이유는 9-slice 보존이다. Image Type=Filled는 9-slice를 무시하고 스프라이트를 통짜로 늘려,
// 층마다 다른 Pixels Per Unit Multiplier로 테두리 두께를 만든 저작(로비 설정판의 레벨 게이지)을 무너뜨린다.
// 자식 층은 스트레치 앵커로 부모 폭을 따라오게 저작한다.
// 값·트윈·마디 통과 판정은 베이스(RankProgressGauge)가 맡는다.
public class BarProgressGauge : RankProgressGauge
{
    [Tooltip("폭이 줄고 느는 채움 사각. pivot·anchor의 x가 0이어야 왼쪽에서 자란다. " +
             "저작 폭이 곧 100% 폭이라, 트랙과 같은 폭으로 저작한다.")]
    [SerializeField] RectTransform fillRect;

    // 저작된 100% 폭. Awake에 붙든다 — 이후에는 폭 자체가 진행을 담는 값이라 여기서 되물을 수 없다.
    float m_fullWidth;

    /// <summary>저작된 100% 폭(px). 미배선이면 0.</summary>
    public float FullWidth => this.m_fullWidth;

    public override Vector2 MarkerPos(float _ratio)
    {
        if (this.fillRect == null) return Vector2.zero;

        Vector2 t_pos = this.fillRect.anchoredPosition;
        return new Vector2(t_pos.x + this.m_fullWidth * Mathf.Clamp01(_ratio), t_pos.y);
    }

    protected override void ApplyRatio(float _ratio)
    {
        if (this.fillRect == null) return;

        Vector2 t_size = this.fillRect.sizeDelta;
        this.fillRect.sizeDelta = new Vector2(this.m_fullWidth * Mathf.Clamp01(_ratio), t_size.y);
    }

    void Awake()
    {
        if (this.fillRect == null) this.fillRect = transform as RectTransform;
        if (this.fillRect != null) this.m_fullWidth = this.fillRect.sizeDelta.x;
    }
}

using UnityEngine;

// 게이지 구현 한 종류 — 채움 사각의 폭을 줄여 진행을 그린다(마디 없이 이어지는 막대).
// 폭을 굴리는 이유는 9-slice 보존이다. Image Type=Filled는 9-slice를 무시하고 스프라이트를 통짜로 늘려,
// 층마다 다른 Pixels Per Unit Multiplier로 테두리 두께를 만든 저작(로비 설정판의 레벨 게이지)을 무너뜨린다.
// 자식 층은 스트레치 앵커로 부모 폭을 따라오게 저작한다.
// 값·트윈·마디 통과 판정은 베이스(RankProgressGauge)가 맡는다.
public class BarProgressGauge : RankProgressGauge
{
    [Tooltip("폭이 줄고 느는 채움 사각. pivot·anchor의 x가 0이어야 왼쪽에서 자란다. " +
             "비워 두면 자기 RectTransform을 쓴다.")]
    [SerializeField] RectTransform fillRect;

    [Tooltip("100%일 때의 폭(px). 0이면 처음 그릴 때의 폭을 기준으로 붙든다. " +
             "저작해 두면 그 값이 이긴다 — 배선을 눈으로 확인할 수 있어 권장한다.")]
    [SerializeField] float fullWidth;

    /// <summary>100%일 때의 폭(px). 아직 확정되지 않았으면 0.</summary>
    public float FullWidth => this.fullWidth;

    public override Vector2 MarkerPos(float _ratio)
    {
        RectTransform t_rect = Rect;
        if (t_rect == null) return Vector2.zero;

        EnsureFullWidth(t_rect);

        Vector2 t_pos = t_rect.anchoredPosition;
        return new Vector2(t_pos.x + this.fullWidth * Mathf.Clamp01(_ratio), t_pos.y);
    }

    protected override void ApplyRatio(float _ratio)
    {
        RectTransform t_rect = Rect;
        if (t_rect == null) return;

        EnsureFullWidth(t_rect);

        Vector2 t_size = t_rect.sizeDelta;
        t_rect.sizeDelta = new Vector2(this.fullWidth * Mathf.Clamp01(_ratio), t_size.y);
    }

    // 배선을 Awake에 두지 않는다 — 부모의 OnEnable이 자식의 Awake보다 먼저 돌 수 있어,
    // 그 사이에 그리기가 들어오면 미배선 상태로 새어 나간다.
    RectTransform Rect
    {
        get
        {
            if (this.fillRect == null) this.fillRect = transform as RectTransform;
            return this.fillRect;
        }
    }

    // 기준 폭을 처음 그리기 직전에 확정한다. 폭을 줄이는 것은 이 컴포넌트뿐이라 이 시점의 값이 곧 저작 폭이다.
    // Awake에서 읽으면 그보다 먼저 도는 OnEnable이 이미 폭을 0으로 만든 뒤일 수 있고,
    // 그 0을 기준으로 붙들면 게이지가 영영 되살아나지 않는다.
    void EnsureFullWidth(RectTransform _rect)
    {
        if (this.fullWidth > 0f) return;

        this.fullWidth = _rect.sizeDelta.x;
        if (this.fullWidth <= 0f)
            Debug.LogError($"[{name}] 게이지 기준 폭이 0이다 — 채움 사각의 저작 폭을 확인할 것.", this);
    }
}

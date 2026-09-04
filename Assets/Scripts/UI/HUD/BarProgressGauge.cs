using UnityEngine;

// 게이지 구현 한 종류 — 채움 사각의 폭을 줄여 진행을 그린다(마디 없이 이어지는 막대).
// 폭을 굴리는 이유는 9-slice 보존이다. Image Type=Filled는 9-slice를 무시하고 스프라이트를 통짜로 늘려,
// 층마다 다른 Pixels Per Unit Multiplier로 테두리 두께를 만든 저작(로비 설정판의 레벨 게이지)을 무너뜨린다.
// 자식 층은 스트레치 앵커로 부모 폭을 따라오게 저작한다.
//
// 100%의 기준은 저작된 숫자가 아니라 기준 사각의 실제 폭이다 — 채움 사각의 폭은 이 컴포넌트가 매번 덮으므로
// 자기 폭을 기준으로 삼으면 기준과 결과가 같은 칸을 쓰게 되고, 그 칸을 캐시하는 순간 저작과 갈린다.
// 값·트윈·마디 통과 판정은 베이스(RankProgressGauge)가 맡는다.
public class BarProgressGauge : RankProgressGauge
{
    [Tooltip("폭이 줄고 느는 채움 사각. pivot·anchor의 x가 0이어야 왼쪽에서 자란다. " +
             "비워 두면 자기 RectTransform을 쓴다.")]
    [SerializeField] RectTransform fillRect;

    [Tooltip("100% 자리를 정하는 기준 사각. 채움 사각이 이 폭까지 자란다. 비워 두면 채움 사각의 부모를 쓴다.\n" +
             "고정 폭 저작을 전제한다 — 레이아웃이 아직 서지 않아 폭이 0인 사각을 꽂으면 게이지가 그려지지 않는다.\n" +
             "채움이 트랙 테두리를 넘는다면 홈 크기의 빈 사각을 만들어 여기 꽂는다.")]
    [SerializeField] RectTransform trackRect;

    // 폭이 0인 저작은 매 프레임 도는 트윈에서 콘솔을 덮으므로 한 번만 알린다.
    bool m_widthReported;

    /// <summary>100%일 때의 폭(px). 기준 사각이 없거나 폭이 서지 않았으면 0.</summary>
    public float FullWidth
    {
        get
        {
            RectTransform t_track = this.Track;
            return t_track != null ? t_track.rect.width : 0f;
        }
    }

    public override Vector2 MarkerPos(float _ratio)
    {
        RectTransform t_rect = this.Rect;
        if (t_rect == null) return Vector2.zero;

        Vector2 t_pos = t_rect.anchoredPosition;
        return new Vector2(t_pos.x + this.FullWidth * Mathf.Clamp01(_ratio), t_pos.y);
    }

    protected override void ApplyRatio(float _ratio)
    {
        RectTransform t_rect = this.Rect;
        if (t_rect == null) return;

        float t_full = this.FullWidth;
        if (t_full <= 0f)
        {
            this.ReportMissingWidth();
            return;
        }

        Vector2 t_size = t_rect.sizeDelta;
        t_rect.sizeDelta = new Vector2(t_full * Mathf.Clamp01(_ratio), t_size.y);
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

    RectTransform Track
    {
        get
        {
            if (this.trackRect != null) return this.trackRect;

            RectTransform t_rect = this.Rect;
            return t_rect != null ? t_rect.parent as RectTransform : null;
        }
    }

    void ReportMissingWidth()
    {
        if (this.m_widthReported) return;
        this.m_widthReported = true;

        Debug.LogError($"[{name}] 게이지 기준 폭이 0이다 — 기준 사각의 저작 폭을 확인할 것.", this);
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (this.fillRect == null) return;

        // 기준 사각을 채움 쪽으로 잡으면 폭이 그리기마다 줄어들어 게이지가 스스로 사라진다.
        // IsChildOf는 자기 자신에도 true라 "자신이거나 자손"이 이 한 줄로 걸린다.
        if (this.trackRect != null && this.trackRect.IsChildOf(this.fillRect))
            Debug.LogWarning($"[{name}] 기준 사각이 채움 사각 자신이거나 그 자손이다 — 채움 바깥의 사각을 꽂을 것.", this);

        // 가로 스트레치 앵커에서 sizeDelta.x는 폭이 아니라 좌우 여백이라, 그리기가 트랙 밖으로 자라면서도
        // 폭 검사에는 걸리지 않는다. 저작 시점에 잡지 않으면 화면에서만 드러난다.
        if (this.fillRect.pivot.x != 0f || this.fillRect.anchorMin.x != 0f || this.fillRect.anchorMax.x != 0f)
            Debug.LogWarning($"[{name}] 채움 사각의 pivot.x·anchor.x가 0이 아니다 — 왼쪽 고정 폭 저작이어야 폭이 뜻대로 해석된다.", this);
    }
#endif
}

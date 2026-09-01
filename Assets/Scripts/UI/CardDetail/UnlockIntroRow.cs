using UnityEngine;
using UnityEngine.UI;

/// <summary>해금 안내 한 줄. 글자는 KeywordExplainItem에 위임하고, 데모 띠와 시너지 배지,
/// 그리고 시너지 줄 아래에 서는 티어 줄들을 맡는다.
///
/// 세로 자리 배분의 주인이 여기다: 티어 줄 수만큼 설명문을 올리고, 데모 띠는 그러고도 남은
/// "구분선 아래 ~ 설명문 위" 밴드에만 세운다. 띠가 행 전체를 채우면 시너지 줄에서 글자를 덮는다.</summary>
[RequireComponent(typeof(RectTransform))]
public class UnlockIntroRow : MonoBehaviour
{
    [Tooltip("아이콘·이름·설명. 미배선이면 글자 없이 띠만 뜬다.")]
    [SerializeField] KeywordExplainItem item;

    [Tooltip("데모 무대가 그려지는 띠(구분선과 설명 사이). 미배선이면 이 축만 빠지고 행은 글자로 성립한다.")]
    [SerializeField] RawImage demoStrip;

    [Tooltip("데모 띠의 AspectRatioFitter(선택). 띠의 두 축은 코드가 가지므로, 배선해 두면 모드를 None으로 잠가\n" +
             "저작이 코드가 정한 크기를 되돌리지 못하게 한다.")]
    [SerializeField] AspectRatioFitter demoFitter;

    [Tooltip("띠의 위끝 기준이 되는 구분선. 밴드 위끝의 진실원이라 같은 값을 코드에 다시 적지 않는다.\n" +
             "미배선이면 위끝이 부모 윗변으로 내려앉아 띠가 위쪽으로 넓어진다.")]
    [SerializeField] RectTransform dividerRect;

    [Tooltip("띠가 구분선·설명문과 띄우는 간격. 위아래에 같이 걸린다.")]
    [SerializeField] float demoGap = 24f;

    [Tooltip("띠의 한쪽 좌우 여백. 좌우 양쪽에 같이 걸린다 —\n" +
             "부모 폭에서 이 값의 두 배를 뺀 것이 띠가 쓸 수 있는 최대 폭이다.")]
    [SerializeField] float demoSideInset = 20f;

    [Tooltip("데모 무대를 못 세운 시너지 칸이 대신 세우는 가운데 배지. 미배선이면 이 축만 빠지고 행은 글자로 성립한다.")]
    [SerializeField] Image synergyBadge;

    [Header("티어 줄")]
    [Tooltip("설명문의 RectTransform. 티어 줄이 몇 개 서느냐에 따라 이 자리를 위로 올린다.\n" +
             "미배선이면 설명문이 제자리에 남아 티어 줄과 겹친다.")]
    [SerializeField] RectTransform explainRect;

    [Tooltip("미리 깔아 둔 티어 줄(위에서 아래 순). 채우는 것은 아래부터라 티어가 하나면 맨 아래만 선다.\n" +
             "런타임 Instantiate 없음 — 개수가 모자라면 앞 단계부터 잘린다.")]
    [SerializeField] UnlockIntroTierRow[] tierRows;

    [Tooltip("맨 위 티어 줄과 설명문 사이 간격.")]
    [SerializeField] float tierGap = 28f;

    // 이 줄이 시너지면 여기에 아이콘이 들어온다. 실제로 세울지는 SetDemo가 정한다 —
    // 배지와 데모 띠는 같은 자리를 쓰므로 둘 중 하나만 서야 한다.
    Sprite m_badgeIcon;

    // 티어 줄이 하나도 없을 때 설명문이 앉는 자리(저작값). 티어 수에 따라 매번 여기서부터 다시 올린다 —
    // 직전 표시의 올려진 좌표를 기준으로 삼으면 표시할 때마다 설명문이 위로 밀려 올라간다.
    float m_explainY0;
    bool  m_explainCached;

    // 깔린 티어 줄이 모자란 것은 저작 문제다 — 매 표시마다 경고하면 로그가 묻힌다.
    static bool s_tierShortageWarned;

    // 데모 띠가 설 자리가 없는 것도 저작 문제다 — 같은 규약으로 한 번만 알린다.
    static bool s_bandShortageWarned;

    // GetWorldCorners 재사용 버퍼.
    static readonly Vector3[] s_corners = new Vector3[4];

    /// <summary>글자를 채우고 배지 후보를 받아 둔다. 가운데 자리의 주인은 <see cref="SetDemo"/>가 정한다.</summary>
    public void Bind(UnlockIntro _intro)
    {
        this.m_badgeIcon = _intro.IsSynergy ? _intro.Icon : null;
        SetSynergyBadge(null);   // 데모 여부가 아직 안 정해졌다 — 일단 비우고 SetDemo를 기다린다

        if (this.item != null)
            this.item.Init(_intro.Icon, _intro.Name, _intro.Body, _intro.IconScale);

        LayoutTiers(_intro.Synergy);
    }

    /// <summary>데모 띠에 그림을 물린다. null이면 띠를 끄고, 시너지 줄이면 그 자리에 배지를 대신 세운다
    /// (저작이 덜 됐거나 무대를 못 세운 경우의 폴백).</summary>
    public void SetDemo(Texture _texture)
    {
        // 띠의 두 축은 코드가 갖는다 — 피터가 살아 있으면 리빌드마다 코드가 정한 크기를 되돌린다.
        if (this.demoFitter != null) this.demoFitter.aspectMode = AspectRatioFitter.AspectMode.None;

        if (this.demoStrip != null)
        {
            this.demoStrip.texture = _texture;
            this.demoStrip.gameObject.SetActive(_texture != null);

            if (_texture != null && _texture.height > 0)
                ApplyDemoBand((float)_texture.width / _texture.height);
        }

        SetSynergyBadge(_texture == null ? this.m_badgeIcon : null);
    }

    // 띠를 "구분선 아래 ~ 설명문 위"의 남은 자리에 앉힌다. 설명문은 티어 수만큼 올라가 있으므로
    // 이 계산은 LayoutTiers 뒤에 와야 한다(SetDemo가 Bind보다 늦게 불리는 것이 그 보장이다).
    //
    // 두 기준 rect는 부모도 앵커 기준도 서로 달라 anchoredPosition을 그대로 빼면 기준이 섞인다.
    // 그래서 월드 모서리를 띠의 부모 공간으로 옮겨 한 좌표계에서만 계산한다.
    void ApplyDemoBand(float _ratio)
    {
        RectTransform t_rt    = this.demoStrip.rectTransform;
        var           t_space = t_rt.parent as RectTransform;

        if (t_space == null || _ratio <= 0f) return;

        Rect  t_bounds = t_space.rect;
        float t_top    = t_bounds.yMax;
        float t_bottom = t_bounds.yMin;

        if (this.dividerRect != null) t_top    = EdgeYIn(t_space, this.dividerRect, false) - this.demoGap;
        if (this.explainRect != null) t_bottom = EdgeYIn(t_space, this.explainRect, true)  + this.demoGap;

        // 자리가 없으면 부모 전체로 되돌린다. 0으로 접으면 띠가 사라진 것처럼 보여 저작자가 원인을 못 찾는데,
        // 되돌려 두면 겹치는 옛 모습이 남아 로그가 가리키는 곳과 눈에 보이는 것이 맞는다.
        if (t_top - t_bottom <= 0f)
        {
            if (!s_bandShortageWarned)
            {
                s_bandShortageWarned = true;
                Debug.LogWarning($"[UnlockIntroRow] 구분선({t_top:F1})과 설명문({t_bottom:F1}) 사이에 "
                               + "데모 띠가 설 자리가 없습니다(demoGap 또는 티어 줄 저작을 볼 것).");
            }

            t_top    = t_bounds.yMax;
            t_bottom = t_bounds.yMin;
        }

        float t_maxWidth = t_bounds.width - this.demoSideInset * 2f;
        float t_height   = Mathf.Max(0f, Mathf.Min(t_top - t_bottom, t_maxWidth / _ratio));

        t_rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, t_height * _ratio);
        t_rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,   t_height);

        // 앵커가 상하좌우 스트레치이고 피벗이 가운데라 anchoredPosition은 부모 rect 중심에서의 오프셋이다.
        float t_centerY = (t_top + t_bottom) * 0.5f - t_bounds.center.y;
        t_rt.anchoredPosition = new Vector2(t_rt.anchoredPosition.x, t_centerY);
    }

    // _rect의 위(또는 아래) 변을 _space 로컬 Y로 옮긴다. 두 rect가 같은 조상 아래에 있으면
    // 등장 안무가 걸어 둔 배율도 이 왕복에서 함께 상쇄된다.
    static float EdgeYIn(RectTransform _space, RectTransform _rect, bool _top)
    {
        _rect.GetWorldCorners(s_corners);
        return _space.InverseTransformPoint(_top ? s_corners[1] : s_corners[0]).y;
    }

    // 티어 줄을 아래부터 채우고 설명문을 그 위로 올린다. 키워드 줄은 티어가 없어 전부 꺼지고
    // 설명문이 저작 자리로 돌아간다 — 같은 프리팹이 두 종류를 다 받는다.
    void LayoutTiers(SynergyData _synergy)
    {
        CacheExplainHome();

        int t_slots = this.tierRows != null ? this.tierRows.Length : 0;
        if (t_slots == 0) return;

        SynergyTier[] t_tiers = _synergy != null ? _synergy.tiers : null;
        int           t_count = CountTiers(t_tiers);

        if (t_count > t_slots)
        {
            if (!s_tierShortageWarned)
            {
                s_tierShortageWarned = true;
                Debug.LogWarning($"[UnlockIntroRow] 깔린 티어 줄이 {t_slots}개뿐이라 "
                               + $"{t_count}단계를 다 세우지 못했습니다(프리팹에 줄을 더 깔 것).");
            }
            t_count = t_slots;
        }

        // 아래부터 채운다. 티어가 하나면 맨 아래 줄만 서고 위쪽은 빈 자리로 남는다 —
        // 위부터 채우면 줄 아래에 구멍이 생겨 설명문과의 간격만 늘어난 것처럼 보인다.
        int t_first = t_slots - t_count;

        for (int t_i = 0; t_i < t_slots; t_i++)
        {
            UnlockIntroTierRow t_row = this.tierRows[t_i];
            if (t_row == null) continue;

            if (t_i < t_first) { t_row.gameObject.SetActive(false); continue; }

            SynergyTier t_tier = TierAt(t_tiers, t_i - t_first);
            t_row.Bind(SynergyText.TierRequirement(t_tier), SynergyText.TierEffect(_synergy, t_tier));
            t_row.gameObject.SetActive(true);
        }

        MoveExplainAbove(t_count > 0 ? this.tierRows[t_first] : null);
    }

    // 설명문을 맨 위 티어 줄 위로 올린다. _top이 null이면 저작 자리로 되돌린다.
    void MoveExplainAbove(UnlockIntroTierRow _top)
    {
        if (this.explainRect == null || !this.m_explainCached) return;

        float t_y = this.m_explainY0;

        if (_top != null)
        {
            var t_rect = (RectTransform)_top.transform;
            t_y = t_rect.anchoredPosition.y + t_rect.rect.height + this.tierGap;
        }

        Vector2 t_pos = this.explainRect.anchoredPosition;
        this.explainRect.anchoredPosition = new Vector2(t_pos.x, t_y);
    }

    void CacheExplainHome()
    {
        if (this.m_explainCached || this.explainRect == null) return;

        this.m_explainY0    = this.explainRect.anchoredPosition.y;
        this.m_explainCached = true;
    }

    static int CountTiers(SynergyTier[] _tiers)
    {
        if (_tiers == null) return 0;

        int t_count = 0;
        foreach (SynergyTier t_tier in _tiers)
            if (t_tier != null) t_count++;

        return t_count;
    }

    // 빈 칸이 섞여 있어도 순서가 밀리지 않게, null을 건너뛰며 _index번째 유효 티어를 꺼낸다.
    static SynergyTier TierAt(SynergyTier[] _tiers, int _index)
    {
        if (_tiers == null) return null;

        int t_seen = 0;
        foreach (SynergyTier t_tier in _tiers)
        {
            if (t_tier == null) continue;
            if (t_seen == _index) return t_tier;
            t_seen++;
        }

        return null;
    }

    void SetSynergyBadge(Sprite _icon)
    {
        if (this.synergyBadge == null) return;

        this.synergyBadge.sprite = _icon;
        this.synergyBadge.gameObject.SetActive(_icon != null);
    }
}

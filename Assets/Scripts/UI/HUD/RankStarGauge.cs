using System;
using UnityEngine;
using UnityEngine.UI;

// 게이지 구현 한 종류 — 등급 안 진행을 별 N개가 왼쪽부터 차례로 채우며 그린다.
// 별 K의 채움 = 진행의 K번째 1/N 토막. "점등"은 별도 상태가 아니라 fillAmount == 1인 것 그 자체다 —
// 별을 따로 켜고 끄는 경로를 두면 게이지와 별이 서로 다른 시계로 돌아 어긋난다.
// 값·트윈·마디 통과 판정은 베이스(RankProgressGauge)가 맡는다.
public class RankStarGauge : RankProgressGauge
{
    // 별 한 칸의 배선. 밑판(Off)은 항상 깔려 있기만 하면 되므로 게이지가 참조하지 않는다.
    [Serializable]
    public class Star
    {
        [Tooltip("펀치가 겨눌 별의 사각. 자리는 프리팹 저작값을 그대로 쓴다 — 코드가 좌표를 덮지 않는다.")]
        public RectTransform rect;

        [Tooltip("채워지는 On 스프라이트. Image Type=Filled / Fill Method=Vertical / Fill Origin=Bottom 전제.")]
        public Image fill;
    }

    [Tooltip("왼쪽부터 순서대로. 개수는 RankConfig.DivisionsPerGrade와 맞춘다 — " +
             "모자라면 그만큼만 그리고, 남으면 뒤쪽 별은 영영 차지 않는다.")]
    [SerializeField] Star[] stars;

    /// <summary>배선된 별 개수. 등급 안 진행을 몇 토막으로 나누는가와 같다.</summary>
    public int StarCount => this.stars != null ? this.stars.Length : 0;

    /// <summary>연출이 펀치할 별의 사각. 범위 밖이면 null.</summary>
    public RectTransform StarRect(int _index)
    {
        if (this.stars == null || _index < 0 || _index >= this.stars.Length) return null;

        var t_star = this.stars[_index];
        return t_star != null ? t_star.rect : null;
    }

    public override Vector2 MarkerPos(float _ratio)
    {
        // 경계 (K+1)/N은 별 K를 가리킨다 — 새 모델의 사건은 "별이 꽉 찼다"라서 자리도 방금 찬 별 쪽이다.
        int t_count = this.StarCount;
        if (t_count == 0) return Vector2.zero;

        int t_index = Mathf.Clamp(Mathf.CeilToInt(Mathf.Clamp01(_ratio) * t_count) - 1, 0, t_count - 1);

        var t_rect = this.StarRect(t_index);
        return t_rect != null ? t_rect.anchoredPosition : Vector2.zero;
    }

    protected override void ApplyRatio(float _ratio)
    {
        int t_count = this.StarCount;

        for (int t_i = 0; t_i < t_count; t_i++)
        {
            var t_star = this.stars[t_i];
            if (t_star == null || t_star.fill == null) continue;

            t_star.fill.fillAmount = Mathf.Clamp01(_ratio * t_count - t_i);
        }
    }
}

using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>성장 단계를 고정된 별 칸의 채움으로 표시한다.</summary>
public sealed class GrowthStarStrip : MonoBehaviour
{
    public static readonly Color FilledColor = new Color(1f, 0.84f, 0.42f, 1f);
    public static readonly Color EmptyColor = new Color(0.25f, 0.28f, 0.34f, 1f);

    [SerializeField] Image[] stars;

    public void SetLevel(int _level)
    {
        if (this.stars == null) return;
        int t_count = GrowthStar.FromLevel(_level);
        for (int t_i = 0; t_i < this.stars.Length; t_i++)
        {
            Image t_star = this.stars[t_i];
            if (t_star == null) continue;
            t_star.transform.DOKill(true);
            t_star.color = t_i < t_count ? FilledColor : EmptyColor;
        }
    }

    // 결과 행이 제자리에 앉은 뒤 새로 얻은 별만 강조한다. 샤드만 투입한 경우에는 별을 늘리지 않는다.
    public void PulseGained(int _fromLevel, int _toLevel)
    {
        if (this.stars == null) return;
        int t_from = Mathf.Clamp(GrowthStar.FromLevel(_fromLevel), 0, this.stars.Length);
        int t_to = Mathf.Clamp(GrowthStar.FromLevel(_toLevel), 0, this.stars.Length);
        for (int t_i = t_from; t_i < t_to; t_i++)
            if (this.stars[t_i] != null) UiPunch.Play(this.stars[t_i].transform, 0.25f, 0.35f);
    }

    void OnDisable()
    {
        if (this.stars == null) return;
        foreach (Image t_star in this.stars)
            if (t_star != null) t_star.transform.DOKill(true);
    }
}

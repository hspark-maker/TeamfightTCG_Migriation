using System.Collections.Generic;
using UnityEngine;

/// <summary>덱의 전투 참여 가능 시너지를 현재/다음 티어 진행도와 함께 표시한다.</summary>
public class DeckSynergyStrip : MonoBehaviour
{
    [SerializeField] SynergyCountIcon[] icons;

    [Header("Filter")]
    [Tooltip("해제하면 아직 활성화되지 않은 시너지는 숨긴다.")]
    [SerializeField] bool showInactive = true;
    [Tooltip("0보다 크면 이 개수까지만 표시한다.")]
    [SerializeField] int maxIcons;

    readonly List<CardData> eligibleCards = new List<CardData>();

    void Awake()
    {
        if (this.icons == null) return;

        foreach (SynergyCountIcon t_icon in this.icons)
        {
            if (t_icon == null) continue;
            t_icon.onLongPress    = ShowExplain;
            t_icon.onLongPressEnd = _ => HideExplain();
            t_icon.gameObject.SetActive(false);
        }
    }

    public void Refresh(IEnumerable<CardData> _deck)
    {
        this.eligibleCards.Clear();
        if (_deck != null)
        {
            foreach (CardData t_card in _deck)
            {
                if (t_card == null) continue;
                if (DeckConfig.IsMultiplayer)
                {
                    this.eligibleCards.Add(t_card);
                    continue;
                }

                CardGrowth t_growth = CardGrowthManager.GrowthOf(t_card);
                if (!t_growth.Applied || t_growth.SynergyUnlocked)
                    this.eligibleCards.Add(t_card);
            }
        }

        List<SynergyProgress> t_all = SynergyPreview.Resolve(this.eligibleCards);
        var t_shown = new List<SynergyProgress>();
        foreach (SynergyProgress t_progress in t_all)
        {
            if (!this.showInactive && !t_progress.IsActive) continue;
            t_shown.Add(t_progress);
            if (this.maxIcons > 0 && t_shown.Count >= this.maxIcons) break;
        }

        if (this.icons == null) return;
        for (int i = 0; i < this.icons.Length; i++)
        {
            SynergyCountIcon t_icon = this.icons[i];
            if (t_icon == null) continue;
            if (i < t_shown.Count) t_icon.Set(t_shown[i]);
            else                   t_icon.gameObject.SetActive(false);
        }
    }

    public void Clear() => Refresh(null);

    void OnDisable() => HideExplain();

    void ShowExplain(SynergyCountIcon _icon)
    {
        if (_icon == null || _icon.Progress?.Synergy == null) return;

        UIPoolManager.Instance?.AddOrUpdateUI<SynergyExplainPopupUI>(new SynergyExplainData
        {
            synergy    = _icon.Progress.Synergy,
            iconRect   = (RectTransform)_icon.transform,
            ownedCount = _icon.Progress.Count,
        });
    }

    static void HideExplain() => UIPoolManager.Instance?.HideUI<SynergyExplainPopupUI>();
}

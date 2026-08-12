using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>덱의 전투 참여 가능 시너지를 현재/다음 티어 진행도와 함께 표시한다.</summary>
public class DeckSynergyStrip : MonoBehaviour
{
    /// <summary>롱프레스로 시너지 하나를 들여다보는 중이면 그 시너지, 손을 떼면 null.
    /// 어떤 카드를 강조할지는 편성 상태를 아는 쪽(DeckEditController)이 정한다.</summary>
    public Action<SynergyData> onFocusChanged;

    [SerializeField] SynergyCountIcon[] icons;

    [Header("Filter")]
    [Tooltip("해제하면 아직 활성화되지 않은 시너지는 숨긴다.")]
    [SerializeField] bool showInactive = true;
    [Tooltip("0보다 크면 이 개수까지만 표시한다.")]
    [SerializeField] int maxIcons;

    readonly List<CardData> eligibleCards = new List<CardData>();

    // 내가 연 popup이 떠 있는 동안만 true. 안 연 채로 닫으면 남의 popup을 대신 지운다.
    bool m_explainOpen;

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

        this.m_explainOpen = true;
        this.onFocusChanged?.Invoke(_icon.Progress.Synergy);
    }

    // 설명 popup과 강조는 항상 같이 뜨고 같이 진다 — 한쪽만 남으면 화면이 흐린 채로 굳는다.
    void HideExplain()
    {
        if (!this.m_explainOpen) return;

        this.m_explainOpen = false;
        UIPoolManager.Instance?.HideUI<SynergyExplainPopupUI>();
        this.onFocusChanged?.Invoke(null);
    }
}

using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 키워드 강화 그리드의 한 칸. 강화는 이 칸이 하지 않는다 — 누르면 선택만 바뀌고
// 실제 강화는 하단 업그레이드 버튼 하나가 맡는다(선택과 실행을 갈라 오조작을 막는 레퍼런스 구조).
public class KeywordGrowthCellView : MonoBehaviour
{
    [SerializeField] Image     iconImage;    // 키워드 아이콘(KeywordIconConfig가 정본)
    [SerializeField] TMP_Text  levelText;    // "Lv 0"
    [SerializeField] TMP_Text  bonusText;    // "+3" (0이면 숨김)
    [SerializeField] Button    selectButton;

    [Header("상태 노드(선택 — 미배선 시 null 가드)")]
    [SerializeField] GameObject selectRing;  // 선택된 칸의 테두리

    public CardKeyword Keyword => this.m_keyword;

    CardKeyword m_keyword = CardKeyword.None;

    Action<CardKeyword> m_onSelect;

    public void Bind(CardKeyword _keyword, Action<CardKeyword> _onSelect)
    {
        this.m_keyword  = _keyword;
        this.m_onSelect = _onSelect;

        if (this.selectButton != null)
        {
            this.selectButton.onClick.RemoveAllListeners();
            this.selectButton.onClick.AddListener(this.HandleClick);
        }

        // 아이콘은 강화로 변하지 않는다 — 바인딩 때 한 번만 세운다.
        KeywordIconConfig t_config = DataLibrary.instance != null ? DataLibrary.instance.keywordIconConfig : null;
        if (this.iconImage != null && t_config != null
            && t_config.TryGetEntry(_keyword, out KeywordIconConfig.Entry t_entry) && t_entry.icon != null)
            this.iconImage.sprite = t_entry.icon;
    }

    public void Refresh(bool _selected)
    {
        if (this.m_keyword == CardKeyword.None) return;

        int t_level = KeywordGrowthManager.LevelOf(this.m_keyword);
        int t_bonus = t_level * KeywordGrowthManager.Config.HpPerLevel;

        if (this.levelText != null) this.levelText.text = $"Lv {t_level}";
        if (this.bonusText != null)
        {
            this.bonusText.gameObject.SetActive(t_bonus > 0);
            this.bonusText.text = $"+{t_bonus}";
        }
        if (this.selectRing != null) this.selectRing.SetActive(_selected);
    }

    public void PlayUpgradePop()
    {
        UiPunch.Play(this.selectButton != null ? this.selectButton.transform : transform);
    }

    void HandleClick()
    {
        if (this.m_keyword == CardKeyword.None) return;

        this.m_onSelect?.Invoke(this.m_keyword);
    }
}

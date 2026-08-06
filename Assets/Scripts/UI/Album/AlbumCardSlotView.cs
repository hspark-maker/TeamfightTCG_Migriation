using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 앨범 페이지의 카드 칸 하나(Slot_00 부착). 클릭 배선은 오버레이 몫 — 여기는 표시만
public class AlbumCardSlotView : MonoBehaviour
{
    [SerializeField] Button button;
    [SerializeField] Image icon;
    [SerializeField] GameObject starBadge;
    [SerializeField] GameObject hpBadge;
    [SerializeField] TMP_Text hpLabel;
    [SerializeField] GameObject namePlate;
    [SerializeField] TMP_Text nameLabel;
    [SerializeField] GameObject roleBadge;
    [SerializeField] Image roleIcon;
    [SerializeField] KeywordIconConfig keywordIconConfig;
    [SerializeField] Color unownedTint = new Color(0.45f, 0.45f, 0.45f, 1f);

    readonly List<GameObject> m_stars = new List<GameObject>();
    bool m_starsPooled;

    public Button Button => button;

    public void Bind(CardData _card, bool _owned)
    {
        if (_card == null)
        {
            BindEmpty();
            return;
        }

        if (icon != null)
        {
            var t_art = CardVisualRules.PickCardArt(_card);
            icon.enabled = t_art != null;
            icon.sprite = t_art;
            icon.color = _owned ? Color.white : unownedTint;
        }

        // 미소유도 이름은 보여준다 — "무엇을 모으는지"가 수집 동기다
        if (namePlate != null) namePlate.SetActive(true);
        if (nameLabel != null) nameLabel.text = _card.displayName;
        if (button != null) button.interactable = true;

        if (!_owned)
        {
            if (hpBadge != null) hpBadge.SetActive(false);
            if (roleBadge != null) roleBadge.SetActive(false);
            if (starBadge != null) starBadge.SetActive(false);
            return;
        }

        if (hpBadge != null) hpBadge.SetActive(true);
        if (hpLabel != null) hpLabel.text = DeckPower.MaxHpOf(_card).ToString();

        // CardInstance와 같은 규칙 — 마스터 기본과 강화 해금 중 높은 쪽(플레이어 진화 반영)
        BindStars(Mathf.Max(_card.defaultEvolutionStage, CardGrowthManager.GrowthOf(_card).EvolutionStage));
        BindRole(_card);
    }

    void BindEmpty()
    {
        if (icon != null) icon.enabled = false;
        if (starBadge != null) starBadge.SetActive(false);
        if (hpBadge != null) hpBadge.SetActive(false);
        if (namePlate != null) namePlate.SetActive(false);
        if (roleBadge != null) roleBadge.SetActive(false);
        if (button != null) button.interactable = false;
    }

    void BindStars(int _stage)
    {
        if (starBadge == null) return;

        int t_stage = Mathf.Clamp(_stage, 0, CardData.MaxEvolutionStage);
        starBadge.SetActive(t_stage > 0);
        if (t_stage <= 0) return;

        EnsureStarPool(t_stage);
        for (int t_i = 0; t_i < m_stars.Count; t_i++)
            m_stars[t_i].SetActive(t_i < t_stage);
    }

    // 첫 Bind에서 목업 별들을 풀로 수거하고, 부족하면 0번을 클론한다
    void EnsureStarPool(int _need)
    {
        if (!m_starsPooled)
        {
            m_starsPooled = true;
            foreach (Transform t_child in starBadge.transform)
                m_stars.Add(t_child.gameObject);
            if (m_stars.Count == 0)
                Debug.LogWarning("[AlbumCardSlotView] Badge_Star에 별 템플릿이 없어 별을 그릴 수 없다", this);
        }

        int t_cap = Mathf.Min(_need, CardData.MaxEvolutionStage);
        while (m_stars.Count > 0 && m_stars.Count < t_cap)
            m_stars.Add(Instantiate(m_stars[0], starBadge.transform));
    }

    void BindRole(CardData _card)
    {
        if (roleBadge == null) return;

        Sprite t_sprite = null;
        if (keywordIconConfig != null)
        {
            var t_icons = CardVisualRules.CollectKeywordIcons(CardVisualRules.IconKeywords(_card), keywordIconConfig);
            for (int t_i = 0; t_i < t_icons.Count; t_i++)
            {
                // 폴백(None)은 카드 정체성이 아니다 — 배지를 숨기는 편이 맞다
                if (t_icons[t_i].Keyword == CardKeyword.None) continue;
                t_sprite = t_icons[t_i].Icon;
                break;
            }
        }

        roleBadge.SetActive(t_sprite != null);
        if (t_sprite != null && roleIcon != null) roleIcon.sprite = t_sprite;
    }
}

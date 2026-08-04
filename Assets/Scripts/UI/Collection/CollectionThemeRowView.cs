using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 도감 테마 한 행(행 프리팹 루트에 부착).
// 행은 펼침 상태를 스스로 토글하지 않는다 — "한 번에 하나"는 목록이 SetExpanded로 강제한다.
public class CollectionThemeRowView : MonoBehaviour
{
    [Header("헤더")]
    [SerializeField] Button        headerButton;
    [SerializeField] TMP_Text      nameText;
    [SerializeField] TMP_Text      progressText;
    [SerializeField] RectTransform arrow;
    [SerializeField] Image         themeIcon;      // 선택 — 테마에 아이콘이 없으면 꺼둔다

    [Header("본문")]
    [SerializeField] GameObject              body;           // GridLayoutGroup 노드
    [SerializeField] Transform               slotContainer;  // 슬롯 부모(보통 body와 같은 노드)
    [SerializeField] CollectionThemeSlotView slotPrefab;

    [Header("연출")]
    [SerializeField] float arrowExpandedZ = -90f;

    CollectionTheme m_theme;

    readonly List<CollectionThemeSlotView> m_slots = new List<CollectionThemeSlotView>();

    // 상세 오버레이가 넘겨볼 순서. 오버레이가 참조로 들고 있으므로 인스턴스를 갈아치우지 않는다.
    readonly List<CardData> m_order = new List<CardData>();

    // 슬롯은 최초 펼침 때 1회만 만든다(접어도 파괴하지 않는다).
    bool m_slotsBuilt;

    public void Bind(CollectionTheme _theme, System.Action<int> _onHeaderClicked)
    {
        m_theme = _theme;

        if (nameText != null) nameText.text = _theme != null ? _theme.DisplayName : string.Empty;

        if (themeIcon != null)
        {
            Sprite t_icon = _theme != null ? _theme.Icon : null;
            themeIcon.sprite  = t_icon;
            themeIcon.enabled = t_icon != null;
        }

        // 재바인딩마다 중복 등록 방지.
        if (headerButton != null)
        {
            headerButton.onClick.RemoveAllListeners();
            if (_onHeaderClicked != null && _theme != null)
                headerButton.onClick.AddListener(() => _onHeaderClicked(m_theme.Index));
        }

        RefreshProgress();
    }

    public void SetExpanded(bool _expanded)
    {
        if (body != null) body.SetActive(_expanded);

        // 슬롯 생성은 body를 켠 **다음**이어야 한다 — CardVisualView.ApplyIngameFontScale이
        // 꺼진 부모(rect 높이 0)에서는 건너뛰어 글자 크기가 어긋난 채로 남는다.
        if (_expanded) EnsureSlots();

        if (arrow != null)
        {
            Vector3 t_euler = arrow.localEulerAngles;
            t_euler.z = _expanded ? arrowExpandedZ : 0f;
            arrow.localEulerAngles = t_euler;
        }
    }

    public void RefreshProgress()
    {
        if (progressText == null) return;

        int t_total = m_theme != null && m_theme.Cards != null ? m_theme.Cards.Count : 0;
        int t_owned = m_theme != null ? CollectionThemes.OwnedCountOf(m_theme) : 0;

        progressText.text = $"{t_owned}/{t_total}";
    }

    // 접혀 있어도 수행한다 — 접힌 사이에 팩을 까서 소유가 늘었을 수 있다.
    public void RefreshOwnership()
    {
        if (!m_slotsBuilt || m_theme == null) return;

        var t_cards = m_theme.Cards;
        if (t_cards == null) return;

        for (int t_i = 0; t_i < m_slots.Count && t_i < t_cards.Count; t_i++)
        {
            var t_card = t_cards[t_i];
            if (m_slots[t_i] != null) m_slots[t_i].Bind(t_card, OwnershipManager.IsOwned(t_card), t_i + 1);
        }
    }

    // 최초 펼침 1회 생성.
    void EnsureSlots()
    {
        if (m_slotsBuilt || m_theme == null || slotPrefab == null) return;

        Transform t_parent = slotContainer != null ? slotContainer
                           : body != null          ? body.transform
                                                   : null;
        if (t_parent == null) return;

        // 프리팹에 저작된 목업 슬롯 제거. Destroy는 프레임 말 지연이라 먼저 꺼야
        // 같은 프레임의 ScrollToRow가 목업까지 더한 높이를 읽지 않는다.
        for (int t_i = t_parent.childCount - 1; t_i >= 0; t_i--)
        {
            var t_mock = t_parent.GetChild(t_i).gameObject;
            t_mock.SetActive(false);
            Destroy(t_mock);
        }

        var t_cards = m_theme.Cards;

        for (int t_i = 0; t_i < t_cards.Count; t_i++)
        {
            var t_card = t_cards[t_i];
            var t_slot = Instantiate(slotPrefab, t_parent);
            t_slot.Bind(t_card, OwnershipManager.IsOwned(t_card), t_i + 1);

            // 미소유 슬롯도 담는다 — 넘겨보는 순서가 화면 배열과 같아야 길을 잃지 않는다.
            m_order.Add(t_card);
            CardDetailOverlayView.BindTile(t_slot.CardView, m_order, t_i);
            m_slots.Add(t_slot);
        }

        m_slotsBuilt = true;
    }
}

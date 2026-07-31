using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// 탭 버튼 하나의 선택/비선택 겉모습 (탭 버튼 오브젝트에 부착, LobbyTabController가 SetSelected로 구동)
/// "탭 방식" = 선택 탭만 아래 콘텐츠까지 이어져 보이도록 더 높고 밝게, 나머지는 낮고 어둡게 뒤로 물러난다.
/// 부모 탭바가 HorizontalLayoutGroup(ChildControlHeight=0, ChildAlignment=LowerCenter)이면
/// 높이 차이가 곧 "선택 탭이 위로 솟은" 모양이 된다.
///
/// 스프라이트 슬롯이 비어 있으면 색만 바꾼다 — 나중에 이미지 에셋을 꽂으면 코드 수정 없이 룩이 바뀐다.
[RequireComponent(typeof(RectTransform))]
public class TabButtonView : MonoBehaviour
{
    [Header("배선 (비우면 자동 탐색)")]
    [SerializeField] Image background;          // 탭 배경. 비우면 자기 자신의 Image
    [SerializeField] TMP_Text label;            // 탭 이름(옵션). 비우면 자식에서 찾는다
    [SerializeField] Image icon;                // 탭 아이콘(옵션) — 색만 상태에 맞춰 바꾼다
    [SerializeField] GameObject selectedMark;   // 선택 표시 장식(밑줄·글로우 등, 옵션). 선택일 때만 켠다

    [Header("선택 상태")]
    [SerializeField] Sprite selectedSprite;                                          // 비우면 스프라이트 유지(색만 적용)
    [SerializeField] Color selectedColor = Color.white;
    [SerializeField] Color selectedLabelColor = new Color(0.15f, 0.18f, 0.22f, 1f);
    [Tooltip("선택 탭 높이. 탭바 높이와 같게 두면 탭바 위쪽 끝까지 채운다.")]
    [SerializeField] float selectedHeight = 110f;

    [Header("비선택 상태")]
    [SerializeField] Sprite normalSprite;                                            // 비우면 스프라이트 유지(색만 적용)
    [SerializeField] Color normalColor = new Color(0.66f, 0.70f, 0.76f, 1f);
    [SerializeField] Color normalLabelColor = new Color(0.34f, 0.38f, 0.44f, 1f);
    [Tooltip("비선택 탭 높이. 선택보다 낮게 두면 선택 탭이 솟아 보인다(탭 느낌의 핵심).")]
    [SerializeField] float normalHeight = 86f;

    RectTransform m_rect;

    // 현재 상태. 첫 호출은 무조건 적용해야 하므로 nullable로 "아직 없음"을 구분한다.
    bool? m_selected;

    void Awake()
    {
        m_rect = (RectTransform)transform;

        // 인스펙터 배선을 강요하지 않는다 — 탭을 복제해 늘릴 때 손이 덜 간다.
        if (background == null) background = GetComponent<Image>();
        if (label == null) label = GetComponentInChildren<TMP_Text>(true);
    }

    /// 선택 여부에 맞춰 배경·라벨·아이콘·높이를 한 번에 적용한다.
    public void SetSelected(bool _on)
    {
        if (m_rect == null) m_rect = (RectTransform)transform;
        if (m_selected == _on) return;
        m_selected = _on;

        if (background != null)
        {
            Sprite sprite = _on ? selectedSprite : normalSprite;
            if (sprite != null) background.sprite = sprite;   // 비어 있으면 원래 스프라이트를 그대로 둔다
            background.color = _on ? selectedColor : normalColor;
        }

        if (label != null) label.color = _on ? selectedLabelColor : normalLabelColor;
        if (icon != null) icon.color = _on ? selectedLabelColor : normalLabelColor;
        if (selectedMark != null) selectedMark.SetActive(_on);

        ApplyHeight(_on ? selectedHeight : normalHeight);
    }

    /// 탭 높이를 바꾼다. 부모 레이아웃 그룹이 세로 정렬을 다시 계산해야 하므로 리빌드를 예약한다.
    void ApplyHeight(float _height)
    {
        if (_height <= 0f) return;                                 // 0/음수 = "높이 연출 안 씀"
        if (Mathf.Approximately(m_rect.sizeDelta.y, _height)) return;

        m_rect.sizeDelta = new Vector2(m_rect.sizeDelta.x, _height);

        // sizeDelta 변경만으로는 부모 그룹이 더러워지지 않는다 — 세로 정렬이 옛 높이로 남는다.
        RectTransform parent = m_rect.parent as RectTransform;
        if (parent != null) LayoutRebuilder.MarkLayoutForRebuild(parent);
    }
}

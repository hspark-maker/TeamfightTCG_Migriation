using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// 탭 버튼 하나의 선택/비선택 겉모습 (탭 버튼 오브젝트에 부착, LobbyTabController가 SetSelected로 구동)
/// "꽉 채운 탭" = 탭들이 탭바를 좌우·상하로 빈틈없이 채우고, 선택 구분은 오직 색과 하단 인디케이터로 한다.
/// 크기는 부모 탭바의 HorizontalLayoutGroup(ChildControl/ForceExpand 모두 켜짐)이 구동하므로
/// 이 컴포넌트는 크기에 손대지 않는다 — 탭을 늘려도 폭이 알아서 나뉜다.
///
/// 비선택 배경을 투명하게 두면 탭바 자체의 배경판이 비쳐 세그먼트 컨트롤처럼 보인다.
/// 스프라이트 슬롯이 비어 있으면 색만 바꾼다 — 나중에 이미지 에셋을 꽂으면 코드 수정 없이 룩이 바뀐다.
public class TabButtonView : MonoBehaviour
{
    [Header("배선 (비우면 자동 탐색)")]
    [SerializeField] Image background;          // 탭 배경. 비우면 자기 자신의 Image
    [SerializeField] TMP_Text label;            // 탭 이름(옵션). 비우면 자식에서 찾는다
    [SerializeField] Image icon;                // 탭 아이콘(옵션) — 색만 상태에 맞춰 바꾼다
    [SerializeField] GameObject selectedMark;   // 선택 표시 장식(하단 인디케이터 등, 옵션). 선택일 때만 켠다

    [Header("선택 상태")]
    [SerializeField] Sprite selectedSprite;                                          // 비우면 스프라이트 유지(색만 적용)
    [SerializeField] Color selectedColor = Color.white;
    [SerializeField] Color selectedLabelColor = new Color(0.15f, 0.18f, 0.22f, 1f);

    [Header("비선택 상태")]
    [SerializeField] Sprite normalSprite;                                            // 비우면 스프라이트 유지(색만 적용)
    [Tooltip("알파 0으로 두면 탭바 배경판이 그대로 비쳐 '가라앉은 탭'이 된다.")]
    [SerializeField] Color normalColor = new Color(1f, 1f, 1f, 0f);
    [SerializeField] Color normalLabelColor = new Color(0.72f, 0.76f, 0.82f, 1f);

    // 현재 상태. 첫 호출은 무조건 적용해야 하므로 nullable로 "아직 없음"을 구분한다.
    bool? m_selected;

    Button m_button;

    /// 이 탭의 Button. 없으면 확보해서 준다 — 탭바가 인스펙터로 버튼을 배선하지 않기 때문이다.
    public Button Button { get { EnsureRefs(); return m_button; } }

    /// 아이콘. 따로 배선하지 않았으면 탭 그래픽 자체가 아이콘이다(지금 저작이 그렇다) —
    /// 알약(LobbyTabBarView.focus)이 선택 탭의 그림을 복사해 갈 때 여기를 본다.
    public Image Icon { get { EnsureRefs(); return this.icon != null ? this.icon : this.background; } }

    void Awake() => EnsureRefs();

    // 인스펙터 배선을 강요하지 않는다 — 탭을 복제해 늘릴 때 손이 덜 간다.
    // 탭바(LobbyTabBarView)와 Awake 순서가 정해져 있지 않아 어느 쪽이 먼저 불러도 같은 결과가 나와야 한다.
    void EnsureRefs()
    {
        if (m_button != null) return;

        if (this.background == null) this.background = GetComponent<Image>();
        if (this.label == null) this.label = GetComponentInChildren<TMP_Text>(true);

        m_button = GetComponent<Button>();
        if (m_button != null) return;

        // 새로 다는 Button은 룩에 손대지 않는다 — 색은 이 컴포넌트(SetSelected)가 단독으로 소유한다.
        m_button = gameObject.AddComponent<Button>();
        m_button.transition = Selectable.Transition.None;
        m_button.targetGraphic = this.background;
    }

    /// 클릭 이벤트는 탭 자신이 소유한다. 탭바는 어떤 인덱스인지만 실어 보낸다.
    public void BindClick(UnityAction _onClick)
    {
        EnsureRefs();
        m_button.onClick.AddListener(_onClick);
    }

    /// 선택 여부에 맞춰 배경·라벨·아이콘을 한 번에 적용한다.
    public void SetSelected(bool _on)
    {
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
    }
}

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// 로비 탭 컨트롤러 (하단 5탭 / 도감 서브탭 등 탭바 공용)
/// 탭 버튼을 누르면 대응하는 콘텐츠 패널만 켜지고 나머지는 꺼진다.
/// 선택 표시는 두 가지 — focus가 배선돼 있으면 Button_Focus(강조 버튼)를 선택된 탭 자리로 옮기고,
/// 비어 있으면 각 버튼의 TabButtonView가 자기 겉모습(높이·색·선택표시)을 바꾼다.
/// 아이콘/상세 배치는 각 콘텐츠 패널과 버튼 안에서 자유롭게 채우면 된다.
public class LobbyTabController : MonoBehaviour
{
    [System.Serializable]
    public class Tab
    {
        public string name;               // 에디터 식별용 라벨
        public Button button;             // 하단바 탭 버튼
        public GameObject content;        // 중앙에 표시할 콘텐츠 패널
        public Image icon;                // 탭 버튼의 아이콘(옵션) — 선택 시 Focus 아이콘으로 스프라이트를 복사한다
        public string label;              // Focus에 표시할 이름(옵션) — 비우면 name을 쓴다
        public EOutgameTutorialAnchor tutorialAnchor;   // 튜토리얼 안내 타깃 키(옵션) — None이면 등록 안 함
    }

    [SerializeField] List<Tab> tabs = new List<Tab>();
    [SerializeField] int defaultIndex = 2; // 시작 시 열릴 탭 (기본 = 경기)

    [Header("Focus (옵션)")]
    [SerializeField] RectTransform focus;   // 선택 탭 자리로 옮길 강조 버튼. 비워두면 Focus 연출 없이 콘텐츠만 토글한다
    [SerializeField] Image focusIcon;       // Focus 안의 아이콘
    [SerializeField] TMP_Text focusLabel;   // Focus 안의 이름 텍스트

    // 탭 버튼의 겉모습 컴포넌트 캐시(없으면 null). 버튼에서 직접 찾으므로 인스펙터 재배선이 필요 없다.
    TabButtonView[] m_views;

    void Awake()
    {
        this.m_views = new TabButtonView[this.tabs.Count];

        for (int i = 0; i < this.tabs.Count; i++)
        {
            int idx = i; // 클로저 캡처 방지
            Button btn = this.tabs[i].button;
            if (btn != null)
            {
                btn.onClick.AddListener(() => this.Select(idx));
                this.m_views[i] = btn.GetComponent<TabButtonView>();
            }

            // 탭 버튼은 Layer Lab 프리팹 인스턴스 내부의 stripped Button이라 TutorialAnchor를 직접 못 붙인다 → 여기서 대신 등록.
            // 선택된 탭 버튼은 Focus에 가려져 잠시 꺼지지만, 오브젝트 자체는 살아 있으므로 Unregister는 불필요하다.
            if (btn != null && this.tabs[i].tutorialAnchor != EOutgameTutorialAnchor.None)
                TutorialAnchorRegistry.Register(this.tabs[i].tutorialAnchor, btn.transform as RectTransform, btn);
        }
    }

    void Start()
    {
        this.Select(this.defaultIndex);
    }

    /// 지정 인덱스 탭만 활성화한다.
    public void Select(int _index)
    {
        bool useFocus = (this.focus != null);

        for (int i = 0; i < this.tabs.Count; i++)
        {
            bool on = (i == _index);
            if (this.tabs[i].content != null) this.tabs[i].content.SetActive(on);

            // Focus가 선택 탭 자리를 대신 차지하므로 그 탭의 일반 버튼은 숨긴다.
            if (useFocus && this.tabs[i].button != null) this.tabs[i].button.gameObject.SetActive(!on);

            // Focus를 안 쓰는 탭바(도감 서브탭 등)는 버튼이 스스로 선택 겉모습을 바꾼다.
            if (!useFocus && this.m_views != null && this.m_views[i] != null) this.m_views[i].SetSelected(on);
        }

        if (useFocus) this.ApplyFocus(_index);
    }

    /// Focus를 선택 탭 자리로 옮기고 아이콘·이름을 그 탭에 맞춘다.
    void ApplyFocus(int _index)
    {
        if (_index < 0 || _index >= this.tabs.Count) return;

        Tab tab = this.tabs[_index];
        if (tab.button == null) return;

        // 비활성 오브젝트도 형제 인덱스는 유지되므로 선택 상태와 무관하게 안전하다.
        this.focus.SetSiblingIndex(tab.button.transform.GetSiblingIndex());

        if (this.focusIcon != null && tab.icon != null) this.focusIcon.sprite = tab.icon.sprite;
        if (this.focusLabel != null) this.focusLabel.text = string.IsNullOrEmpty(tab.label) ? tab.name : tab.label;
    }
}

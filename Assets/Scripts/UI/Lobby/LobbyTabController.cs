using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// 로비 하단 탭 컨트롤러 (레이아웃 템플릿용)
/// 하단바 버튼을 누르면 대응하는 중앙 콘텐츠 패널만 켜지고 나머지는 꺼진다.
/// 아이콘/상세 배치는 각 콘텐츠 패널과 버튼 안에서 자유롭게 채우면 된다.
public class LobbyTabController : MonoBehaviour
{
    [System.Serializable]
    public class Tab
    {
        public string name;               // 에디터 식별용 라벨
        public Button button;             // 하단바 탭 버튼
        public GameObject content;        // 중앙에 표시할 콘텐츠 패널
        public GameObject selectedMark;   // 선택 표시(옵션) — 없으면 무시
        public EOutgameTutorialAnchor tutorialAnchor;   // 튜토리얼 안내 타깃 키(옵션) — None이면 등록 안 함
    }

    [SerializeField] List<Tab> tabs = new List<Tab>();
    [SerializeField] int defaultIndex = 2; // 시작 시 열릴 탭 (기본 = 경기)

    void Awake()
    {
        for (int i = 0; i < this.tabs.Count; i++)
        {
            int idx = i; // 클로저 캡처 방지
            Button btn = this.tabs[i].button;
            if (btn != null) btn.onClick.AddListener(() => this.Select(idx));

            // 탭 버튼은 Layer Lab 프리팹 인스턴스 내부의 stripped Button이라 TutorialAnchor를 직접 못 붙인다 → 여기서 대신 등록.
            // 콘텐츠와 달리 탭 버튼은 항상 활성이므로 Unregister는 불필요하다.
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
        for (int i = 0; i < this.tabs.Count; i++)
        {
            bool on = (i == _index);
            if (this.tabs[i].content != null) this.tabs[i].content.SetActive(on);
            if (this.tabs[i].selectedMark != null) this.tabs[i].selectedMark.SetActive(on);
        }
    }
}

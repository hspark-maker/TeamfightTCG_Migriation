using System;
using System.Collections.Generic;
using DG.Tweening;
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
        public EOutgameTutorialTrigger tutorialTrigger; // 첫 진입 1회 튜토리얼 발화 키(옵션) — None이면 발화 안 함
        public EOutgameFeature unlockFeature;           // 이 탭을 여는 기능 키(옵션) — None이면 항상 열림
    }

    [SerializeField] List<Tab> tabs = new List<Tab>();
    [SerializeField] int defaultIndex = 2; // 시작 시 열릴 탭 (기본 = 경기)

    [Header("Focus (옵션)")]
    [SerializeField] RectTransform focus;   // 선택 탭 자리로 옮길 강조 버튼. 비워두면 Focus 연출 없이 콘텐츠만 토글한다
    [SerializeField] Image focusIcon;       // Focus 안의 아이콘
    [SerializeField] TMP_Text focusLabel;   // Focus 안의 이름 텍스트

    [Header("튜토리얼 알림 점 (옵션)")]
    [Tooltip("tutorialTrigger가 배선된 탭 버튼에 얹을 점 프리팹(Notify_Point).\n" +
             "비우면 알림 점을 그리지 않는다 — 트리거를 안 쓰는 탭바(도감 서브탭 등)는 그대로 비워 둔다.")]
    [SerializeField] GameObject alertDotPrefab;

    [Header("Focus 하이라이트 회전")]
    [Tooltip("Button_Focus 뒤쪽 하이라이트. 비우면 Focus의 Light 자식을 자동으로 찾는다.")]
    [SerializeField] RectTransform focusHighlight;

    [Tooltip("하이라이트가 한 바퀴 도는 시간(초). 타임스케일 영향을 받지 않는다.")]
    [SerializeField] float focusSpinSeconds = 6f;

    [SerializeField] bool focusSpinClockwise = true;

    [Header("선택 확대 (탭바가 HorizontalLayoutGroup일 때만 의미 있음)")]
    [Tooltip("끄면 폭을 전혀 건드리지 않는다(종전 동작).")]
    [SerializeField] bool animateTabWidth = true;

    [Tooltip("선택된 칸이 가져가는 폭 가중치. 나머지 칸이 남은 몫을 똑같이 나눠 가지므로 **합은 항상 탭 수**다.\n" +
             "탭 5개에 1.25면 나머지는 각 0.9375 — 1080 폭 기준 선택 270 / 나머지 202.5.\n" +
             "1이면 전부 균등(확대 없음). 크게 할수록 선택 칸이 커지고 나머지가 같이 좁아진다.")]
    [Range(1f, 2f)] [SerializeField] float selectedWidthWeight = 1.25f;

    [Tooltip("폭이 바뀌는 시간(초). 타임스케일 영향을 받지 않는다.")]
    [SerializeField] float widthTweenDuration = 0.2f;

    // 직전 선택 탭. 전환 때 "큰 폭에서 작은 폭으로 줄어드는" 쪽을 알아야 확대 영역이 순간이동하지 않는다.
    int m_prevIndex = -1;

    // 탭 버튼의 겉모습 컴포넌트 캐시(없으면 null). 버튼에서 직접 찾으므로 인스펙터 재배선이 필요 없다.
    TabButtonView[] m_views;

    // 현재 탭이 건 이탈 가드(없으면 null). 셸은 무엇을 확인하는지 모르고 Action<Action> 하나만 안다.
    Action<Action> m_leaveGuard;

    /// 탭 전환을 가로챌 이탈 가드를 건다. 가드는 이탈이 허가된 시점에 넘겨받은 _proceed를 부른다 —
    /// 그때는 스스로 해제돼 있어야 한다(안 그러면 _proceed가 다시 가드로 들어와 무한히 맴돈다).
    public void SetLeaveGuard(Action<Action> _guard) => this.m_leaveGuard = _guard;

    public void ClearLeaveGuard() => this.m_leaveGuard = null;

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

            // 잠금 표시도 같은 이유로 여기서 얹는다(프리팹에 못 붙이는 버튼이라).
            // 진입 차단은 Select의 IsTabLocked가 따로 하므로 여기는 룩만 붙인다.
            if (btn != null) FeatureLockView.Attach(btn.gameObject, this.tabs[i].unlockFeature);

            // 아직 안 본 트리거 튜토리얼이 남은 탭에 알림 점. 잠김 표시와 같은 이유로 여기서 얹는다.
            // 붙는 자리는 버튼이 아니라 아이콘이다 — 버튼 칸은 선택에 따라 폭이 늘었다 줄어서
            // 그 우상단에 매달면 점이 같이 밀려다닌다(아이콘은 가운데 고정이라 자리가 안 흔들린다).
            if (this.alertDotPrefab != null && this.tabs[i].tutorialTrigger != EOutgameTutorialTrigger.None
                && this.tabs[i].icon != null)
                this.tabs[i].icon.gameObject.AddComponent<TutorialAlertDot>()
                    .Bind(this.tabs[i].tutorialTrigger, this.tabs[i].unlockFeature, this.alertDotPrefab);
        }
    }

    void OnEnable()
    {
        this.StartFocusHighlightSpin();
    }

    void OnDisable()
    {
        if (this.focusHighlight != null) DOTween.Kill(this.focusHighlight);
    }

    void Start()
    {
        // 부팅 시 기본 탭 선택은 유저의 "첫 진입"이 아니다 — 발화시키면 아무도 누르지 않은 탭의 튜토리얼이 낭비된다
        // (도감 서브탭 인스턴스의 초기 선택도 같은 이유로 막힌다).
        this.Select(this.defaultIndex, false);
    }

    /// 지정 인덱스 탭만 활성화한다. _fireTrigger=false는 유저 이동이 아닌 초기 선택용.
    public void Select(int _index, bool _fireTrigger = true)
    {
        // 아직 안 열린 탭은 유저 이동으로 들어갈 수 없다(잠김 오버레이·interactable과 이중 안전).
        // 초기 선택은 통과시킨다 — 기본 탭이 잠기면 아무 콘텐츠도 안 열린 로비가 되어 더 나쁘다.
        if (_fireTrigger && this.IsTabLocked(_index)) return;

        // 잠금 검사 뒤에 둔다 — 어차피 못 들어가는 탭 때문에 "나갈까요?"를 묻게 할 이유가 없다.
        // 전환 권한을 통째로 넘기고 여기서는 끝낸다. 허가되면 가드가 이 호출을 그대로 다시 부른다.
        if (this.m_leaveGuard != null)
        {
            this.m_leaveGuard(() => this.Select(_index, _fireTrigger));

            return;
        }

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

        // 폭은 Focus를 옮긴 **뒤에** 잡는다 — 자리(형제 순서)가 확정돼야 어느 칸이 커지는지가 맞는다.
        // 첫 선택(부팅)은 트윈 없이 즉시 — 로비가 열리자마자 탭바가 벌어지는 그림은 연출이 아니라 결함으로 읽힌다.
        this.ApplyTabWidths(_index, _animate: this.m_prevIndex >= 0 && this.m_prevIndex != _index);
        this.m_prevIndex = _index;

        // 콘텐츠를 켠 뒤에 발화한다 — 안내 타깃(앵커)이 그제서야 등록된다.
        if (_fireTrigger && _index >= 0 && _index < this.tabs.Count)
            TriggeredTutorialRunner.Fire(this.tabs[_index].tutorialTrigger);
    }

    /// 선택 칸은 넓히고 나머지는 남은 몫을 균등하게 나눠 갖는다. 합이 탭 수로 고정이라 탭바 전체 폭은 그대로다.
    ///
    /// <b>scale이 아니라 폭</b>인 이유: HorizontalLayoutGroup은 자식 localScale을 배치에 반영하지 않는다.
    /// 확대만 하면 이웃과 겹치고, 무엇보다 <b>클릭 판정은 원래 폭 그대로 남아</b> 보이는 것과 눌리는 곳이 어긋난다.
    /// 폭을 움직이면 배치·클릭·튜토리얼 앵커가 전부 같은 RectTransform 하나를 따라간다.
    void ApplyTabWidths(int _index, bool _animate)
    {
        if (!this.animateTabWidth || this.tabs.Count <= 1) return;

        float t_selected = Mathf.Clamp(this.selectedWidthWeight, 0.01f, this.tabs.Count - 0.01f);
        float t_normal   = (this.tabs.Count - t_selected) / (this.tabs.Count - 1);
        bool  t_useFocus = this.focus != null;

        // 전환 중 연타해도 현재 레이아웃 모양을 그대로 이어받는다. Focus의 현재 폭은 다시 켜지는
        // 직전 버튼으로, 새로 꺼지는 선택 버튼의 현재 폭은 이동한 Focus로 넘겨야 활성 슬롯 합이 튀지 않는다.
        float t_previousFocusWeight = t_useFocus ? GetWidthWeight(this.focus, t_selected) : t_selected;
        float t_newSlotWeight = t_normal;
        if (t_useFocus && _index >= 0 && _index < this.tabs.Count && this.tabs[_index].button != null)
            t_newSlotWeight = GetWidthWeight(this.tabs[_index].button.transform as RectTransform, t_normal);

        for (int i = 0; i < this.tabs.Count; i++)
        {
            RectTransform t_slot = this.tabs[i].button != null
                ? this.tabs[i].button.transform as RectTransform
                : null;
            if (t_slot == null) continue;

            // Focus를 쓰면 선택 탭의 버튼은 꺼져 있어 배치에 끼지 않는다. 대신 **직전 선택 버튼**이
            // 방금 다시 켜졌으므로, Focus가 비우고 간 큰 폭에서 시작해 보통 폭으로 줄어들게 한다.
            if (t_useFocus && i == _index) continue;

            bool t_shrinking = t_useFocus && i == this.m_prevIndex && _animate;
            if (t_shrinking) SetWidthWeight(t_slot, t_previousFocusWeight);

            TweenWidthWeight(t_slot, t_normal, _animate);
        }

        // 선택 칸: Focus가 있으면 Focus가, 없으면 그 탭 버튼이 커지는 쪽이다.
        RectTransform t_grow = t_useFocus
            ? this.focus
            : (_index >= 0 && _index < this.tabs.Count && this.tabs[_index].button != null
                ? this.tabs[_index].button.transform as RectTransform
                : null);
        if (t_grow == null) return;

        // Focus는 자리를 옮겨 왔을 뿐 폭은 직전 탭의 큰 값을 그대로 들고 있다 —
        // 보통 폭에서 다시 출발시켜야 "밀려서 커진다"로 읽힌다(안 그러면 확대 영역이 순간이동한다).
        if (t_useFocus && _animate) SetWidthWeight(t_grow, t_newSlotWeight);

        TweenWidthWeight(t_grow, t_selected, _animate);
    }

    /// 폭을 **가중치만으로** 정하게 만든다. preferred/min을 0으로 눕히지 않으면 각 버튼 Image의
    /// 네이티브 크기가 바닥값으로 남아, 가중치를 아무리 바꿔도 비율대로 안 움직인다.
    static LayoutElement EnsureWidthSlot(RectTransform _slot)
    {
        var t_element = _slot.GetComponent<LayoutElement>();
        if (t_element == null) t_element = _slot.gameObject.AddComponent<LayoutElement>();

        if (t_element.minWidth != 0f)       t_element.minWidth = 0f;
        if (t_element.preferredWidth != 0f) t_element.preferredWidth = 0f;

        return t_element;
    }

    static void SetWidthWeight(RectTransform _slot, float _weight)
    {
        LayoutElement t_element = EnsureWidthSlot(_slot);
        DOTween.Kill(t_element);
        t_element.flexibleWidth = _weight;
    }

    static float GetWidthWeight(RectTransform _slot, float _fallback)
    {
        if (_slot == null) return _fallback;

        LayoutElement t_element = _slot.GetComponent<LayoutElement>();
        return t_element != null && t_element.flexibleWidth >= 0f
            ? t_element.flexibleWidth
            : _fallback;
    }

    void TweenWidthWeight(RectTransform _slot, float _weight, bool _animate)
    {
        LayoutElement t_element = EnsureWidthSlot(_slot);
        DOTween.Kill(t_element);   // 연타로 트윈이 쌓이면 폭이 중간값에서 덜컥거린다

        if (!_animate || Mathf.Approximately(t_element.flexibleWidth, _weight))
        {
            t_element.flexibleWidth = _weight;
            return;
        }

        float t_from = t_element.flexibleWidth;
        DOTween.To(() => t_from, _v => { t_from = _v; t_element.flexibleWidth = _v; }, _weight,
                   Mathf.Max(0.01f, this.widthTweenDuration))
               .SetEase(Ease.OutCubic)
               .SetUpdate(true)            // 로비 연출이 timeScale에 끌려가지 않게
               .SetTarget(t_element)
               .SetLink(_slot.gameObject);
    }

    void StartFocusHighlightSpin()
    {
        if (this.focusHighlight == null && this.focus != null)
            this.focusHighlight = this.focus.Find("Light") as RectTransform;
        if (this.focusHighlight == null) return;

        DOTween.Kill(this.focusHighlight);
        this.focusHighlight.localRotation = Quaternion.identity;
        float t_angle = this.focusSpinClockwise ? -360f : 360f;
        this.focusHighlight
            .DOLocalRotate(new Vector3(0f, 0f, t_angle), Mathf.Max(0.01f, this.focusSpinSeconds), RotateMode.FastBeyond360)
            .SetLoops(-1, LoopType.Restart)
            .SetEase(Ease.Linear)
            .SetUpdate(true)
            .SetTarget(this.focusHighlight)
            .SetLink(this.focusHighlight.gameObject);
    }

    bool IsTabLocked(int _index)
    {
        if (_index < 0 || _index >= this.tabs.Count) return false;

        return !OutgameFeatureLock.IsUnlocked(this.tabs[_index].unlockFeature);
    }

    /// Focus를 선택 탭 자리로 옮기고 아이콘·이름을 그 탭에 맞춘다.
    void ApplyFocus(int _index)
    {
        if (_index < 0 || _index >= this.tabs.Count) return;

        Tab tab = this.tabs[_index];
        if (tab.button == null) return;

        // 비활성 오브젝트도 형제 인덱스는 유지되므로 선택 상태와 무관하게 안전하다.
        this.focus.SetSiblingIndex(tab.button.transform.GetSiblingIndex());

        // 아이콘은 스프라이트마다 크기가 달라서 Focus 쪽 고정 크기를 쓰면 찌그러진다 → 대상 버튼 아이콘의 크기를 그대로 가져온다.
        if (this.focusIcon != null && tab.icon != null)
        {
            this.focusIcon.sprite = tab.icon.sprite;
            this.focusIcon.rectTransform.sizeDelta = tab.icon.rectTransform.sizeDelta;
        }
        if (this.focusLabel != null) this.focusLabel.text = string.IsNullOrEmpty(tab.label) ? tab.name : tab.label;
    }
}

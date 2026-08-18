using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Coordinates tab policy; panels own lifecycle and the tab bar owns visuals.</summary>
public class LobbyTabController : MonoBehaviour
{
    [Serializable]
    public class Tab
    {
        public string name;
        public LobbyTabPanel panel;
        [HideInInspector] public Button button;
        [HideInInspector] public GameObject content;
        [HideInInspector] public Image icon;
        public string label;
        public EOutgameTutorialAnchor tutorialAnchor;
        public EOutgameTutorialTrigger tutorialTrigger;
        public EOutgameFeature unlockFeature;
    }

    [SerializeField] LobbyTabBarView tabBar;
    [SerializeField] List<Tab> tabs = new List<Tab>();
    [SerializeField] int defaultIndex = 2;
    [SerializeField] GameObject alertDotPrefab;

    // Expand-phase compatibility: keep serialized values until LobbyTabBarView is wired.
    [HideInInspector] [SerializeField] RectTransform focus;
    [HideInInspector] [SerializeField] Image focusIcon;
    [HideInInspector] [SerializeField] TMP_Text focusLabel;
    [HideInInspector] [SerializeField] RectTransform focusHighlight;
    [HideInInspector] [SerializeField] float focusSpinSeconds = 6f;
    [HideInInspector] [SerializeField] bool focusSpinClockwise = true;
    [HideInInspector] [SerializeField] bool animateTabWidth = true;
    [HideInInspector] [Range(1f, 2f)] [SerializeField] float selectedWidthWeight = 1.25f;
    [HideInInspector] [SerializeField] float widthTweenDuration = 0.2f;

    int m_currentIndex = -1;
    int m_previousIndex = -1;
    TabButtonView[] m_legacyViews;
    Action<Action> m_legacyLeaveGuard;

    public LobbyTabPanel CurrentPanel
        => m_currentIndex >= 0 && m_currentIndex < tabs.Count ? tabs[m_currentIndex].panel : null;

    void Awake()
    {
        if (tabBar != null) tabBar.Selected += HandleTabSelected;
        m_legacyViews = new TabButtonView[tabs.Count];
        for (int i = 0; i < tabs.Count; i++)
        {
            Tab t_tab = tabs[i];
            string t_label = string.IsNullOrEmpty(t_tab.label) ? t_tab.name : t_tab.label;
            if (tabBar != null)
                tabBar.ConfigureItem(i, t_label, t_tab.tutorialAnchor, t_tab.tutorialTrigger,
                    t_tab.unlockFeature, alertDotPrefab);
            else
                ConfigureLegacyTab(i, t_tab);
        }
    }

    void OnEnable()
    {
        if (tabBar == null) StartLegacyFocusSpin();
    }

    void OnDisable()
    {
        if (tabBar == null && focusHighlight != null) DOTween.Kill(focusHighlight);
    }

    void OnDestroy()
    {
        if (tabBar != null) tabBar.Selected -= HandleTabSelected;
    }

    void Start() => Select(defaultIndex, false);

    void HandleTabSelected(int _index) => Select(_index);

    [Obsolete("Leave decisions belong to LobbyTabPanel.RequestLeave.")]
    public void SetLeaveGuard(Action<Action> _guard) => m_legacyLeaveGuard = _guard;

    [Obsolete("Leave decisions belong to LobbyTabPanel.RequestLeave.")]
    public void ClearLeaveGuard() => m_legacyLeaveGuard = null;

    public void Select(LobbyTabPanel _panel, bool _fireTrigger = true)
    {
        int t_index = tabs.FindIndex(_tab => _tab.panel == _panel);
        if (t_index >= 0) Select(t_index, _fireTrigger);
    }

    public void Select(int _index, bool _fireTrigger = true)
    {
        if (_index < 0 || _index >= tabs.Count) return;
        if (_fireTrigger && !OutgameFeatureLock.IsUnlocked(tabs[_index].unlockFeature)) return;
        if (_index == m_currentIndex)
        {
            tabBar?.SetSelected(_index);
            return;
        }

        LobbyTabPanel t_current = CurrentPanel;
        if (t_current == null && m_legacyLeaveGuard != null)
        {
            m_legacyLeaveGuard(() => Select(_index, _fireTrigger));
            return;
        }
        if (t_current == null)
        {
            CommitSelection(_index, _fireTrigger);
            return;
        }

        t_current.RequestLeave(() =>
        {
            if (!this) return;
            CommitSelection(_index, _fireTrigger);
        });
    }

    void CommitSelection(int _index, bool _fireTrigger)
    {
        LobbyTabPanel t_previous = CurrentPanel;
        if (t_previous != null)
        {
            t_previous.gameObject.SetActive(false);
            t_previous.OnLeave();
        }

        for (int i = 0; i < tabs.Count; i++)
        {
            LobbyTabPanel t_panel = tabs[i].panel;
            if (t_panel != null && i != _index) t_panel.gameObject.SetActive(false);
            if (t_panel == null && tabs[i].content != null) tabs[i].content.SetActive(i == _index);
        }

        m_currentIndex = _index;
        LobbyTabPanel t_next = CurrentPanel;
        if (t_next != null)
        {
            t_next.gameObject.SetActive(true);
            t_next.OnEnter();
        }

        if (tabBar != null) tabBar.SetSelected(_index);
        else ApplyLegacySelection(_index);
        if (_fireTrigger) TriggeredTutorialRunner.Fire(tabs[_index].tutorialTrigger);
    }

    void ConfigureLegacyTab(int _index, Tab _tab)
    {
        Button t_button = _tab.button;
        if (t_button == null) return;
        int t_index = _index;
        t_button.onClick.AddListener(() => Select(t_index));
        m_legacyViews[_index] = t_button.GetComponent<TabButtonView>();
        if (_tab.tutorialAnchor != EOutgameTutorialAnchor.None)
            TutorialAnchorRegistry.Register(_tab.tutorialAnchor, t_button.transform as RectTransform, t_button);
        FeatureLockView.Attach(t_button.gameObject, _tab.unlockFeature);
        if (alertDotPrefab != null && _tab.tutorialTrigger != EOutgameTutorialTrigger.None && _tab.icon != null
            && _tab.icon.GetComponent<TutorialAlertDot>() == null)
            _tab.icon.gameObject.AddComponent<TutorialAlertDot>()
                .Bind(_tab.tutorialTrigger, _tab.unlockFeature, alertDotPrefab);
    }

    void ApplyLegacySelection(int _index)
    {
        bool t_useFocus = focus != null;
        for (int i = 0; i < tabs.Count; i++)
        {
            bool t_on = i == _index;
            if (t_useFocus && tabs[i].button != null) tabs[i].button.gameObject.SetActive(!t_on);
            if (!t_useFocus && m_legacyViews[i] != null) m_legacyViews[i].SetSelected(t_on);
        }
        if (t_useFocus) ApplyLegacyFocus(_index);
        ApplyLegacyWidths(_index, m_previousIndex >= 0 && m_previousIndex != _index);
        m_previousIndex = _index;
    }

    void ApplyLegacyFocus(int _index)
    {
        Tab t_tab = tabs[_index];
        if (t_tab.button == null) return;
        focus.SetSiblingIndex(t_tab.button.transform.GetSiblingIndex());
        if (focusIcon != null && t_tab.icon != null)
        {
            focusIcon.sprite = t_tab.icon.sprite;
            focusIcon.rectTransform.sizeDelta = t_tab.icon.rectTransform.sizeDelta;
        }
        if (focusLabel != null) focusLabel.text = string.IsNullOrEmpty(t_tab.label) ? t_tab.name : t_tab.label;
    }

    void ApplyLegacyWidths(int _index, bool _animate)
    {
        if (!animateTabWidth || tabs.Count <= 1) return;
        float t_selected = Mathf.Clamp(selectedWidthWeight, 0.01f, tabs.Count - 0.01f);
        float t_normal = (tabs.Count - t_selected) / (tabs.Count - 1);
        bool t_useFocus = focus != null;
        float t_previousFocusWeight = t_useFocus ? GetWidthWeight(focus, t_selected) : t_selected;
        RectTransform t_newSlot = tabs[_index].button != null ? tabs[_index].button.transform as RectTransform : null;
        float t_newSlotWeight = t_useFocus ? GetWidthWeight(t_newSlot, t_normal) : t_normal;
        for (int i = 0; i < tabs.Count; i++)
        {
            RectTransform t_slot = tabs[i].button != null ? tabs[i].button.transform as RectTransform : null;
            if (t_slot == null || (t_useFocus && i == _index)) continue;
            if (t_useFocus && i == m_previousIndex && _animate) SetWidthWeight(t_slot, t_previousFocusWeight);
            TweenWidthWeight(t_slot, t_normal, _animate);
        }
        RectTransform t_grow = t_useFocus ? focus : t_newSlot;
        if (t_grow == null) return;
        if (t_useFocus && _animate) SetWidthWeight(t_grow, t_newSlotWeight);
        TweenWidthWeight(t_grow, t_selected, _animate);
    }

    static LayoutElement EnsureWidthSlot(RectTransform _slot)
    {
        LayoutElement t_element = _slot.GetComponent<LayoutElement>();
        if (t_element == null) t_element = _slot.gameObject.AddComponent<LayoutElement>();
        t_element.minWidth = 0f;
        t_element.preferredWidth = 0f;
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
        LayoutElement t_element = _slot != null ? _slot.GetComponent<LayoutElement>() : null;
        return t_element != null && t_element.flexibleWidth >= 0f ? t_element.flexibleWidth : _fallback;
    }

    void TweenWidthWeight(RectTransform _slot, float _weight, bool _animate)
    {
        LayoutElement t_element = EnsureWidthSlot(_slot);
        DOTween.Kill(t_element);
        if (!_animate || Mathf.Approximately(t_element.flexibleWidth, _weight))
        {
            t_element.flexibleWidth = _weight;
            return;
        }
        float t_from = t_element.flexibleWidth;
        DOTween.To(() => t_from, _v => { t_from = _v; t_element.flexibleWidth = _v; }, _weight,
                Mathf.Max(0.01f, widthTweenDuration))
            .SetEase(Ease.OutCubic).SetUpdate(true).SetTarget(t_element).SetLink(_slot.gameObject);
    }

    void StartLegacyFocusSpin()
    {
        if (focusHighlight == null && focus != null) focusHighlight = focus.Find("Light") as RectTransform;
        if (focusHighlight == null) return;
        DOTween.Kill(focusHighlight);
        focusHighlight.localRotation = Quaternion.identity;
        float t_angle = focusSpinClockwise ? -360f : 360f;
        focusHighlight.DOLocalRotate(new Vector3(0f, 0f, t_angle), Mathf.Max(0.01f, focusSpinSeconds), RotateMode.FastBeyond360)
            .SetLoops(-1, LoopType.Restart).SetEase(Ease.Linear).SetUpdate(true)
            .SetTarget(focusHighlight).SetLink(focusHighlight.gameObject);
    }
}

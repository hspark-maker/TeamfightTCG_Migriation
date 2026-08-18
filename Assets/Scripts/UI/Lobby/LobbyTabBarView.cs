using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Owns every visual and input detail inside the lobby bottom tab bar.</summary>
public sealed class LobbyTabBarView : MonoBehaviour
{
    [Serializable]
    sealed class Item
    {
        public Button button;
        public Image icon;
    }

    [SerializeField] List<Item> items = new List<Item>();
    [SerializeField] RectTransform focus;
    [SerializeField] Image focusIcon;
    [SerializeField] TMP_Text focusLabel;
    [SerializeField] RectTransform focusHighlight;
    [SerializeField] float focusSpinSeconds = 6f;
    [SerializeField] bool focusSpinClockwise = true;
    [SerializeField] bool animateTabWidth = true;
    [Range(1f, 2f)] [SerializeField] float selectedWidthWeight = 1.25f;
    [SerializeField] float widthTweenDuration = 0.2f;

    readonly List<string> m_labels = new List<string>();
    TabButtonView[] m_views;
    int m_previousIndex = -1;

    public event Action<int> Selected;
    public int Count => items.Count;

    void Awake()
    {
        m_views = new TabButtonView[items.Count];
        EnsureLabelCapacity(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            int t_index = i;
            Button t_button = items[i].button;
            if (t_button == null) continue;
            t_button.onClick.AddListener(() => Selected?.Invoke(t_index));
            m_views[i] = t_button.GetComponent<TabButtonView>();
        }
    }

    void OnEnable() => StartFocusHighlightSpin();

    void OnDisable()
    {
        if (focusHighlight != null) DOTween.Kill(focusHighlight);
    }

    public void ConfigureItem(int _index, string _label, EOutgameTutorialAnchor _anchor,
        EOutgameTutorialTrigger _trigger, EOutgameFeature _feature, GameObject _alertDotPrefab)
    {
        if (!TryGetItem(_index, out Item t_item)) return;
        EnsureLabelCapacity(items.Count);
        m_labels[_index] = _label;

        if (t_item.button != null)
        {
            if (_anchor != EOutgameTutorialAnchor.None)
                TutorialAnchorRegistry.Register(_anchor, t_item.button.transform as RectTransform, t_item.button);
            FeatureLockView.Attach(t_item.button.gameObject, _feature);
        }

        if (_alertDotPrefab != null && _trigger != EOutgameTutorialTrigger.None && t_item.icon != null
            && t_item.icon.GetComponent<TutorialAlertDot>() == null)
            t_item.icon.gameObject.AddComponent<TutorialAlertDot>().Bind(_trigger, _feature, _alertDotPrefab);
    }

    public RectTransform GetButtonAnchor(int _index)
        => TryGetItem(_index, out Item t_item) && t_item.button != null
            ? t_item.button.transform as RectTransform
            : null;

    public void SetSelected(int _index)
    {
        if (!TryGetItem(_index, out _)) return;
        bool t_useFocus = focus != null;
        for (int i = 0; i < items.Count; i++)
        {
            bool t_on = i == _index;
            Button t_button = items[i].button;
            if (t_useFocus && t_button != null) t_button.gameObject.SetActive(!t_on);
            if (!t_useFocus && m_views != null && m_views[i] != null) m_views[i].SetSelected(t_on);
        }

        if (t_useFocus) ApplyFocus(_index);
        ApplyTabWidths(_index, m_previousIndex >= 0 && m_previousIndex != _index);
        m_previousIndex = _index;
    }

    void ApplyFocus(int _index)
    {
        if (!TryGetItem(_index, out Item t_item) || t_item.button == null) return;
        focus.SetSiblingIndex(t_item.button.transform.GetSiblingIndex());
        if (focusIcon != null && t_item.icon != null)
        {
            focusIcon.sprite = t_item.icon.sprite;
            focusIcon.rectTransform.sizeDelta = t_item.icon.rectTransform.sizeDelta;
        }
        if (focusLabel != null) focusLabel.text = m_labels[_index];
    }

    void ApplyTabWidths(int _index, bool _animate)
    {
        if (!animateTabWidth || items.Count <= 1) return;
        float t_selected = Mathf.Clamp(selectedWidthWeight, 0.01f, items.Count - 0.01f);
        float t_normal = (items.Count - t_selected) / (items.Count - 1);
        bool t_useFocus = focus != null;
        float t_previousFocusWeight = t_useFocus ? GetWidthWeight(focus, t_selected) : t_selected;
        float t_newSlotWeight = t_normal;
        RectTransform t_newSlot = GetButtonAnchor(_index);
        if (t_useFocus && t_newSlot != null) t_newSlotWeight = GetWidthWeight(t_newSlot, t_normal);

        for (int i = 0; i < items.Count; i++)
        {
            RectTransform t_slot = GetButtonAnchor(i);
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

    void StartFocusHighlightSpin()
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

    bool TryGetItem(int _index, out Item _item)
    {
        _item = _index >= 0 && _index < items.Count ? items[_index] : null;
        return _item != null;
    }

    void EnsureLabelCapacity(int _count)
    {
        while (m_labels.Count < _count) m_labels.Add(string.Empty);
    }
}

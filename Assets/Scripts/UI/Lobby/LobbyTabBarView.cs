using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Owns every visual and input detail inside the lobby bottom tab bar.</summary>
public sealed class LobbyTabBarView : MonoBehaviour
{
    [SerializeField] RectTransform focus;
    [SerializeField] Image focusIcon;
    [SerializeField] TMP_Text focusLabel;
    [SerializeField] RectTransform focusHighlight;
    [SerializeField] float focusSpinSeconds = 6f;
    [SerializeField] bool focusSpinClockwise = true;
    [Tooltip("알약이 선택 탭 자리로 미끄러지는 시간(초). 0이면 즉시 이동")]
    [SerializeField] float focusSlideSeconds = 0.35f;

    /// <summary>알약 아이콘 표시 박스. 탭 아이콘 크기를 따라가지 않는다.</summary>
    static readonly Vector2 FOCUS_ICON_SIZE = new Vector2(150f, 150f);

    readonly List<string> m_labels = new List<string>();
    TabButtonView[] m_views;
    int m_previousIndex = -1;
    bool m_focusResolved;

    public event Action<int> Selected;
    public int Count { get { EnsureViews(); return m_views.Length; } }

    void Awake() => EnsureViews();

    /// <summary>탭 목록은 인스펙터 배선이 아니라 자식 계층의 TabButtonView를 훑어 만든다 —
    /// 클릭 이벤트는 각 탭이 스스로 소유하고(BindClick), 여기는 계층 순서로 인덱스만 정한다.
    /// 그래서 탭 순서 = 계층 순서 = LobbyTabController.tabs 순서가 계약이다.
    ///
    /// Awake에서만 모으면 안 된다 — LobbyTabController.Awake가 먼저 돌아 ConfigureItem을 부를 수 있다.</summary>
    void EnsureViews()
    {
        if (m_views != null) return;

        m_views = GetComponentsInChildren<TabButtonView>(true);
        EnsureLabelCapacity(m_views.Length);

        for (int i = 0; i < m_views.Length; i++)
        {
            int t_index = i;
            m_views[i].BindClick(() => Selected?.Invoke(t_index));
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
        if (!TryGetView(_index, out TabButtonView t_view)) return;
        EnsureLabelCapacity(m_views.Length);
        m_labels[_index] = _label;

        Button t_button = t_view.Button;
        if (t_button != null)
        {
            if (_anchor != EOutgameTutorialAnchor.None)
                TutorialAnchorRegistry.Register(_anchor, t_button.transform as RectTransform, t_button);
            FeatureLockView.Attach(t_button.gameObject, _feature);
        }

        // 아이콘 배선은 옵션이다 — 비어 있으면 탭 자신이 알림 점의 자리다(탭 그래픽이 곧 아이콘인 저작).
        GameObject t_dotHost = t_view.Icon != null ? t_view.Icon.gameObject : t_view.gameObject;
        if (_alertDotPrefab != null && _trigger != EOutgameTutorialTrigger.None
            && t_dotHost.GetComponent<TutorialAlertDot>() == null)
            t_dotHost.AddComponent<TutorialAlertDot>().Bind(_trigger, _feature, _alertDotPrefab);
    }

    public RectTransform GetButtonAnchor(int _index)
        => TryGetView(_index, out TabButtonView t_view)
            ? t_view.transform as RectTransform
            : null;

    public RectTransform GetVisualAnchor(int _index)
        => focus != null && m_previousIndex == _index
            ? focus
            : GetButtonAnchor(_index);

    /// <summary>도착 강조(UiPunch)를 받을 그래픽. 시각 앵커와 따로 두는 이유는 탭 버튼에
    /// 자물쇠 배지 같은 런타임 자식이 붙기 때문이다 — 자식 순서로 짚으면 그쪽이 대신 튄다.</summary>
    public RectTransform GetPunchAnchor(int _index)
    {
        if (focus != null && m_previousIndex == _index)
            return focusIcon != null ? focusIcon.rectTransform : focus;

        if (!TryGetView(_index, out TabButtonView t_view)) return null;

        Image t_icon = t_view.Icon;
        return t_icon != null ? t_icon.rectTransform : t_view.transform as RectTransform;
    }

    public void SetSelected(int _index)
    {
        if (!TryGetView(_index, out _)) return;
        bool t_useFocus = focus != null;
        bool t_animate = m_previousIndex >= 0 && m_previousIndex != _index;

        for (int i = 0; i < m_views.Length; i++)
        {
            bool t_on = i == _index;
            // 알약이 선택 탭 자리를 대신 채운다 — 원래 탭을 남겨 두면 알약 밑으로 겹쳐 보인다.
            if (t_useFocus) m_views[i].gameObject.SetActive(!t_on);
            else            m_views[i].SetSelected(t_on);
        }

        if (t_useFocus) ApplyFocus(_index, t_animate);
        m_previousIndex = _index;
    }

    void ApplyFocus(int _index, bool _animate)
    {
        if (!TryGetView(_index, out TabButtonView t_view)) return;

        EnsureFocusParts();

        var t_slot = t_view.transform as RectTransform;

        // 레이아웃 그룹이 없으니 형제 순서는 자리가 아니라 그리는 순서다 — 알약은 늘 맨 위여야 한다.
        // (탭 인덱스 자리로 옮기면 뒤 형제 탭들이 알약 위에 겹쳐 그려진다. 알약이 탭보다 넓다.)
        focus.SetAsLastSibling();

        Image t_icon = t_view.Icon;
        if (focusIcon != null && t_icon != null)
        {
            focusIcon.sprite = t_icon.sprite;
            // 크기는 탭에서 복사하지 않는다 — 탭마다 다른 sizeDelta·localScale이 그대로 옮겨와
            // 알약 아이콘 폭이 두 배 넘게 벌어졌다. 고정 박스로 통일하고 스케일도 1로 눕힌다.
            focusIcon.rectTransform.sizeDelta = FOCUS_ICON_SIZE;
            focusIcon.rectTransform.localScale = Vector3.one;
        }
        if (focusLabel != null) focusLabel.text = m_labels[_index];

        SlideFocusTo(t_slot, _animate);
    }

    /// <summary>알약을 선택 탭 자리로 옮긴다.
    ///
    /// 레이아웃 그룹을 쓰지 않는다 — 씬 쪽 오버라이드가 계속 끼어들어 걷어낸 구조라, 알약 위치는
    /// 코드가 단독으로 소유한다. 탭은 저작된 고정 좌표에 그대로 있고 알약만 그 위로 미끄러진다.
    ///
    /// x만 맞춘다. y는 저작값을 지킨다 — 알약은 바 위로 솟은 디자인이라 탭과 높이가 다르다.
    /// 앵커가 탭마다 달라(어떤 탭은 좌상단, 어떤 탭은 중앙) 공통 기준인 localPosition으로 차이를 재고
    /// 그만큼 anchoredPosition을 민다. 트윈 중에 다시 불려도 현재 값에서 다시 재므로 어긋나지 않는다.</summary>
    void SlideFocusTo(RectTransform _slot, bool _animate)
    {
        float t_x = focus.anchoredPosition.x + (_slot.localPosition.x - focus.localPosition.x);

        focus.DOKill();

        if (!_animate || focusSlideSeconds <= 0f)
        {
            focus.anchoredPosition = new Vector2(t_x, focus.anchoredPosition.y);
            return;
        }

        focus.DOAnchorPosX(t_x, focusSlideSeconds)
             .SetEase(Ease.OutCubic)   // LobbyTabController.SLIDE_EASE와 같은 곡선이어야 한다
             .SetUpdate(true)   // 결과창 등에서 timeScale이 눌려도 탭 전환은 같은 속도로 돈다
             .SetLink(focus.gameObject);
    }

    /// <summary>알약 안쪽(하이라이트·아이콘·라벨)은 이름 규약으로 찾는다 — 씬 배선을 늘리지 않는다.
    /// "Light"는 원래부터 회전 하이라이트의 이름 규약이었고, 아이콘은 "알약 배경도 하이라이트도 아닌
    /// 첫 Image"로 잡는다. 인스펙터에 배선돼 있으면 그쪽이 이긴다.</summary>
    void EnsureFocusParts()
    {
        if (m_focusResolved || focus == null) return;
        m_focusResolved = true;

        if (focusHighlight == null) focusHighlight = focus.Find("Light") as RectTransform;
        if (focusLabel == null) focusLabel = focus.GetComponentInChildren<TMP_Text>(true);
        if (focusIcon != null) return;

        Image[] t_images = focus.GetComponentsInChildren<Image>(true);
        for (int i = 0; i < t_images.Length; i++)
        {
            RectTransform t_rect = t_images[i].rectTransform;
            if (t_rect == focus || t_rect == focusHighlight) continue;

            focusIcon = t_images[i];
            break;
        }
    }

    void StartFocusHighlightSpin()
    {
        EnsureFocusParts();
        if (focusHighlight == null) return;
        DOTween.Kill(focusHighlight);
        focusHighlight.localRotation = Quaternion.identity;
        float t_angle = focusSpinClockwise ? -360f : 360f;
        focusHighlight.DOLocalRotate(new Vector3(0f, 0f, t_angle), Mathf.Max(0.01f, focusSpinSeconds), RotateMode.FastBeyond360)
            .SetLoops(-1, LoopType.Restart).SetEase(Ease.Linear).SetUpdate(true)
            .SetTarget(focusHighlight).SetLink(focusHighlight.gameObject);
    }

    bool TryGetView(int _index, out TabButtonView _view)
    {
        EnsureViews();
        _view = _index >= 0 && _index < m_views.Length ? m_views[_index] : null;
        return _view != null;
    }

    void EnsureLabelCapacity(int _count)
    {
        while (m_labels.Count < _count) m_labels.Add(string.Empty);
    }
}

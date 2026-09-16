using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>온보딩 보상 무대와 같은 순서로 콘텐츠 해금을 소개한다.</summary>
public sealed class ContentUnlockIntroView : PooledOverlay<ContentUnlockIntroView>
{
    [SerializeField] TMP_Text _headingText;
    [SerializeField] TMP_Text _messageText;
    [SerializeField] TMP_Text _bodyText;
    [SerializeField] Image[] _icons;
    [SerializeField] TMP_Text[] _iconNames;
    [SerializeField] RectTransform _contentRoot;
    [SerializeField] CanvasGroup _contentGroup;
    [SerializeField] Button _confirmButton;
    [SerializeField] RectTransform iconRoot;
    [SerializeField] RectTransform glowRoot;
    [SerializeField] RectTransform _flightRoot;
    [Header("퇴장 · 아이콘 이동")]
    [SerializeField, Min(0f)] float _fadeDuration = 0.18f;
    [SerializeField] Ease _fadeEase = Ease.OutCubic;
    [SerializeField, Min(0f)] float _flightDuration = 0.4f;
    [SerializeField] Ease _flightEase = Ease.OutCubic;
    [SerializeField] OverlayDim dim = new OverlayDim();
    [SerializeField] EOutgameSound _introSound = EOutgameSound.PopupOpen;

    [Header("등장 · 제목")]
    [SerializeField] float titleDelay = 0.04f;
    [SerializeField] float titleDuration = 0.16f;
    [SerializeField] float titleDropDistance = 70f;
    [Header("등장 · 콘텐츠")]
    [SerializeField] float iconDelay = 0.2f;
    [SerializeField] float iconDuration = 0.1f;
    [SerializeField] float iconDropDistance = 180f;
    [SerializeField] float iconDropScale = 1.4f;
    [Header("등장 · 확인")]
    [SerializeField] float confirmDelay = 0.6f;
    [SerializeField] float confirmDuration = 0.16f;
    [SerializeField] float confirmRiseDistance = 90f;

    Action _onConfirmed;
    Action _onCancelled;
    Sequence _intro;
    Sequence _exit;
    bool _closing;
    bool _arrived;
    bool _captured;
    Vector2 _headingHome;
    Vector2 _stageHome;
    Vector2 _iconHome;
    Vector2 _confirmHome;
    Vector3 _iconScale;
    Vector3 _glowScale;
    Vector2[] _iconSizes;
    Vector2[] _namePositions;
    Vector2[] _nameSizes;
    CanvasGroup _headingGroup;
    CanvasGroup _messageGroup;
    CanvasGroup _bodyGroup;
    CanvasGroup _iconGroup;
    CanvasGroup _confirmGroup;
    CanvasGroup _glowGroup;
    IReadOnlyList<RectTransform> _destinations;
    Action<int, Action> _onArrived;
    readonly List<IconHome> _flyingIcons = new List<IconHome>();
    int _pendingArrivals;
    int _runVersion;

    protected override int SortingOrder => UiSortingOrder.Intro;

    /// <summary>등록된 온보딩 해금 소개 프리팹을 얻는다.</summary>
    public static bool TryGet(out ContentUnlockIntroView view)
        => TryGetOrCreate(out view);

    /// <summary>제목·콘텐츠·확인을 순서대로 드러낸다.</summary>
    public void Show(string title, string body, IReadOnlyList<Sprite> icons,
        Action onConfirmed, Action onCancelled, RectTransform destination = null,
        Action<Action> onArrived = null)
        => ShowTogether(title, body, icons, null, new[] { destination },
            onConfirmed, onCancelled, onArrived == null ? null : (index, done) => onArrived(done));

    /// <summary>아이콘별 목적지로 함께 이동하고 모든 도착 효과가 끝난 뒤 완료한다.</summary>
    public void ShowTogether(string title, string body, IReadOnlyList<Sprite> icons,
        IReadOnlyList<string> names, IReadOnlyList<RectTransform> destinations,
        Action onConfirmed, Action onCancelled, Action<int, Action> onArrived)
    {
        InitializeUI();
        Close();
        CaptureHome();
        _onConfirmed = onConfirmed;
        _onCancelled = onCancelled;
        _destinations = destinations;
        _onArrived = onArrived;
        _headingText.text = "신규 컨텐츠";
        _messageText.text = title;
        _bodyText.text = body;
        for (int i = 0; i < _icons.Length; i++)
        {
            if (_icons[i] == null) continue;
            bool visible = icons != null && i < icons.Count && icons[i] != null;
            _icons[i].gameObject.SetActive(visible);
            if (visible) _icons[i].sprite = icons[i];
            if (_iconNames != null && i < _iconNames.Length && _iconNames[i] != null)
            {
                bool showName = visible && names != null && i < names.Count;
                _iconNames[i].gameObject.SetActive(showName);
                if (showName) _iconNames[i].text = names[i];
            }
        }
        FitIconRow(icons != null ? icons.Count : 0);
        _confirmButton.onClick.RemoveListener(Confirm);
        _confirmButton.onClick.AddListener(Confirm);
        MarkOpen();
        dim.Show(this, UiSortingOrder.IntroDim, transition.OpenDuration);
        SetContentsVisible(true, transition);
        _confirmButton.interactable = false;
        BuildIntro();
        LayoutRebuilder.ForceRebuildLayoutImmediate(iconRoot);
    }

    /// <summary>스텝 취소는 완료 통지 없이 즉시 무대를 걷는다.</summary>
    public void Close() => Finish(false);

    public override void Hide() => Close();

    void CaptureHome()
    {
        if (_captured) return;
        _captured = true;
        _iconSizes = new Vector2[_icons.Length];
        for (int i = 0; i < _icons.Length; i++)
            if (_icons[i] != null) _iconSizes[i] = _icons[i].rectTransform.sizeDelta;
        int nameCount = _iconNames != null ? _iconNames.Length : 0;
        _namePositions = new Vector2[nameCount];
        _nameSizes = new Vector2[nameCount];
        for (int i = 0; i < nameCount; i++)
        {
            if (_iconNames[i] == null) continue;
            _namePositions[i] = _iconNames[i].rectTransform.anchoredPosition;
            _nameSizes[i] = _iconNames[i].rectTransform.sizeDelta;
        }
        _headingHome = _headingText.rectTransform.anchoredPosition;
        _stageHome = _contentRoot.anchoredPosition;
        _iconHome = iconRoot.anchoredPosition;
        _iconScale = iconRoot.localScale;
        _confirmHome = ((RectTransform)_confirmButton.transform).anchoredPosition;
        _headingGroup = GroupOf(_headingText.gameObject);
        _messageGroup = GroupOf(_messageText.gameObject);
        _bodyGroup = GroupOf(_bodyText.gameObject);
        _iconGroup = GroupOf(iconRoot.gameObject);
        _confirmGroup = GroupOf(_confirmButton.gameObject);
        if (glowRoot != null)
        {
            _glowScale = glowRoot.localScale;
            _glowGroup = GroupOf(glowRoot.gameObject);
        }
    }

    void BuildIntro()
    {
        ResetChoreography();
        _headingGroup.alpha = _messageGroup.alpha = _bodyGroup.alpha = 0f;
        _iconGroup.alpha = _confirmGroup.alpha = 0f;
        _headingText.rectTransform.anchoredPosition = _headingHome + Vector2.up * titleDropDistance;
        iconRoot.anchoredPosition = _iconHome + Vector2.up * iconDropDistance;
        iconRoot.localScale = _iconScale * iconDropScale;
        var confirmRect = (RectTransform)_confirmButton.transform;
        confirmRect.anchoredPosition = _confirmHome - Vector2.up * confirmRiseDistance;
        if (_glowGroup != null)
        {
            _glowGroup.alpha = 0f;
            glowRoot.localScale = _glowScale * 0.65f;
        }
        float impact = iconDelay + iconDuration;
        float confirmAt = impact + confirmDelay;
        _intro = DOTween.Sequence().SetTarget(this).SetLink(gameObject);
        _intro.Insert(titleDelay, _headingText.rectTransform.DOAnchorPos(_headingHome, titleDuration).SetEase(Ease.OutCubic));
        _intro.Insert(titleDelay, _headingGroup.DOFade(1f, titleDuration));
        _intro.Insert(iconDelay, _iconGroup.DOFade(1f, 0.04f));
        _intro.Insert(iconDelay, iconRoot.DOAnchorPos(_iconHome, iconDuration).SetEase(Ease.InCubic));
        _intro.Insert(iconDelay, iconRoot.DOScale(_iconScale, iconDuration).SetEase(Ease.InCubic));
        _intro.InsertCallback(impact, () => SoundManager.Instance?.PlayCue(_introSound));
        _intro.Insert(impact, _contentRoot.DOPunchAnchorPos(Vector2.down * 20f, 0.14f, 2, 0.8f));
        _intro.Insert(impact, _messageGroup.DOFade(1f, 0.16f));
        if (_iconNames != null)
            foreach (var label in _iconNames)
            {
                if (label == null || !label.gameObject.activeSelf) continue;
                var group = GroupOf(label.gameObject);
                group.alpha = 0f;
                _intro.Insert(impact, group.DOFade(1f, 0.16f));
            }
        _intro.Insert(impact + 0.08f, _bodyGroup.DOFade(1f, 0.16f));
        if (_glowGroup != null)
        {
            _intro.Insert(impact, _glowGroup.DOFade(1f, 0.08f));
            _intro.Insert(impact, glowRoot.DOScale(_glowScale * 1.06f, 0.1f).SetEase(Ease.OutQuad));
            _intro.Insert(impact + 0.1f, glowRoot.DOScale(_glowScale, 0.28f).SetEase(Ease.OutQuad));
        }
        _intro.Insert(confirmAt, _confirmGroup.DOFade(1f, confirmDuration));
        _intro.Insert(confirmAt, confirmRect.DOAnchorPos(_confirmHome, confirmDuration).SetEase(Ease.OutCubic));
        _intro.OnComplete(() => _confirmButton.interactable = true);
        _intro.Play();
    }

    void FitIconRow(int count)
    {
        var layout = iconRoot.GetComponent<HorizontalLayoutGroup>();
        float spacing = layout != null ? layout.spacing : 0f;
        float available = iconRoot.rect.width - (layout != null ? layout.padding.horizontal : 0);
        float width = count > 2 ? Mathf.Max(0f, (available - spacing * (count - 1)) / count) : 0f;
        for (int i = 0; i < _icons.Length; i++)
        {
            if (_icons[i] == null) continue;
            Vector2 size = _iconSizes[i];
            if (count > 2 && size.x > width) size *= width / size.x;
            _icons[i].rectTransform.sizeDelta = size;
            if (_iconNames == null || i >= _iconNames.Length || _iconNames[i] == null) continue;
            Vector2 position = _namePositions[i];
            Vector2 nameSize = _nameSizes[i];
            if (count > 2)
            {
                position.x = (i - (count - 1) * 0.5f) * (width + spacing);
                nameSize.x = width;
            }
            _iconNames[i].rectTransform.anchoredPosition = position;
            _iconNames[i].rectTransform.sizeDelta = nameSize;
        }
    }

    void Confirm()
    {
        if (!IsOpen || _closing || !_confirmButton.interactable) return;
        _closing = true;
        _confirmButton.interactable = false;
        KillChoreography();
        dim.Hide(_fadeDuration);
        LayoutRebuilder.ForceRebuildLayoutImmediate(iconRoot);
        _exit = DOTween.Sequence().SetTarget(this).SetLink(gameObject)
            .Append(_contentGroup.DOFade(0f, _fadeDuration).SetEase(_fadeEase));
        for (int i = 0; i < _icons.Length; i++)
        {
            if (_icons[i] == null || !_icons[i].gameObject.activeSelf) continue;
            var rect = _icons[i].rectTransform;
            _flyingIcons.Add(new IconHome(rect));
        }
        foreach (var home in _flyingIcons) home.Rect.SetParent(_flightRoot, true);
        for (int i = 0; i < _icons.Length; i++)
        {
            if (_icons[i] == null || !_icons[i].gameObject.activeSelf) continue;
            var rect = _icons[i].rectTransform;
            if (TryGetDestination(rect, DestinationAt(i), out Vector3 position, out Vector3 scale))
            {
                _exit.Insert(_fadeDuration, rect.DOLocalMove(position, _flightDuration).SetEase(_flightEase));
                _exit.Insert(_fadeDuration, rect.DOScale(scale, _flightDuration).SetEase(_flightEase));
            }
            else _exit.Insert(_fadeDuration, GroupOf(rect.gameObject).DOFade(0f, 0f));
        }
        _exit.OnComplete(Arrive);
        _exit.Play();
    }

    RectTransform DestinationAt(int index)
        => _destinations != null && index < _destinations.Count ? _destinations[index] : null;

    bool TryGetDestination(RectTransform icon, RectTransform destination, out Vector3 position, out Vector3 scale)
    {
        position = icon.localPosition;
        scale = icon.localScale;
        if (destination == null || !destination.gameObject.activeInHierarchy) return false;
        Camera sourceCamera = CameraOf(destination);
        Camera flightCamera = CameraOf(_flightRoot);
        Vector2 screen = RectTransformUtility.WorldToScreenPoint(sourceCamera,
            destination.TransformPoint(destination.rect.center));
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_flightRoot, screen,
            flightCamera, out Vector2 center)) return false;
        var corners = new Vector3[4];
        destination.GetWorldCorners(corners);
        var local = new Vector2[4];
        for (int i = 0; i < corners.Length; i++)
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_flightRoot,
                RectTransformUtility.WorldToScreenPoint(sourceCamera, corners[i]), flightCamera, out local[i]);
        float width = Vector2.Distance(local[0], local[3]);
        float height = Vector2.Distance(local[0], local[1]);
        float ratio = Mathf.Min(1f, Mathf.Min(width / Mathf.Max(0.001f, icon.rect.width * Mathf.Abs(scale.x)),
            height / Mathf.Max(0.001f, icon.rect.height * Mathf.Abs(scale.y))));
        scale *= ratio;
        position = (Vector3)center - icon.localRotation * Vector3.Scale(icon.rect.center, scale);
        return true;
    }

    static Camera CameraOf(RectTransform rect)
    {
        var canvas = rect.GetComponentInParent<Canvas>();
        return canvas == null || canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null : canvas.rootCanvas.worldCamera;
    }

    void Arrive()
    {
        if (!_closing || _arrived) return;
        _arrived = true;
        int version = _runVersion;
        var arrivals = new List<int>();
        for (int i = 0; i < _icons.Length; i++)
        {
            if (_icons[i] == null || !_icons[i].gameObject.activeSelf) continue;
            GroupOf(_icons[i].gameObject).alpha = 0f;
            var destination = DestinationAt(i);
            if (destination != null && destination.gameObject.activeInHierarchy && _onArrived != null)
                arrivals.Add(i);
        }
        _pendingArrivals = arrivals.Count;
        if (_pendingArrivals == 0) { Finish(true); return; }
        foreach (int index in arrivals)
        {
            if (version != _runVersion) break;
            bool completed = false;
            _onArrived(index, () =>
            {
                if (completed || version != _runVersion || !IsOpen || !_closing) return;
                completed = true;
                if (--_pendingArrivals == 0) Finish(true);
            });
        }
    }

    void Finish(bool confirmed)
    {
        _runVersion++;
        dim.Clear();
        bool wasOpen = ConsumeOpen();
        Action callback = confirmed ? _onConfirmed : _onCancelled;
        _onConfirmed = null;
        _onCancelled = null;
        _onArrived = null;
        _destinations = null;
        _pendingArrivals = 0;
        _closing = false;
        _arrived = false;
        KillChoreography();
        transition.HandleDisabled(ResolveTarget());
        ResetChoreography();
        SetContentsVisible(false);
        NotifyClosed(wasOpen);
        if (wasOpen) callback?.Invoke();
    }

    protected override void OnViewHidden()
    {
        transition.HandleDisabled(ResolveTarget());
        Finish(false);
    }

    void KillChoreography()
    {
        _intro?.Kill();
        _intro = null;
        _exit?.Kill();
        _exit = null;
    }

    void ResetChoreography()
    {
        if (!_captured) return;
        foreach (var home in _flyingIcons) home.Restore();
        _flyingIcons.Clear();
        foreach (var icon in _icons)
            if (icon != null) GroupOf(icon.gameObject).alpha = 1f;
        _headingText.rectTransform.anchoredPosition = _headingHome;
        _contentRoot.anchoredPosition = _stageHome;
        iconRoot.anchoredPosition = _iconHome;
        iconRoot.localScale = _iconScale;
        ((RectTransform)_confirmButton.transform).anchoredPosition = _confirmHome;
        _headingGroup.alpha = _messageGroup.alpha = _bodyGroup.alpha = 1f;
        _iconGroup.alpha = _confirmGroup.alpha = 1f;
        if (_contentGroup != null) _contentGroup.alpha = 1f;
        if (_glowGroup != null)
        {
            glowRoot.localScale = _glowScale;
            _glowGroup.alpha = 1f;
        }
    }

    GameObject ResolveTarget() => viewContents;

    readonly struct IconHome
    {
        public readonly RectTransform Rect;
        readonly Transform _parent;
        readonly int _sibling;
        readonly Vector3 _position;
        readonly Vector3 _scale;
        readonly Quaternion _rotation;
        readonly Vector2 _anchorMin;
        readonly Vector2 _anchorMax;
        readonly Vector2 _size;

        public IconHome(RectTransform rect)
        {
            Rect = rect;
            _parent = rect.parent;
            _sibling = rect.GetSiblingIndex();
            _position = rect.localPosition;
            _scale = rect.localScale;
            _rotation = rect.localRotation;
            _anchorMin = rect.anchorMin;
            _anchorMax = rect.anchorMax;
            _size = rect.sizeDelta;
        }

        public void Restore()
        {
            Rect.SetParent(_parent, false);
            Rect.SetSiblingIndex(_sibling);
            Rect.anchorMin = _anchorMin;
            Rect.anchorMax = _anchorMax;
            Rect.sizeDelta = _size;
            Rect.localPosition = _position;
            Rect.localScale = _scale;
            Rect.localRotation = _rotation;
        }
    }

    static CanvasGroup GroupOf(GameObject target)
    {
        var group = target.GetComponent<CanvasGroup>();
        return group != null ? group : target.AddComponent<CanvasGroup>();
    }
}

using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>온보딩 보상 무대와 같은 순서로 콘텐츠 해금을 소개한다.</summary>
public sealed class ContentUnlockIntroView : SingletonOverlay<ContentUnlockIntroView>
{
    [SerializeField] GameObject root;
    [SerializeField] TMP_Text _headingText;
    [SerializeField] TMP_Text _messageText;
    [SerializeField] TMP_Text _bodyText;
    [SerializeField] Image[] _icons;
    [SerializeField] RectTransform _contentRoot;
    [SerializeField] CanvasGroup _contentGroup;
    [SerializeField] Button _confirmButton;
    [SerializeField] RectTransform iconRoot;
    [SerializeField] RectTransform glowRoot;
    [SerializeField] PopupTransition transition = new PopupTransition();
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
    bool _captured;
    Vector2 _headingHome;
    Vector2 _stageHome;
    Vector2 _iconHome;
    Vector2 _confirmHome;
    Vector3 _iconScale;
    Vector3 _glowScale;
    CanvasGroup _headingGroup;
    CanvasGroup _messageGroup;
    CanvasGroup _bodyGroup;
    CanvasGroup _iconGroup;
    CanvasGroup _confirmGroup;
    CanvasGroup _glowGroup;

    protected override int SortingOrder => UiSortingOrder.Intro;

    /// <summary>등록된 온보딩 해금 소개 프리팹을 얻는다.</summary>
    public static bool TryGet(out ContentUnlockIntroView view)
        => TryGetOrCreate(RuntimeOverlayPrefabs.Get<ContentUnlockIntroView>, out view);

    /// <summary>제목·콘텐츠·확인을 순서대로 드러낸다.</summary>
    public void Show(string title, string body, IReadOnlyList<Sprite> icons,
        Action onConfirmed, Action onCancelled)
    {
        Close();
        CaptureHome();
        _onConfirmed = onConfirmed;
        _onCancelled = onCancelled;
        _headingText.text = "신규 컨텐츠";
        _messageText.text = title;
        _bodyText.text = body;
        for (int i = 0; i < _icons.Length; i++)
        {
            // 빈 칸이 남은 프리팹에서 예외가 새면 재생 플래그가 켜진 채 굳어 스텝이 영구 정지한다.
            if (_icons[i] == null) continue;
            bool visible = icons != null && i < icons.Count && icons[i] != null;
            _icons[i].gameObject.SetActive(visible);
            if (visible) _icons[i].sprite = icons[i];
        }
        _confirmButton.onClick.RemoveListener(Confirm);
        _confirmButton.onClick.AddListener(Confirm);
        MarkOpen();
        transition.SetVisible(ResolveTarget(), true);
        _confirmButton.interactable = false;
        BuildIntro();
    }

    /// <summary>스텝 취소는 완료 통지 없이 즉시 무대를 걷는다.</summary>
    public void Close() => Finish(false);

    void CaptureHome()
    {
        if (_captured) return;
        _captured = true;
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

    void Confirm()
    {
        if (!IsOpen || _closing || !_confirmButton.interactable) return;
        _closing = true;
        _confirmButton.interactable = false;
        KillChoreography();
        transition.SetVisible(ResolveTarget(), false);
        ResolveTarget().GetComponent<CanvasGroup>().blocksRaycasts = true;
        // 후속 스텝이 자체 PopupDim 아래에 먼저 서지 않게 퇴장을 마친 뒤 넘긴다.
        _exit = DOTween.Sequence().SetTarget(this)
            .AppendInterval(transition.CloseDuration)
            .OnComplete(() => Finish(true));
        _exit.Play();
    }

    void Finish(bool confirmed)
    {
        bool wasOpen = ConsumeOpen();
        Action callback = confirmed ? _onConfirmed : _onCancelled;
        _onConfirmed = null;
        _onCancelled = null;
        _closing = false;
        KillChoreography();
        transition.HandleDisabled(ResolveTarget());
        ResetChoreography();
        ResolveTarget().SetActive(false);
        NotifyClosed(wasOpen);
        if (wasOpen) callback?.Invoke();
    }

    void OnDisable()
    {
        transition.HandleDisabled(ResolveTarget());
        Finish(_closing);
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

    GameObject ResolveTarget() => root != null ? root : gameObject;

    static CanvasGroup GroupOf(GameObject target)
    {
        var group = target.GetComponent<CanvasGroup>();
        return group != null ? group : target.AddComponent<CanvasGroup>();
    }
}

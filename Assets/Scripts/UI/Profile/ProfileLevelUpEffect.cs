using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>프로필 원본을 움직이지 않고 주변 빛과 문구로 레벨업을 강조한다.</summary>
public sealed class ProfileLevelUpEffect : MonoBehaviour
{
    [SerializeField] CanvasGroup visibility;
    [SerializeField] Image flash;
    [SerializeField] Image ring;
    [SerializeField] TMP_Text levelText;
    [Header("표현")]
    [SerializeField] Color lightTint = new Color(1f, 0.87f, 0.45f, 1f);
    [SerializeField] string levelFormat = "LEVEL UP · Lv.{0}";
    [SerializeField, Min(0f)] float labelGap = 20f;
    [SerializeField, Min(0f)] float edgePadding = 20f;
    [SerializeField, Min(0.1f)] float flashSize = 1.5f;
    [SerializeField, Min(0.1f)] float ringStartScale = 1.02f;
    [SerializeField, Min(0.1f)] float ringEndScale = 1.9f;
    [Header("박자")]
    [SerializeField, Min(0.01f)] float appearDuration = 0.15f;
    [SerializeField, Min(0.01f)] float expandDuration = 0.45f;
    [SerializeField, Min(0f)] float holdDuration = 0.7f;
    [SerializeField, Min(0.01f)] float fadeDuration = 0.25f;

    Sequence _sequence;
    RectTransform _target;

    public bool IsPlaying => _sequence != null && _sequence.IsActive();
    public bool IsWired => visibility != null && flash != null && ring != null && levelText != null;

    /// <summary>대상 프로필 주변에서 확정 레벨을 표시한다.</summary>
    public bool Play(RectTransform target, int level)
    {
        if (!IsWired || target == null || !target.gameObject.activeInHierarchy || level <= 0) return false;
        Stop();
        _target = target;
        gameObject.SetActive(true);
        visibility.alpha = 0f;
        visibility.interactable = false;
        visibility.blocksRaycasts = false;
        flash.raycastTarget = ring.raycastTarget = levelText.raycastTarget = false;
        flash.color = ring.color = lightTint;
        levelText.text = string.Format(levelFormat, level);
        flash.rectTransform.localScale = Vector3.one;
        ring.rectTransform.localScale = Vector3.one * ringStartScale;
        Place();

        _sequence = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
        _sequence.Append(visibility.DOFade(1f, appearDuration));
        _sequence.Append(ring.rectTransform.DOScale(ringEndScale, expandDuration).SetEase(Ease.OutCubic));
        _sequence.Join(ring.DOFade(0f, expandDuration));
        _sequence.Join(flash.DOFade(0f, expandDuration));
        _sequence.AppendInterval(holdDuration);
        _sequence.Append(visibility.DOFade(0f, fadeDuration));
        _sequence.OnComplete(Stop);
        return true;
    }

    /// <summary>즉시 종료하고 표시만 정리한다.</summary>
    public void Stop()
    {
        var sequence = _sequence;
        _sequence = null;
        sequence?.Kill();
        _target = null;
        if (visibility != null) visibility.alpha = 0f;
        if (gameObject.activeSelf) gameObject.SetActive(false);
    }

    void LateUpdate()
    {
        if (!IsPlaying) return;
        if (_target == null || !_target.gameObject.activeInHierarchy) { Stop(); return; }
        Place();
    }

    void OnDisable() => Stop();

    void Place()
    {
        var root = (RectTransform)transform;
        root.position = _target.TransformPoint(_target.rect.center);
        Vector3 size = root.InverseTransformVector(_target.TransformVector(_target.rect.size));
        var diameter = Mathf.Max(Mathf.Abs(size.x), Mathf.Abs(size.y));
        flash.rectTransform.sizeDelta = Vector2.one * diameter * flashSize;
        ring.rectTransform.sizeDelta = Vector2.one * diameter;
        PopupPlacer.PlaceBelowAnchor(levelText.rectTransform, _target, labelGap, edgePadding);
    }
}

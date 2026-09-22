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
    [SerializeField] string levelFormat = "LEVEL UP";
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

    /// <summary>프리팹에 설정된 위치와 크기로 레벨업 연출을 재생한다.</summary>
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
        levelText.text = levelFormat;
        flash.rectTransform.localScale = Vector3.one;
        ring.rectTransform.localScale = Vector3.one * ringStartScale;

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
    }

    void OnDisable() => Stop();

}

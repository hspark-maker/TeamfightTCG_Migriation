using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

public class UIAnimator : MonoBehaviour
{
    [SerializeField] float fadeDuration = 0.3f;
    [SerializeField] AnimationCurve fadeCurve = AnimationCurve.Linear(0, 0, 1, 1);
    [SerializeField] float moveDuration = 0.3f;
    [SerializeField] AnimationCurve moveCurve = AnimationCurve.Linear(0, 0, 1, 1);

    public void Fade(CanvasGroup _target, float _targetAlpha, Action _onComplete = null)
    {
        _target.DOKill();
        _target.DOFade(_targetAlpha, fadeDuration)
            .SetEase(fadeCurve)
            .OnComplete(() => _onComplete?.Invoke());
    }

    public void FadeGraphic(Graphic _target, float _targetAlpha, Action _onComplete = null)
    {
        _target.DOKill();
        _target.DOFade(_targetAlpha, fadeDuration)
            .SetEase(fadeCurve)
            .OnComplete(() => _onComplete?.Invoke());
    }

    public void Move(RectTransform _target, Vector2 _to, Action _onComplete = null)
    {
        _target.DOKill();
        _target.DOAnchorPos(_to, moveDuration)
            .SetEase(moveCurve)
            .OnComplete(() => _onComplete?.Invoke());
    }
}

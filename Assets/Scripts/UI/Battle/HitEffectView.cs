using DG.Tweening;
using TMPro;
using UnityEngine;

/// <summary>실제 피격량을 카드 위에 띄우는 표시 전용 연출.</summary>
public class HitEffectView : MonoBehaviour
{
    [SerializeField] TMP_Text damageText;
    [SerializeField] float duration = 1f;
    [SerializeField] float floatDistance = 0.6f;

    Vector3 textHome;
    Sequence playSequence;
    bool cached;

    public void Play(int _damage)
    {
        Stop();
        if (this.damageText == null || _damage <= 0) return;

        if (!this.cached)
        {
            this.textHome = this.damageText.transform.localPosition;
            this.cached = true;
        }

        gameObject.SetActive(true);
        this.damageText.gameObject.SetActive(true);
        this.damageText.text = $"-{_damage}";
        this.damageText.transform.localPosition = this.textHome;

        Color t_color = this.damageText.color;
        t_color.a = 1f;
        this.damageText.color = t_color;

        this.playSequence = DOTween.Sequence().SetLink(gameObject);
        this.playSequence.Join(this.damageText.transform
            .DOLocalMoveY(this.textHome.y + this.floatDistance, this.duration)
            .SetEase(Ease.OutCubic));
        this.playSequence.Join(this.damageText.DOFade(0f, this.duration).SetEase(Ease.InQuad));
        this.playSequence.OnComplete(() => gameObject.SetActive(false));
    }

    public void Stop()
    {
        this.playSequence?.Kill();
        this.playSequence = null;
        if (this.damageText != null)
        {
            this.damageText.DOKill();
            this.damageText.transform.DOKill();
            this.damageText.gameObject.SetActive(false);
        }
        gameObject.SetActive(false);
    }
}

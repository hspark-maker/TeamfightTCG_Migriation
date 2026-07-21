using DG.Tweening;
using UnityEngine;

public class SwipeGuide : MonoBehaviour
{
    [SerializeField] SpriteRenderer leftSr;
    [SerializeField] SpriteRenderer centerSr;
    [SerializeField] SpriteRenderer rightSr;
    [SerializeField] float dimAlpha = 0.3f;
    [SerializeField] float highlightAlpha = 1.0f;
    [SerializeField] float fadeInDuration = 0.25f;
    [SerializeField] float switchDuration = 0.12f;
    [SerializeField] float dirThreshold = 0.35f;

    SpriteRenderer[] allSprites;
    SpriteRenderer currentHighlight;
    Sequence hideSeq;
    bool isVisible;

    void Awake()
    {
        this.allSprites = new[] { this.leftSr, this.centerSr, this.rightSr };
        gameObject.SetActive(false);
    }

    public void SetVisible(bool _visible)
    {
        if (_visible == this.isVisible) return;
        this.isVisible = _visible;
        if (_visible) Show();
        else Hide();
    }

    public void UpdateDirection(float _aimX)
    {
        if (!this.isVisible) return;

        SpriteRenderer t_next;
        if (_aimX < -this.dirThreshold) t_next = this.rightSr;
        else if (_aimX > this.dirThreshold) t_next = this.leftSr;
        else t_next = this.centerSr;

        if (t_next == this.currentHighlight) return;
        this.currentHighlight = t_next;

        foreach (SpriteRenderer t_sr in this.allSprites)
        {
            t_sr.DOKill();
            t_sr.DOFade(t_sr == t_next ? this.highlightAlpha : this.dimAlpha, this.switchDuration).SetLink(gameObject);
        }
    }

    void Show()
    {
        this.hideSeq?.Kill();
        gameObject.SetActive(true);
        this.currentHighlight = this.centerSr;

        foreach (SpriteRenderer t_sr in this.allSprites)
        {
            t_sr.DOKill();
            Color t_c = t_sr.color;
            t_c.a = 0f;
            t_sr.color = t_c;
            float t_target = t_sr == this.centerSr ? this.highlightAlpha : this.dimAlpha;
            t_sr.DOFade(t_target, this.fadeInDuration).SetLink(gameObject);
        }
    }

    void Hide()
    {
        this.hideSeq?.Kill();
        this.hideSeq = DOTween.Sequence();
        this.hideSeq.SetLink(gameObject);
        foreach (SpriteRenderer t_sr in this.allSprites)
            this.hideSeq.Join(t_sr.DOFade(0f, this.fadeInDuration));
        this.hideSeq.AppendCallback(() => gameObject.SetActive(false));
    }

    void OnDestroy()
    {
        this.hideSeq?.Kill();
        if (this.allSprites != null)
            foreach (SpriteRenderer t_sr in this.allSprites)
                if (t_sr != null) t_sr.DOKill();
    }
}

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
        EnsureInit();
        // 시작 시 숨김은 알파 0으로 처리. (예전 gameObject.SetActive(false)는 GO가 비활성으로 시작한 경우
        //  첫 Show()의 SetActive(true)가 뒤늦게 Awake를 돌려 다시 꺼버려 → 첫 드래그-백에서 가이드가 안 보였다.)
        foreach (SpriteRenderer t_sr in this.allSprites)
            if (t_sr != null) { Color t_c = t_sr.color; t_c.a = 0f; t_sr.color = t_c; }
    }

    void EnsureInit()
    {
        if (this.allSprites == null)
            this.allSprites = new[] { this.leftSr, this.centerSr, this.rightSr };
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
        EnsureInit();
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

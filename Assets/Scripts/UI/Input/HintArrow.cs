using DG.Tweening;
using UnityEngine;

public class HintArrow : MonoBehaviour
{
    [SerializeField] SpriteRenderer[] arrowRenderers;
    [SerializeField] float[] peakAlphas  = { 0.5f, 0.25f, 0.125f };
    [SerializeField] float moveDistance  = 0.3f;
    [SerializeField] float duration      = 0.6f;
    [SerializeField] float stagger       = 0.2f;

    Sequence[] loopSeqs;
    Vector3[]  baseLocalPos;
    bool       isVisible;

    void Awake()
    {
        this.baseLocalPos = new Vector3[this.arrowRenderers.Length];
        for (int i = 0; i < this.arrowRenderers.Length; i++)
            this.baseLocalPos[i] = this.arrowRenderers[i].transform.localPosition;
    }

    public void SetVisible(bool _visible)
    {
        if (_visible == this.isVisible) return;
        this.isVisible = _visible;
        if (_visible) Play();
        else Stop();
    }

    void Play()
    {
        gameObject.SetActive(true);
        KillLoops();
        this.loopSeqs = new Sequence[this.arrowRenderers.Length];

        float t_totalPeriod = this.duration + this.stagger * (this.arrowRenderers.Length - 1);

        for (int t_i = 0; t_i < this.arrowRenderers.Length; t_i++)
        {
            SpriteRenderer t_sr   = this.arrowRenderers[t_i];
            Transform      t_tr   = t_sr.transform;
            Vector3        t_base = this.baseLocalPos[t_i];
            float          t_delay    = t_i * this.stagger;
            float          t_padAfter = t_totalPeriod - t_delay - this.duration;

            t_tr.localPosition = t_base;
            Color t_c = t_sr.color;
            t_c.a = 0f;
            t_sr.color = t_c;

            Sequence t_seq = DOTween.Sequence();
            if (t_delay > 0f)    t_seq.AppendInterval(t_delay);
            float t_peak = (this.peakAlphas != null && t_i < this.peakAlphas.Length) ? this.peakAlphas[t_i] : 1f;
            t_seq.Append(t_sr.DOFade(t_peak, this.duration * 0.35f));
            t_seq.Join(t_tr.DOLocalMoveY(t_base.y - this.moveDistance, this.duration).SetEase(Ease.InQuad));
            t_seq.Append(t_sr.DOFade(0f, this.duration * 0.65f));
            t_seq.AppendCallback(() => t_tr.localPosition = t_base);
            if (t_padAfter > 0f) t_seq.AppendInterval(t_padAfter);
            t_seq.SetLoops(-1, LoopType.Restart);
            t_seq.SetLink(gameObject);

            this.loopSeqs[t_i] = t_seq;
        }
    }

    void Stop()
    {
        KillLoops();
        gameObject.SetActive(false);
    }

    void KillLoops()
    {
        if (this.loopSeqs == null) return;
        foreach (Sequence t_seq in this.loopSeqs)
            t_seq?.Kill();
    }

    void OnDestroy() => KillLoops();
}

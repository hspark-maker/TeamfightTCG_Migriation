using DG.Tweening;
using UnityEngine;

/// <summary>터치 이펙트 한 발. 프리팹에 저작해 두고 <see cref="TouchEffectOverlay"/>가 돌려 쓴다 —
/// 런타임에 만들지 않으므로 그림·색·크기는 전부 인스펙터 저작값이다.</summary>
public sealed class TouchEffectItem : MonoBehaviour
{
    [SerializeField] RectTransform  body;
    [SerializeField] CanvasGroup    group;
    [SerializeField] ParticleSystem burst;      // 없어도 된다(스프라이트만 쓰는 저작)

    [SerializeField] float duration  = 0.35f;
    [SerializeField] float fromScale = 0.4f;
    [SerializeField] float toScale   = 1.4f;

    Sequence sequence;

    void Reset()
    {
        this.body  = transform as RectTransform;
        this.group = GetComponent<CanvasGroup>();
    }

    /// <summary>_anchoredPos(오버레이 루트 기준)에서 한 번 재생한다.
    /// 연타로 같은 발이 재사용돼도 배율·알파가 누적되지 않게 이전 트윈을 먼저 죽인다.</summary>
    public void Play(Vector2 _anchoredPos)
    {
        if (this.body == null) this.body = transform as RectTransform;
        if (this.body == null) return;

        this.sequence?.Kill();

        this.body.anchoredPosition = _anchoredPos;
        this.body.localScale       = Vector3.one * this.fromScale;
        gameObject.SetActive(true);

        if (this.group != null) this.group.alpha = 1f;

        if (this.burst != null)
        {
            this.burst.Clear(true);
            this.burst.Play(true);
        }

        this.sequence = DOTween.Sequence()
            .Append(this.body.DOScale(this.toScale, this.duration).SetEase(Ease.OutQuad))
            .SetLink(gameObject)
            .OnComplete(Hide);

        if (this.group != null)
            this.sequence.Join(this.group.DOFade(0f, this.duration).SetEase(Ease.InQuad));
    }

    void Hide()
    {
        this.sequence = null;
        gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        this.sequence?.Kill();
        this.sequence = null;
    }
}

using Coffee.UIExtensions;
using DG.Tweening;
using UnityEngine;

// 카드가 슬리브에 안착한 순간, 저작된 UI 파티클을 슬롯 중심에서 재생한다.
// layer는 삽입 패널 밖의 Layer_InsertFanfare에 둔다. 패널이 숨겨져도 연출은 남아야 한다.
[System.Serializable]
public class AlbumInsertFanfareFx
{
    [SerializeField] RectTransform layer;
    [SerializeField] UIParticle effectPrefab;
    [Min(0.01f)] [SerializeField] float effectScale = 10f;
    [Range(0.1f, 1f)] [SerializeField] float quickScale = 0.7f;
    [Min(0.01f)] [SerializeField] float playbackSpeed = 8f;
    [Min(0.01f)] [SerializeField] float duration = 0.65f;

    UIParticle m_effect;
    Tween m_completion;

    public void Play(RectTransform _slotRect, bool _quick)
    {
        this.Reset();
        if (_slotRect == null || this.layer == null || this.effectPrefab == null) return;

        if (this.m_effect == null)
        {
            this.m_effect = Object.Instantiate(this.effectPrefab, this.layer, false);
            foreach (ParticleSystem t_particle in this.m_effect.particles)
            {
                var t_main = t_particle.main;
                t_main.useUnscaledTime = true;
            }
        }

        RectTransform t_rect = this.m_effect.rectTransform;
        t_rect.localScale = Vector3.one;
        t_rect.anchoredPosition = UiGainBurst.ToLayerLocal(this.layer, _slotRect);
        this.m_effect.scale = this.effectScale * (_quick ? this.quickScale : 1f);
        this.m_effect.timeScaleMultiplier = this.playbackSpeed;
        this.m_effect.gameObject.SetActive(true);
        this.m_effect.Play();
        this.m_completion = DOVirtual.DelayedCall(this.duration, this.Reset)
            .SetUpdate(true).SetLink(this.layer.gameObject);
    }

    public void Reset()
    {
        this.m_completion?.Kill();
        this.m_completion = null;
        if (this.m_effect == null) return;

        this.m_effect.Stop();
        this.m_effect.Clear();
        this.m_effect.gameObject.SetActive(false);
    }
}

using DG.Tweening;
using UnityEngine;

/// <summary>터치 이펙트 한 발. 프리팹에 저작해 두고 <see cref="TouchEffectOverlay"/>가 돌려 쓴다 —
/// 런타임에 만들지 않으므로 파티클 저작값(색·수명·크기)은 전부 인스펙터에 있다.
///
/// 파티클이 월드 시뮬레이션이라(FX_Common_Touch의 moveWithTransform=0) 이미 튄 알갱이는 발이 옮겨 가도 제자리에 남는다 —
/// 연타를 겹쳐 보이게 하는 것이 발을 여럿 두는 이유다.</summary>
public sealed class TouchEffectItem : MonoBehaviour
{
    [SerializeField] RectTransform    body;
    [SerializeField] ParticleSystem[] particles;

    /// <summary>이 시간이 지나면 발을 꺼서 되돌린다. 알갱이 수명보다 짧으면 꺼지는 순간 잘려 보인다 —
    /// 파티클을 고쳤으면 이 값도 같이 본다.</summary>
    [SerializeField] float lifetime = 1.2f;   // FX_Common_Touch 기준: 알갱이 수명 0.4초 + 여유

    Tween hideTimer;

    void Reset() => this.body = transform as RectTransform;

    /// <summary>_anchoredPos(오버레이 Stage 기준)에서 한 번 재생한다.</summary>
    public void Play(Vector2 _anchoredPos)
    {
        if (this.body == null) this.body = transform as RectTransform;
        if (this.body == null) return;

        this.hideTimer?.Kill();

        this.body.anchoredPosition = _anchoredPos;
        gameObject.SetActive(true);

        // 위치를 옮긴 프레임에 바로 터뜨린다 — Clear를 먼저 하는 이유는 발을 뺏겼을 때
        // 이전 탭의 알갱이가 새 자리로 끌려오지 않게 하기 위해서다(월드 공간이라 남아 있으면 그대로 튄다).
        for (int i = 0; i < (this.particles?.Length ?? 0); i++)
        {
            ParticleSystem t_ps = this.particles[i];
            if (t_ps == null) continue;

            t_ps.Clear(true);
            t_ps.Play(true);
        }

        this.hideTimer = DOVirtual.DelayedCall(this.lifetime, Hide).SetLink(gameObject);
    }

    void Hide()
    {
        this.hideTimer = null;
        gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        this.hideTimer?.Kill();
        this.hideTimer = null;
    }
}

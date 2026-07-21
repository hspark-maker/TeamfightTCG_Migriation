using System.Collections;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;

[RequireComponent(typeof(ParticleSystem))]
public class PooledParticle : MonoBehaviour
{
    public string id;
    ParticleSystem ps;
    [SerializeField] float releaseTime;
    CancellationTokenSource cts;

    void Awake()
    {
        this.ps = GetComponent<ParticleSystem>();
    }

    void OnEnable()
    {
        this.cts = new CancellationTokenSource();
        WaitAndRelease();
    }

    void OnDisable()
    {
        this.cts?.Cancel();
        this.cts?.Dispose();
        this.cts = null;
    }

    async void WaitAndRelease()
    {
        bool t_cancelled = await UniTask.WaitForSeconds(this.releaseTime, cancellationToken: this.cts.Token).SuppressCancellationThrow();
        if (!t_cancelled)
            ParticlePooler.Release(this.id, gameObject);
    }
}

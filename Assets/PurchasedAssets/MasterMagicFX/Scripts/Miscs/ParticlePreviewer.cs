using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MasterFX
{
public class ParticlePreviewer : MonoBehaviour
{
    public float Interval  = 1f;
    float Timer = 0f;
    ParticleSystem ParticleSystem;  
    void Update()
    {
        Timer += Time.deltaTime;
        if (Timer >= Interval)
        {
            Timer = 0f;
            // 播放粒子效果
            PlayParticleEffect();
        }
    }
    void Start()
    {
        // 获取粒子系统组件
        ParticleSystem = GetComponent<ParticleSystem>();
        if (ParticleSystem == null)
        {
            Debug.LogError("ParticleSystem is not assigned!");
            return;
        }
    }
    public void PlayParticleEffect()
    {
        ParticleSystem.Play();
    }
}
}

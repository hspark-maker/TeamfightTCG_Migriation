using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MasterFX
{
    [ExecuteInEditMode]
    public class SubEmitterByTime : MonoBehaviour
    {
        public ParticleSystem Particle;
        public float TriggerInterval = 0.2f;
        float LastTime = 0;
        public float Timer;
        private void OnWillRenderObject()
        {
            Timer += Time.time - LastTime;
            if (Particle == null)
            {
                Particle = GetComponent<ParticleSystem>();
            }
            if (Particle != null && Timer >= TriggerInterval)
            {
                //Debug.Log("TriggerSubEmitter");
                if (Particle.isPlaying)
                {
                    Timer = 0;
                    for (int i = 0; i < Particle.subEmitters.subEmittersCount; i++)
                    {
                    Particle.TriggerSubEmitter(i);
                    }
                }

            }
            LastTime = Time.time;
        }
    }
}

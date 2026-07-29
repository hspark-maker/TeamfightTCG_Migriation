using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MasterFX
{
    [System.Serializable]
    public class Effect
    {
        public ParticleSystem effect;

        public Vector3 Position;

        public Vector3 EulerAngle;

    }
    public class MEffectExample : MonoBehaviour
    {
        public List<Effect> Effects;
        public int currentEffectIndex = 0;
        public Transform EffectPosition;


        GameObject curEffect;
        public void PlayEffect()
        {
            //clear cur played effect;
            if (curEffect != null)
            {
                Destroy(curEffect);
            }

            //Instantiate the effect and apply the mapping texture


            GameObject effect = Instantiate(Effects[currentEffectIndex].effect.gameObject, EffectPosition.position, Quaternion.identity);

            Debug.Log(effect.name);
            effect.transform.position += Effects[currentEffectIndex].Position;
            effect.transform.eulerAngles = Effects[currentEffectIndex].EulerAngle;
            curEffect = effect;
        }
        private void OnDisable()
        {
            if (curEffect != null)
            {
                Destroy(curEffect);
            }
        }
    }

}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MasterFX
{
    public class MMagicExample : MonoBehaviour
    {
        public MEffectExample EffectExample;
        public BulletShooter BulletShooter;
        public LaserShooter LaserShooter;

        int currentEffectIndex = 0;

        int TotalIndex => EffectExample.Effects.Count + BulletShooter.bulletDatas.Count + LaserShooter.laserDatas.Count;
        int CurrentIndex
        {
            get
            {
                if (currentEffectIndex < EffectExample.Effects.Count)
                {
                    return currentEffectIndex;
                }
                else if (currentEffectIndex < EffectExample.Effects.Count + BulletShooter.bulletDatas.Count)
                {
                    return currentEffectIndex - EffectExample.Effects.Count;
                }
                else
                {
                    return currentEffectIndex - EffectExample.Effects.Count - BulletShooter.bulletDatas.Count;
                }
            }
        }
        enum CurrentType
        {
            Effect,
            Bullet,
            Laser
        }
        CurrentType currentType
        {
            get
            {
                if (currentEffectIndex < EffectExample.Effects.Count)
                {
                    return CurrentType.Effect;
                }
                else if (currentEffectIndex < EffectExample.Effects.Count + BulletShooter.bulletDatas.Count)
                {
                    return CurrentType.Bullet;
                }
                else
                {
                    return CurrentType.Laser;
                }
            }
        }

        void Start()
        {
            SetActiveToAllByIndex();
        }
        void Update()
        {
            //press A or D to shift index, and jump index from the MEffectexample, BulletShooter and LaserShooter;
            if (Input.GetKeyDown(KeyCode.A))
            {
                currentEffectIndex--;
                if (currentEffectIndex < 0)
                {
                    currentEffectIndex = TotalIndex - 1;
                }
                SetActiveToAllByIndex();
            }
            if (Input.GetKeyDown(KeyCode.D))
            {
                currentEffectIndex++;
                if (currentEffectIndex >= TotalIndex)
                {
                    currentEffectIndex = 0;
                }
                SetActiveToAllByIndex();
            }
            //press space or left mouse button to play the effect;
            if (Input.GetMouseButtonDown(0))
            {
                PlayEffect();
            }
            if (Input.GetMouseButtonUp(0))
            {
                if (currentType == CurrentType.Laser)
                {
                    LaserShooter.DisableLaser();
                }
            }

        }
        public void SetActiveToAllByIndex()
        {
            EffectExample.gameObject.SetActive(currentType == CurrentType.Effect);
            BulletShooter.gameObject.SetActive(currentType == CurrentType.Bullet);
            LaserShooter.gameObject.SetActive(currentType == CurrentType.Laser);
        }


        public void PlayEffect()
        {

            //play the effect;
            if (currentType == CurrentType.Effect)
            {
                EffectExample.currentEffectIndex = CurrentIndex;
                EffectExample.PlayEffect();
            }
            else if (currentType == CurrentType.Bullet)
            {
                BulletShooter.currentBulletIndex = CurrentIndex;
                BulletShooter.ShootBullet();
            }
            else
            {
                LaserShooter.curLaserIndex = CurrentIndex;
                LaserShooter.EnableLaser();
            }
        }


    }
}
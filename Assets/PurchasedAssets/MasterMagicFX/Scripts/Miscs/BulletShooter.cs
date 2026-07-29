using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace MasterFX
{
    [System.Serializable]
    public class BulletData
    {
        public ParticleSystem Muzzle;
        public ParticleSystem Bullet;
        public GameObject DefaultTrails;
        public ParticleSystem HitEffect;
        public Texture2D MappingTexture;
        public float BulletSpeed = 100f;
    }

    public class BulletShooter : MonoBehaviour
    {
        public ParticleSystem Muzzle => bulletDatas[currentBulletIndex].Muzzle;
        public ParticleSystem Bullet => bulletDatas[currentBulletIndex].Bullet;

        public GameObject DefaultTrails => bulletDatas[currentBulletIndex].DefaultTrails;
        public ParticleSystem HitEffect => bulletDatas[currentBulletIndex].HitEffect;
        public float BulletSpeed = 100f;

        public Transform BulletStartPoint;

        // public Gradient MappingGradient = new Gradient();
        
        public Texture2D MappingTexture => bulletDatas[currentBulletIndex].MappingTexture;

        public List<BulletData> bulletDatas = new List<BulletData>();

        public int currentBulletIndex = 0;


        public List<Material> materials = new List<Material>();


        public bool ModifyMaterials = false;


        void OnDestroy()
        {
            //destroy all the materials;
            foreach (Material material in materials)
            {
                Destroy(material);
                materials = new List<Material>();
            }
        }


        public void ShootBullet()
        {
            StartCoroutine(ShootBulletCoroutine());
        }
        public IEnumerator ShootBulletCoroutine()
        {
            //get targetPosition by raycast to the screen mouse position;
            var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;
            Vector3 targetPosition = ray.GetPoint(1000);
            if (Physics.Raycast(ray, out hit, 1000))
            {
                targetPosition = hit.point;

            }
            //rotate the bullet start point to the target position
            BulletStartPoint.LookAt(targetPosition);
            //offset the end a bit;
            targetPosition -= 0.5f * (targetPosition - BulletStartPoint.position).normalized;



            var muzzle = Instantiate(Muzzle, BulletStartPoint.position, BulletStartPoint.rotation);
            var bullet = Instantiate(Bullet, BulletStartPoint.position, BulletStartPoint.rotation);

            GameObject Trail = null;
            if (DefaultTrails != null)
            {
                Trail = Instantiate(DefaultTrails, BulletStartPoint.position, BulletStartPoint.rotation);
                SetRampTexture(Trail.gameObject);
            }
            SetRampTexture(bullet.gameObject);
            SetRampTexture(muzzle.gameObject);
            if (ModifyMaterials)
            {
                SetRampTexture(DefaultTrails.gameObject);
            }
            bullet.transform.parent = transform;
            var time = Vector3.Distance(BulletStartPoint.position, targetPosition) / BulletSpeed;
            var timer = 0f;
            while (timer < time)
            {
                timer += Time.deltaTime;
                bullet.transform.position += bullet.transform.forward * BulletSpeed * Time.deltaTime;
                if (Trail != null)
                {
                    Trail.transform.position = bullet.transform.position;
                }
                yield return null;
            }
            //destroy bullet
            Destroy(bullet.gameObject);
            //instantiate hit effect
            var hitEffect = Instantiate(HitEffect, bullet.transform.position, bullet.transform.rotation);
            SetRampTexture(hitEffect.gameObject);
        }
        public void SetRampTexture(GameObject target)
        {
            //set texture from the gradient;
            if (MappingTexture)
            {
                ParticleSystemRenderer renderer = target.GetComponent<ParticleSystemRenderer>();
                if (renderer)
                {
                    MUtils.MApplyLutTexturesToParticles(target.GetComponent<ParticleSystem>(), MappingTexture);
                }

                //get all the particles systems in the children
                ParticleSystem[] systems = target.GetComponentsInChildren<ParticleSystem>();
                foreach (ParticleSystem system in systems)
                {
                    MUtils.MApplyLutTexturesToParticles(system, MappingTexture);

                }
                //get all trail renderer in the children
                TrailRenderer[] trails = target.GetComponentsInChildren<TrailRenderer>();
                foreach (TrailRenderer trail in trails)
                {
                    //set the gradient of the trails to the mapping gradient
                    //read pixels from mapping texture and set it to the trail color gradient;
                    ApplyGradientToTrails(trails);
                }
            }
        }

        void ApplyGradientToTrails(TrailRenderer[] trails)
        {
            if (MappingTexture == null)
            {
                Debug.LogError("Gradient texture is not assigned!");
                return;
            }

            Gradient trailGradient = new Gradient();

            var step = 64;
            int colorCount = (MappingTexture.width / step) + 1;

            GradientColorKey[] colorKeys = new GradientColorKey[colorCount];
            GradientAlphaKey[] alphaKeys = new GradientAlphaKey[colorCount];

            for (int i = 0; i < colorCount; i += 1)
            {
                Color color = MappingTexture.GetPixel(i * step, 0);
                var alpha = MappingTexture.GetPixel(i * step, 0).a;
                float time = i / (float)(colorCount - 1);
                colorKeys[i] = new GradientColorKey(color, time);
                alphaKeys[i] = new GradientAlphaKey(alpha, time);
            }

            trailGradient.colorKeys = colorKeys;
            trailGradient.alphaKeys = alphaKeys;

            foreach (TrailRenderer trail in trails)
            {
                trail.colorGradient = trailGradient;
            }
        }
    }
}

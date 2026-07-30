using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MasterFX
{
    public class BulletPreviewerShooter : MonoBehaviour
    {
        public GameObject Muzzle; // 枪口粒子效果
        public GameObject BulletHit; // 子弹击中效果
        public GameObject BulletPrefab; // 子弹预制体
        public GameObject Trail; // 子弹轨迹粒子
        public float Speed = 50f; // 子弹速度
        public float DistanceLimit = 100f; // 子弹最大飞行距离
        public float ShootInterval = 0.2f; // 射击间隔
        public float TrailLifetime = 2f; // 轨迹粒子停留时间（秒）

        private float shootTimer = 0f; // 射击计时器
        private List<GameObject> activeBullets = new List<GameObject>(); // 跟踪活跃子弹
        private Dictionary<GameObject, GameObject> bulletTrailMap = new Dictionary<GameObject, GameObject>(); // 存储子弹与轨迹的对应关系

        private void OnDrawGizmos()
        {
            // 绘制射击范围
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position + transform.forward * DistanceLimit, 0.5f);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * DistanceLimit);
        }
 void OnDestroy()
 {
    //clear all instantiated objects;
    foreach (GameObject bullet in activeBullets)
    {
        if (bullet != null)
        {
            Destroy(bullet);
        }
    }
 }
        void Update()
        {
            // 计时器累加
            shootTimer += Time.deltaTime;

            // 按下鼠标左键或空格键射击
            if (shootTimer >= ShootInterval)
            {
                Shoot();
                shootTimer = 0f; // 重置计时器
            }

            // 更新子弹位置
            UpdateBullets();
        }

        void Shoot()
        {
            // 播放枪口效果
            if (Muzzle != null)
            {
                GameObject muzzleEffect = Instantiate(Muzzle, transform.position, transform.rotation);
                Destroy(muzzleEffect, 1f); // 1秒后销毁枪口效果
            }

            // 实例化子弹
            GameObject bullet = Instantiate(BulletPrefab, transform.position, transform.rotation);
            activeBullets.Add(bullet);

            // 实例化轨迹效果
            if (Trail != null)
            {
                GameObject trail = Instantiate(Trail, bullet.transform.position, bullet.transform.rotation);
                trail.transform.SetParent(bullet.transform); // 轨迹跟随子弹
                bulletTrailMap.Add(bullet, trail); // 存储子弹与轨迹的对应关系
            }
        }

        void UpdateBullets()
        {
            List<GameObject> bulletsToRemove = new List<GameObject>();

            foreach (GameObject bullet in activeBullets)
            {
                if (bullet == null)
                {
                    bulletsToRemove.Add(bullet);
                    continue;
                }

                // 计算子弹移动
                float distanceMoved = Speed * Time.deltaTime;
                bullet.transform.Translate(Vector3.forward * distanceMoved, Space.Self);

                // 检查距离限制
                float distanceFromOrigin = Vector3.Distance(bullet.transform.position, transform.position);
                if (distanceFromOrigin > DistanceLimit)
                {
                    bulletsToRemove.Add(bullet);

                    // 播放击中效果
                    if (BulletHit != null)
                    {
                        GameObject hitEffect = Instantiate(BulletHit, bullet.transform.position, Quaternion.identity);
                        Destroy(hitEffect, 1f); // 1秒后销毁击中效果
                    }

                    // 处理轨迹：解除父对象关系并设置延迟销毁
                    if (bulletTrailMap.ContainsKey(bullet))
                    {
                        GameObject trail = bulletTrailMap[bullet];
                        if (trail != null)
                        {
                            trail.transform.SetParent(null); // 解除与子弹的父子关系
                            Destroy(trail, TrailLifetime); // 设置轨迹延迟销毁
                        }
                        bulletTrailMap.Remove(bullet); // 从字典中移除
                    }

                    Destroy(bullet); // 销毁子弹
                }
            }

            // 移除已销毁的子弹
            foreach (GameObject bullet in bulletsToRemove)
            {
                activeBullets.Remove(bullet);
            }
        }
    }
}
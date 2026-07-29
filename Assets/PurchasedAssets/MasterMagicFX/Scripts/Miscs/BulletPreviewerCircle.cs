using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace MasterFX
{
public class BulletPreviewerCircle : MonoBehaviour
{
    public GameObject BulletPrefab; // 子弹预制体
    public GameObject Trail;
    public float Radius = 5f; // 圆形轨迹的半径
    public int BulletCount = 10; // 子弹数量
    public float BulletAngleSpeed = 360f; // 旋转速度（度/秒，正值逆时针，负值顺时针）

    private List<GameObject> bullets = new List<GameObject>(); // 存储生成的子弹
    private float[] initialAngles; // 存储每个子弹的初始角度
    public float AngleOffset;
    public void Start()
    {
        // 在开始时生成子弹
        SpawnBullets();
    }

    public void Update()
    {
        // 每帧直接计算子弹的位置和朝向
        UpdateBullets();
        //if press R respawn bullets;
        if (Input.GetKeyDown(KeyCode.R))
        {
            RespawnBullets();
        }
    }
    public void RespawnBullets()
    {
        // 清除现有子弹
        foreach (var bullet in bullets)
        {
            if (bullet != null)
            {
                Destroy(bullet);
            }
        }
        bullets.Clear(); // 清空列表
        // 重新生成子弹
        SpawnBullets();
    }

    private void SpawnBullets()
    {
        // 确保子弹预制体存在
        if (BulletPrefab == null)
        {
            Debug.LogError("BulletPrefab is not assigned!");
            return;
        }

        // 初始化子弹角度数组
        initialAngles = new float[BulletCount];
        float angleStep = 360f / BulletCount;

        // 生成子弹并均匀分布在圆形轨迹上（XZ 平面）
        for (int i = 0; i < BulletCount; i++)
        {
            // 计算当前子弹的初始角度
            float angle = i * angleStep;
            initialAngles[i] = angle; // 存储初始角度
            float radian = angle * Mathf.Deg2Rad;

            // 计算子弹的位置（XZ 平面，Y=0）
            Vector3 spawnPosition = transform.position + new Vector3(
                Mathf.Cos(radian) * Radius,
                0,
                Mathf.Sin(radian) * Radius
            );

            // 实例化子弹
            GameObject bullet = Instantiate(BulletPrefab, spawnPosition, Quaternion.identity);
            bullet.transform.SetParent(transform); // 可选，方便管理
            //Instantiate a trail and set bullet as parent;

            if(Trail!= null)
            {
            GameObject trail = Instantiate(Trail, bullet.transform.position, Quaternion.identity);
            trail.transform.SetParent(bullet.transform);
            }
            // 设置子弹初始朝向（沿切线方向）
            float tangentAngle = angle + (BulletAngleSpeed >= 0 ? 90f : -90f); // 根据旋转方向调整
            bullet.transform.rotation = Quaternion.Euler(0, tangentAngle, 0);
            // 存储子弹
            bullets.Add(bullet);
        }
    }

    private void UpdateBullets()
    {
        // 计算当前时间的角度偏移
        float time = Time.time; // 自游戏开始以来的时间
        float angleOffset = BulletAngleSpeed * time; // 总旋转角度 = 速度 * 时间
        // 更新每个子弹的位置和朝向
        for (int i = 0; i < bullets.Count; i++)
        {
            if (bullets[i] == null) continue; // 防止子弹被销毁后访问空对象

            // 计算当前子弹的绝对角度
            float currentAngle = initialAngles[i] + angleOffset;
            float radian = currentAngle * Mathf.Deg2Rad;

            // 计算新位置（XZ 平面，Y=0）
            Vector3 newPos = transform.position + new Vector3(
                Mathf.Cos(radian) * Radius,
                0,
                Mathf.Sin(radian) * Radius
            );

            // 更新子弹位置
            bullets[i].transform.position = newPos;

            bullets[i].transform.rotation = Quaternion.Euler(0, -currentAngle, 0);
        }
    }
}
}
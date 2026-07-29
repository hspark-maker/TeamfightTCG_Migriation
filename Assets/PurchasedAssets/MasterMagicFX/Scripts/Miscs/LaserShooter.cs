using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MasterFX
{
    [System.Serializable]
    public class LaserData
    {
        public MLaser laser;
        // public Texture2D LUTTexture;
    }
    public class LaserShooter : MonoBehaviour
    {
        public List<LaserData> laserDatas = new List<LaserData>();
        public int curLaserIndex = 0;

        public GameObject curLaser;
        public Transform LaserStartPos;
        void Update()
        {
            //move the index if press A or D;
            if (Input.GetKeyDown(KeyCode.A))
            {
                curLaserIndex--;
                if (curLaserIndex < 0)
                {
                    curLaserIndex = laserDatas.Count - 1;
                }
            }
            if (Input.GetKeyDown(KeyCode.D))
            {
                curLaserIndex++;
                if (curLaserIndex >= laserDatas.Count)
                {
                    curLaserIndex = 0;
                }
            }


            UpdateLaser();
        }

        public void EnableLaser()
        {
            //stop cur laser if existed;
            if (curLaser != null)
            {
                //stop the laser;
                Destroy(curLaser);
            }
            curLaser = Instantiate(laserDatas[curLaserIndex].laser.gameObject);
            // curLaser.GetComponent<MLaser>().LUTTexture = laserDatas[curLaserIndex].LUTTexture;
            curLaser.transform.position = LaserStartPos.position;
        }
        public void DisableLaser()
        {
            if (curLaser != null)
            {
                curLaser.GetComponent<MLaser>().StopLaser();
            }
            curLaser = null;
        }

        void OnDisable()
        {
            DisableLaser();
        }


        public void UpdateLaser()
        {
            if (curLaser != null)
            {
                var LaserHit = new RaycastHit();

                // Obtain the camera reference - assuming there's a main camera
                Camera mainCamera = Camera.main;
                if (mainCamera == null)
                {
                    Debug.LogError("Main camera not found");
                    return;
                }
                // Create a ray from the camera through the mouse position
                Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
                // Perform the raycast
                if (Physics.Raycast(ray, out LaserHit))
                {
                    // Adjusting the laser to point precisely from the laser start to the hit point
                    curLaser.GetComponent<MLaser>().SetLaser(LaserStartPos.position, LaserHit.point);
                }
            }
        }
    }
}
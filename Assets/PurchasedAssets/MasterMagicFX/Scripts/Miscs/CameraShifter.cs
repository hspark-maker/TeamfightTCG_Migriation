using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace MasterFX
{
public class CameraShifter : MonoBehaviour
{
    public Camera cam1;
    public Camera cam2;
    void Update()
    {
        //use S to shift two cameras;
        if (Input.GetKeyDown(KeyCode.Q))
        {
            if (cam1.enabled)
            {
                cam1.enabled = false;
                cam2.enabled = true;
            }
            else
            {
                cam1.enabled = true;
                cam2.enabled = false;
            }
        }
    }
}
}
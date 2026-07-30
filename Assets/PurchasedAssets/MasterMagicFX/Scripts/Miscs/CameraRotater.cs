using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//execute in both play and edit mode
public class CameraRotater : MonoBehaviour
{
    public Transform RotateCenter;
    public float RotateSpeed = 10f;
public float Distance = 10f;
    public float Height = 5f;
    public float CurAngle = 0;
    public float RotSpeed = 30f;

    public float DistanceModifySpeed = 10;
    private void Update()
    {
        //if in playmode; add CurAngle perseconds;
        CurAngle += RotSpeed * Time.deltaTime;
        //press W or S to modify distance;
        if (Input.GetKey(KeyCode.W))
        {
            Distance -= DistanceModifySpeed * Time.deltaTime;
        }
        if (Input.GetKey(KeyCode.S))
        {
            Distance += DistanceModifySpeed * Time.deltaTime;
        }
        UpdateCamera();
    }
    void OnDrawGizmos()
    {
        //draw a line from the camera to the RotateCenter
        Gizmos.color = Color.red;
        Gizmos.DrawLine(transform.position, RotateCenter.position); 

    }
    public void OnValidate()
    {
        UpdateCamera();
    }
    public void UpdateCamera()
    {
  //Set the camera at the angle of CurAngle towards RotateCenter;
        transform.position = RotateCenter.position + Quaternion.Euler(0, CurAngle, 0) * new Vector3(0, 0, -Distance) + new Vector3(0, Height, 0);
        //look at rotate center
        transform.LookAt(RotateCenter.position);
    }
}

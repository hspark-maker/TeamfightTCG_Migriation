using UnityEngine;

public static class CameraUtil
{
    // ScreenToWorldPoint with z=0 gives camera position for perspective cameras.
    // Pass worldZ to project at the correct depth plane.
    public static Vector3 ScreenFractionToWorld(float _xFraction, float _yFraction, float _worldZ)
    {
        float t_depth = Mathf.Abs(Camera.main.transform.position.z - _worldZ);
        Vector3 t_sc  = new Vector3(Screen.width * _xFraction, Screen.height * _yFraction, t_depth);
        Vector3 t_wc  = Camera.main.ScreenToWorldPoint(t_sc);
        t_wc.z = _worldZ;
        return t_wc;
    }
}

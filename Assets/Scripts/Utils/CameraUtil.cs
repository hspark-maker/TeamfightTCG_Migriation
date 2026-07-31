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

    /// <summary>월드 AABB를 화면 px 사각으로. 튜토리얼 포커스 딤이 뚫을 구멍 계산의 단일 지점 —
    /// 카드 하나든 필드 전체든 같은 식을 쓴다. 카메라가 없으면 빈 Rect(호출부가 포커스를 접는다).
    /// min/max 두 점만 변환하면 되는 이유: 카드 평면이 카메라 축에 정렬돼 있어 회전 성분이 없다.</summary>
    public static Rect WorldBoundsToScreenRect(Bounds _bounds, float _paddingPx = 0f)
    {
        Camera t_cam = Camera.main;
        if (t_cam == null) return new Rect();

        Vector3 t_a = t_cam.WorldToScreenPoint(_bounds.min);
        Vector3 t_b = t_cam.WorldToScreenPoint(_bounds.max);

        return Rect.MinMaxRect(
            Mathf.Min(t_a.x, t_b.x) - _paddingPx, Mathf.Min(t_a.y, t_b.y) - _paddingPx,
            Mathf.Max(t_a.x, t_b.x) + _paddingPx, Mathf.Max(t_a.y, t_b.y) + _paddingPx);
    }

    /// <summary>두 화면 사각의 합집합. 어느 쪽이 비어 있으면 나머지를 그대로 돌려준다.</summary>
    public static Rect Union(Rect _a, Rect _b)
    {
        if (_a.width <= 0f || _a.height <= 0f) return _b;
        if (_b.width <= 0f || _b.height <= 0f) return _a;
        return Rect.MinMaxRect(Mathf.Min(_a.xMin, _b.xMin), Mathf.Min(_a.yMin, _b.yMin),
                               Mathf.Max(_a.xMax, _b.xMax), Mathf.Max(_a.yMax, _b.yMax));
    }
}

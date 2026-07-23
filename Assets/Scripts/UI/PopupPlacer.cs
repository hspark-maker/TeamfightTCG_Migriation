using UnityEngine;

/// <summary>
/// "앵커(아이콘/행) 옆에 팝업을 띄우되 화면 밖으로 나가면 반대편으로 넘긴다" 배치 규칙 단일 지점.
/// 키워드 설명, 시너지 설명 팝업, 시너지 툴팁이 공용으로 쓴다 — 세 군데에 각자 두면 동작이 갈린다.
///
/// 계산은 **캔버스 로컬 좌표**에서 하고 결과는 월드 위치로 적용한다.
/// (팝업의 부모가 캔버스 루트가 아닐 수 있어서 — 예: 툴팁은 스트립 자식이라
///  부모 rect 기준으로 클램프하면 스트립 크기에 갇힌다.)
/// </summary>
public static class PopupPlacer
{
    /// <summary>_self를 _anchor 오른쪽에 붙인다. 오른쪽이 캔버스를 넘치면 왼쪽으로 뒤집고,
    /// 그래도 넘치면 캔버스 안으로 클램프한다.</summary>
    /// <param name="_gap">앵커와 팝업 사이 여백(px).</param>
    /// <param name="_edgePadding">캔버스 가장자리 여백(px).</param>
    /// <param name="_clampVertical">true면 세로도 캔버스 안으로 클램프한다.</param>
    public static void PlaceBesideAnchor(RectTransform _self, RectTransform _anchor,
        float _gap, float _edgePadding = 0f, bool _clampVertical = true)
    {
        if (_self == null || _anchor == null) return;

        Canvas t_canvas = _self.GetComponentInParent<Canvas>();
        if (t_canvas == null) return;
        RectTransform t_canvasRect = (RectTransform)t_canvas.transform;
        Camera t_cam = t_canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : t_canvas.worldCamera;

        // 앵커 중심을 캔버스 로컬 좌표로
        Vector2 t_screen = RectTransformUtility.WorldToScreenPoint(
            t_cam, _anchor.TransformPoint(_anchor.rect.center));
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                t_canvasRect, t_screen, t_cam, out Vector2 t_local))
            return;

        Rect  t_bounds     = t_canvasRect.rect;
        float t_halfSelf   = _self.rect.width  * 0.5f;
        float t_halfAnchor = _anchor.rect.width * 0.5f;
        float t_offsetX    = t_halfSelf + t_halfAnchor + _gap;

        // 기본은 오른쪽. 넘치면 왼쪽으로 뒤집는다.
        float t_x = t_local.x + t_offsetX;
        if (t_x + t_halfSelf > t_bounds.xMax - _edgePadding)
            t_x = t_local.x - t_offsetX;

        // 뒤집어도 넘치면(팝업이 화면보다 넓거나 앵커가 구석) 안쪽으로 밀어넣는다.
        float t_minX = t_bounds.xMin + t_halfSelf + _edgePadding;
        float t_maxX = t_bounds.xMax - t_halfSelf - _edgePadding;
        if (t_minX <= t_maxX) t_x = Mathf.Clamp(t_x, t_minX, t_maxX);

        float t_y = t_local.y;
        if (_clampVertical)
        {
            float t_halfH = _self.rect.height * 0.5f;
            float t_minY  = t_bounds.yMin + t_halfH + _edgePadding;
            float t_maxY  = t_bounds.yMax - t_halfH - _edgePadding;
            if (t_minY <= t_maxY) t_y = Mathf.Clamp(t_y, t_minY, t_maxY);
        }

        // 부모가 무엇이든 결과가 같도록 월드 위치로 적용(앵커/피벗 산수 회피).
        _self.position = t_canvasRect.TransformPoint(new Vector3(t_x, t_y, 0f));
    }
}

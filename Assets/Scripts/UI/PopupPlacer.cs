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
        Camera t_cam = t_canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : t_canvas.worldCamera;

        Vector2 t_screen = RectTransformUtility.WorldToScreenPoint(
            t_cam, _anchor.TransformPoint(_anchor.rect.center));
        PlaceBesideScreenPoint(_self, t_screen, _anchor.rect.width * 0.5f, _gap, _edgePadding, _clampVertical);
    }

    /// <summary>월드 오브젝트(스프라이트 배지 등) 옆에 배치. 인게임 카드 위 시너지 배지처럼
    /// uGUI가 아닌 대상에 쓴다. _worldHalfWidth는 대상의 월드 반폭(화면 px로 환산해 간격 계산).</summary>
    public static void PlaceBesideWorldPoint(RectTransform _self, Vector3 _worldPos, float _worldHalfWidth,
        float _gap, float _edgePadding = 0f, bool _clampVertical = true)
    {
        if (_self == null) return;
        Camera t_worldCam = Camera.main;
        if (t_worldCam == null) return;

        Vector2 t_screen = t_worldCam.WorldToScreenPoint(_worldPos);
        // 월드 반폭을 화면 px로: 중심과 (중심+반폭)의 화면 거리
        Vector2 t_edge = t_worldCam.WorldToScreenPoint(_worldPos + Vector3.right * _worldHalfWidth);
        PlaceBesideScreenPoint(_self, t_screen, Mathf.Abs(t_edge.x - t_screen.x), _gap, _edgePadding, _clampVertical);
    }

    /// <summary>화면 좌표 기준 공통 배치. 위 두 진입점이 여기로 수렴한다.</summary>
    public static void PlaceBesideScreenPoint(RectTransform _self, Vector2 _screenPos, float _anchorHalfPx,
        float _gap, float _edgePadding = 0f, bool _clampVertical = true)
    {
        if (_self == null) return;

        Canvas t_canvas = _self.GetComponentInParent<Canvas>();
        if (t_canvas == null) return;
        RectTransform t_canvasRect = (RectTransform)t_canvas.transform;
        Camera t_cam = t_canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : t_canvas.worldCamera;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                t_canvasRect, _screenPos, t_cam, out Vector2 t_local))
            return;

        // 화면 px -> 캔버스 로컬 단위 (CanvasScaler 배율 보정)
        float t_scale = t_canvasRect.rect.width / Mathf.Max(1f, Screen.width);

        Rect  t_bounds     = SafeBounds(t_canvasRect, t_cam);
        float t_halfSelf   = _self.rect.width * 0.5f;
        float t_halfAnchor = _anchorHalfPx * t_scale;
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

    static Rect SafeBounds(RectTransform _canvasRect, Camera _camera)
    {
        Rect t_safe = Screen.safeArea;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRect, t_safe.min, _camera, out Vector2 t_min)
            || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRect, t_safe.max, _camera, out Vector2 t_max)
            || t_max.x <= t_min.x || t_max.y <= t_min.y)
            return _canvasRect.rect;

        return Rect.MinMaxRect(t_min.x, t_min.y, t_max.x, t_max.y);
    }
}

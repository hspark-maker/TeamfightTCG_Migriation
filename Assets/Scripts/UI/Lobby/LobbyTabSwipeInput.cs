using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Recognizes a horizontal release gesture within one lobby input region.</summary>
public sealed class LobbyTabSwipeInput : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [SerializeField] LobbyTabController controller;
    [Range(0.08f, 0.5f)] [SerializeField] float snapRatio = 0.22f;
    [SerializeField] float flickSpeed = 700f;
    [Range(0f, 0.3f)] [SerializeField] float flickMinRatio = 0.03f;

    Canvas m_canvas;
    bool m_dragging;
    int m_pointerId;
    int m_version;
    Vector2 m_begin;
    Vector2 m_lastPosition;
    float m_speed;
    float m_lastMoveTime;

    public void OnBeginDrag(PointerEventData _event)
    {
        if (m_dragging || _event.button != PointerEventData.InputButton.Left
            || controller == null || !controller.CanSwipe) return;

        GameObject t_pressed = _event.pointerPressRaycast.gameObject;
        if (!IsRegionHit(t_pressed) || IsExcluded(t_pressed)) return;

        Vector2 t_move = _event.position - _event.pressPosition;
        if (Mathf.Abs(t_move.x) <= Mathf.Abs(t_move.y)) return;

        m_dragging = true;
        m_pointerId = _event.pointerId;
        m_version = controller.SwipeVersion;
        m_begin = _event.pressPosition;
        m_lastPosition = m_begin;
        m_speed = 0f;
        m_lastMoveTime = Time.unscaledTime;
        Sample(_event.position);
    }

    public void OnDrag(PointerEventData _event)
    {
        if (!Validate(_event)) return;
        Sample(_event.position);
    }

    public void OnEndDrag(PointerEventData _event)
    {
        if (!Validate(_event)) return;
        m_dragging = false;

        // A card long-press can end the ScrollRect gesture to take ownership.
        // That cancellation is not a finger release and must never switch tabs.
        if (!_event.dragging) return;

        // A modal opened during the gesture must still own its release.
        GameObject t_hit = _event.pointerCurrentRaycast.gameObject;
        if (t_hit != null)
        {
            LobbyTabSwipeInput t_region = t_hit.GetComponentInParent<LobbyTabSwipeInput>();
            if (t_region == null || t_region.controller != controller || t_region.IsExcluded(t_hit)) return;
        }

        Sample(_event.position);
        float t_delta = (_event.position.x - m_begin.x) / CanvasScale();
        float t_width = CanvasWidth();
        float t_speed = Time.unscaledTime - m_lastMoveTime <= 0.1f ? m_speed : 0f;
        bool t_flick = flickSpeed > 0f && Mathf.Abs(t_speed) >= flickSpeed
            && Mathf.Abs(t_delta) >= t_width * flickMinRatio;
        if (Mathf.Abs(t_delta) < t_width * snapRatio && !t_flick) return;

        controller.TrySwipe(t_delta > 0f ? -1 : 1);
    }

    bool Validate(PointerEventData _event)
    {
        if (!m_dragging || _event.pointerId != m_pointerId) return false;
        if (controller != null && controller.CanSwipe && controller.SwipeVersion == m_version)
            return true;
        m_dragging = false;
        return false;
    }

    bool IsRegionHit(GameObject _hit)
        => _hit != null && (_hit.transform == transform || _hit.transform.IsChildOf(transform));

    bool IsExcluded(GameObject _hit)
    {
        // Child drag handlers normally consume these gestures; also guard relayed events.
        for (Transform t = _hit.transform; t != null && t != transform; t = t.parent)
        {
            DeckEditController t_editor = t.GetComponent<DeckEditController>();
            if (t.GetComponent<PackCarouselView>() != null
                || t.GetComponent<SingletonOverlayBase>() != null
                || (t.GetComponent<PooledUIBase>() != null
                    && (t_editor == null || !t_editor.IsHostEmbedded)))
                return true;
        }
        return false;
    }

    void Sample(Vector2 _position)
    {
        float t_move = (_position.x - m_lastPosition.x) / CanvasScale();
        if (!Mathf.Approximately(t_move, 0f))
        {
            float t_dt = Time.unscaledDeltaTime;
            m_speed = t_dt > 0f ? t_move / t_dt : 0f;
            m_lastMoveTime = Time.unscaledTime;
        }
        m_lastPosition = _position;
    }

    float CanvasScale()
    {
        if (m_canvas == null && controller != null)
        {
            Canvas t_canvas = controller.GetComponentInParent<Canvas>();
            if (t_canvas != null) m_canvas = t_canvas.rootCanvas;
        }
        return m_canvas != null && m_canvas.scaleFactor > 0f ? m_canvas.scaleFactor : 1f;
    }

    float CanvasWidth()
    {
        float t_scale = CanvasScale();
        RectTransform t_rect = m_canvas != null ? m_canvas.transform as RectTransform : null;
        return t_rect != null && t_rect.rect.width > 1f ? t_rect.rect.width : Screen.width / t_scale;
    }

    void OnDisable() => m_dragging = false;
}

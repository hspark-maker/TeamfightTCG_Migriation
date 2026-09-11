using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 셀은 드래그 인터페이스를 구현하지 않는다. 일반 스와이프는 부모 ScrollRect가 받는다.
public class EmoteEditDragController : MonoBehaviour
{
    IReadOnlyList<EmoteItemCell> m_slots;
    Action<int, int, int> m_onDropped;
    Action m_onEnded;
    Tween m_flyTween;
    RectTransform m_layer;
    Image m_ghost;
    Canvas m_canvas;
    EmoteItemCell m_source;
    PointerEventData m_pointer;
    ScrollRect m_scroll;
    bool m_vertical, m_horizontal;
    int m_finger = -1;
    public bool IsDragging => this.m_pointer != null;
    Camera EventCamera => this.m_canvas != null && this.m_canvas.renderMode != RenderMode.ScreenSpaceOverlay
        ? this.m_canvas.worldCamera : null;

    public void Initialize(RectTransform _layer, IReadOnlyList<EmoteItemCell> _slots, Action<int, int, int> _onDropped, Action _onEnded = null)
    {
        this.m_layer = _layer;
        this.m_canvas = _layer.GetComponentInParent<Canvas>();
        this.m_slots = _slots;
        this.m_onDropped = _onDropped;
        this.m_onEnded = _onEnded;
    }

    public void Begin(EmoteItemCell _source, PointerEventData _pointer, ScrollRect _scroll)
    {
        this.Cancel();
        if (this.m_layer == null || _source == null || _source.IsSlot || _source.EmoteId <= 0 || _pointer == null) return;
        if (_pointer.pointerDrag != null && _pointer.dragging)
        {
            _pointer.dragging = false;
            ExecuteEvents.Execute(_pointer.pointerDrag, _pointer, ExecuteEvents.endDragHandler);
        }
        _pointer.dragging = false;
        _pointer.pointerDrag = null;
        this.m_source = _source;
        this.m_pointer = _pointer;
        _pointer.eligibleForClick = false;
        this.m_finger = -1;
        float t_nearest = float.MaxValue;
        for (int t_i = 0; t_i < Input.touchCount; t_i++)
        {
            Touch t_touch = Input.GetTouch(t_i);
            if (t_touch.phase == TouchPhase.Ended || t_touch.phase == TouchPhase.Canceled) continue;
            float t_distance = Vector2.SqrMagnitude(t_touch.position - _pointer.position);
            if (t_distance >= t_nearest) continue;
            t_nearest = t_distance;
            this.m_finger = t_touch.fingerId;
        }
        this.m_scroll = _scroll;
        if (_scroll != null)
        {
            this.m_vertical = _scroll.vertical;
            this.m_horizontal = _scroll.horizontal;
            _scroll.StopMovement();
            _scroll.vertical = false;
            _scroll.horizontal = false;
        }
        this.EnsureGhost();
        this.m_ghost.sprite = _source.Sprite;
        this.m_ghost.rectTransform.sizeDelta = _source.Rect.rect.size;
        this.m_ghost.transform.SetAsLastSibling();
        this.m_ghost.gameObject.SetActive(true);
        this.Move(_pointer.position);
    }

    void EnsureGhost()
    {
        if (this.m_ghost == null)
        {
            var t_go = new GameObject("EmoteDragGhost", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
            t_go.transform.SetParent(this.m_layer, false);
            t_go.GetComponent<LayoutElement>().ignoreLayout = true;
            this.m_ghost = t_go.GetComponent<Image>();
            this.m_ghost.raycastTarget = false;
            this.m_ghost.preserveAspect = true;
        }
        this.m_ghost.rectTransform.anchorMin = this.m_ghost.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        this.m_ghost.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        this.m_ghost.transform.localScale = Vector3.one * 1.05f;
        this.m_ghost.color = new Color(1f, 1f, 1f, 0.85f);
    }

    public void FlyOut(Sprite _sprite, RectTransform _from, RectTransform _to, Vector2 _cellSize)
    {
        if (this.IsDragging || this.m_layer == null || _sprite == null || _from == null || _to == null) return;
        this.KillFly();
        this.EnsureGhost();
        RectTransform t_rect = this.m_ghost.rectTransform;
        t_rect.sizeDelta = _from.rect.size;
        t_rect.localPosition = this.ToLayerPoint(_from);
        this.m_ghost.sprite = _sprite;
        this.m_ghost.transform.SetAsLastSibling();
        this.m_ghost.gameObject.SetActive(true);
        Sequence t_sequence = DOTween.Sequence();
        t_sequence.Append(t_rect.DOLocalMove(this.ToLayerPoint(_to), 0.26f).SetEase(Ease.InQuad));
        t_sequence.Join(t_rect.DOSizeDelta(_cellSize, 0.26f).SetEase(Ease.InQuad));
        t_sequence.Join(t_rect.DOScale(1.05f * 0.5f, 0.26f).SetEase(Ease.InQuad));
        t_sequence.Join(this.m_ghost.DOFade(0f, 0.26f).SetEase(Ease.InQuad));
        this.m_flyTween = t_sequence.OnComplete(() =>
        {
            this.m_flyTween = null;
            if (this.m_ghost != null) this.m_ghost.gameObject.SetActive(false);
        }).SetLink(this.gameObject);
    }

    Vector3 ToLayerPoint(RectTransform _rect)
    {
        Vector2 t_screen = RectTransformUtility.WorldToScreenPoint(this.EventCamera, _rect.TransformPoint(_rect.rect.center));
        RectTransformUtility.ScreenPointToLocalPointInRectangle(this.m_layer, t_screen, this.EventCamera, out Vector2 t_local);
        return new Vector3(t_local.x, t_local.y, 0f);
    }

    void KillFly()
    {
        this.m_flyTween?.Kill();
        this.m_flyTween = null;
    }

    void Update()
    {
        if (this.m_pointer == null) return;
        Vector2 t_position = this.m_pointer.position;
        bool t_held = false;
        bool t_cancelled = false;
        if (this.m_finger < 0)
        {
            t_position = Input.mousePosition;
            t_held = Input.GetMouseButton(0);
        }
        else
        {
            t_cancelled = true;
            for (int t_i = 0; t_i < Input.touchCount; t_i++)
            {
                Touch t_touch = Input.GetTouch(t_i);
                if (t_touch.fingerId != this.m_finger) continue;
                t_position = t_touch.position;
                t_cancelled = t_touch.phase == TouchPhase.Canceled;
                t_held = !t_cancelled && t_touch.phase != TouchPhase.Ended;
                break;
            }
        }
        this.Move(t_position);
        if (t_held) return;
        int t_slot = t_cancelled ? -1 : this.HitTest(t_position);
        int t_sourceSlot = this.m_source.IsSlot ? this.m_source.Key : -1;
        int t_id = this.m_source.EmoteId;
        this.Cancel();
        if (t_slot >= 0) this.m_onDropped?.Invoke(t_slot, t_id, t_sourceSlot);
    }

    void Move(Vector2 _position)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(this.m_layer, _position, this.EventCamera, out Vector2 t_local);
        this.m_ghost.rectTransform.localPosition = new Vector3(t_local.x, t_local.y, 0f);
        int t_hit = this.HitTest(_position);
        for (int t_i = 0; t_i < this.m_slots.Count; t_i++)
            if (this.m_slots[t_i] != null) this.m_slots[t_i].SetHighlight(t_i == t_hit);
    }

    int HitTest(Vector2 _position)
    {
        if (this.m_slots == null) return -1;
        for (int t_i = 0; t_i < this.m_slots.Count; t_i++)
        {
            EmoteItemCell t_slot = this.m_slots[t_i];
            if (t_slot != null && t_slot.gameObject.activeInHierarchy &&
                RectTransformUtility.RectangleContainsScreenPoint(t_slot.Rect, _position, this.EventCamera)) return t_i;
        }
        return -1;
    }

    public void Cancel()
    {
        bool t_wasDragging = this.IsDragging;
        this.KillFly();
        if (this.m_pointer != null) this.m_pointer.eligibleForClick = false;
        if (this.m_source != null) this.m_source.CancelGesture();
        this.m_pointer = null;
        this.m_source = null;
        this.m_finger = -1;
        if (this.m_ghost != null) this.m_ghost.gameObject.SetActive(false);
        if (this.m_slots != null)
            foreach (EmoteItemCell t_slot in this.m_slots)
                if (t_slot != null) t_slot.SetHighlight(false);
        if (this.m_scroll != null)
        {
            this.m_scroll.vertical = this.m_vertical;
            this.m_scroll.horizontal = this.m_horizontal;
            this.m_scroll.StopMovement();
            this.m_scroll = null;
        }
        if (t_wasDragging) this.m_onEnded?.Invoke();
    }

    void OnDisable() => this.Cancel();
    void OnApplicationFocus(bool _focused) { if (!_focused) this.Cancel(); }
    void OnDestroy() { if (this.m_ghost != null) Destroy(this.m_ghost.gameObject); }
}

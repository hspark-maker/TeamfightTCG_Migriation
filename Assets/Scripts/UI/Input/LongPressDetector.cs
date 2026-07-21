using System;
using UnityEngine;
using UnityEngine.EventSystems;

public class LongPressDetector : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    const float THRESHOLD  = 0.45f;
    const float CANCEL_PX  = 12f;   // pointer drift > this cancels long press (scroll 구분)

    public Action OnLongPress;

    bool    pressing, fired;
    float   timer;
    Vector2 startPos;

    void Update()
    {
        if (!this.pressing || this.fired) return;
        if (Vector2.Distance(Input.mousePosition, this.startPos) > CANCEL_PX)
        {
            this.pressing = false;
            return;
        }
        this.timer += Time.deltaTime;
        if (this.timer >= THRESHOLD)
        {
            this.fired = true;
            OnLongPress?.Invoke();
        }
    }

    public void OnPointerDown(PointerEventData _data)
    {
        this.pressing = true;
        this.timer    = 0f;
        this.fired    = false;
        this.startPos = _data.position;
    }

    public void OnPointerUp(PointerEventData _) { this.pressing = false; }
}

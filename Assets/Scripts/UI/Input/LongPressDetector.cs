using System;
using UnityEngine;
using UnityEngine.EventSystems;

public class LongPressDetector : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    // const였으나 사용처마다 반응 속도가 달라야 해서(키워드 툴팁 0.45초 vs 덱 편집 드래그 개시) 필드로 승격했다.
    // 기본값은 기존 const와 동일 — 이미 배치된 프리팹들은 이 값을 직렬화하지 않았으므로 기본값이 그대로 적용된다.
    [SerializeField] float threshold      = 0.45f;
    [SerializeField] float cancelDistance = 12f;   // pointer drift > this cancels long press (scroll 구분)

    public Action OnLongPress;

    bool    pressing, fired;
    float   timer;
    Vector2 startPos;

    void Update()
    {
        if (!this.pressing || this.fired) return;
        if (Vector2.Distance(Input.mousePosition, this.startPos) > this.cancelDistance)
        {
            this.pressing = false;
            return;
        }
        this.timer += Time.deltaTime;
        if (this.timer >= this.threshold)
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

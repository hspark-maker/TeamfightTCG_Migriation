using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 세로 스크롤 영역 위에서 시작한 **가로** 드래그를 상위로 흘려보내는 ScrollRect.
//
// 스톡 ScrollRect는 축과 무관하게 드래그를 통째로 삼킨다 — 그래서 스크롤 영역이 화면 절반을 덮으면
// 그 위에서는 바깥의 <see cref="HorizontalSwipeDetector"/>(카드 넘기기)가 영영 호출되지 않는다.
//
// 주인은 OnBeginDrag에서 한 번만 정한다. 드래그 도중 축이 바뀌었다고 주인을 넘기면
// 스크롤이 중간에 끊기고, 넘기기는 이미 절반쯤 지나간 이동량을 처음부터인 양 받는다.
public class SwipeThroughScrollRect : ScrollRect
{
    // 이번 드래그를 넘겨받은 상위 핸들러. null이면 스크롤인 내가 처리 중이다.
    GameObject m_relay;

    public override void OnBeginDrag(PointerEventData _e)
    {
        this.m_relay = null;

        // _e.delta는 이 시점에 한 프레임분이라 축 판정이 흔들린다 — 누른 자리부터의 총 이동량으로 본다.
        Vector2 t_move = _e.position - _e.pressPosition;

        if (Mathf.Abs(t_move.x) > Mathf.Abs(t_move.y) && transform.parent != null)
            this.m_relay = ExecuteEvents.ExecuteHierarchy(transform.parent.gameObject, _e, ExecuteEvents.beginDragHandler);

        // 위에 받을 사람이 없으면(넘기기가 꺼진 1장짜리 등) 그냥 내가 스크롤한다.
        if (this.m_relay != null) return;

        base.OnBeginDrag(_e);
    }

    public override void OnDrag(PointerEventData _e)
    {
        if (this.m_relay == null) { base.OnDrag(_e); return; }

        ExecuteEvents.Execute(this.m_relay, _e, ExecuteEvents.dragHandler);
    }

    public override void OnEndDrag(PointerEventData _e)
    {
        if (this.m_relay == null) { base.OnEndDrag(_e); return; }

        ExecuteEvents.Execute(this.m_relay, _e, ExecuteEvents.endDragHandler);
        this.m_relay = null;
    }

    // 넘겨준 상태로 굳으면 다음 드래그가 통째로 상위로 샌다(비활성화는 EndDrag 없이 들어올 수 있다).
    protected override void OnDisable()
    {
        this.m_relay = null;
        base.OnDisable();
    }
}

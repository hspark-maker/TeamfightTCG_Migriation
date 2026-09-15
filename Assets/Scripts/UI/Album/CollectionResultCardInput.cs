using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>누르고 있는 카드의 재사용을 막고 스크롤과 상세 열기 탭을 구분한다.</summary>
public sealed class CollectionResultCardInput : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    CardVisualView m_visual;
    PointerEventData m_pointer;
    System.Action m_onTap;
    Vector2 m_pressPosition;
    bool m_clickable;
    public int Card { get; private set; }
    public bool HasActivePointer => m_pointer != null && m_pointer.pointerPress == gameObject;

    public void Initialize(CardVisualView visual)
    {
        m_visual = visual;
        var press = GetComponent<LongPressDetector>();
        if (press != null) press.enabled = false;
    }

    public void Bind(int card, IReadOnlyList<int> cards, int index)
    {
        Unbind();
        Card = card;
        m_visual.Bind(card, OwnershipManager.IsOwned(card));
        m_onTap = () => CardDetailOverlayView.Open(cards, index);
    }

    public void OnPointerDown(PointerEventData data)
    {
        m_pointer = data;
        m_pressPosition = data.position;
        m_clickable = true;
    }

    void Update()
    {
        if (m_pointer != null && Vector2.Distance(m_pointer.position, m_pressPosition) > 12f)
            m_clickable = false;
    }

    public void OnPointerUp(PointerEventData data)
    {
        bool tap = m_clickable && m_pointer == data && !data.dragging
            && Vector2.Distance(data.position, m_pressPosition) <= 12f;
        m_pointer = null;
        m_clickable = false;
        if (tap) m_onTap?.Invoke();
    }

    public void Unbind()
    {
        if (HasActivePointer) m_pointer.eligibleForClick = false;
        m_pointer = null;
        m_clickable = false;
        Card = 0;
        m_onTap = null;
    }

    void OnDisable() => Unbind();
}

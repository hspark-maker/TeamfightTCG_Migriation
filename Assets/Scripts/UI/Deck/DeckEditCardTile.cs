using System;
using UnityEngine;
using UnityEngine.EventSystems;

// 덱 편집 화면 하단 컬렉션 그리드의 카드 타일 1장.
// 표시는 CardVisualView에 전부 위임하고, 여기서는 "롱프레스 → 드래그 요청 중계"와 "장착중 딤"만 한다.
//
// IDragHandler 계열(IBeginDragHandler/IDragHandler/IEndDragHandler)은 절대 구현하지 않는다 —
// 구현하면 uGUI가 드래그 타깃을 이 타일로 잡아버려 부모 ScrollRect가 드래그를 못 받는다
// (Assets/Scripts/UI/MainMenu/SynergyCountIcon.cs:89와 동일한 이유).
// 그래서 드래그 개시는 롱프레스로만 하고, 실제 이동은 DeckEditDragController가 Update 폴링으로 처리한다.
public class DeckEditCardTile : MonoBehaviour, IPointerDownHandler
{
    [SerializeField] CardVisualView     view;
    [SerializeField] LongPressDetector  longPress;
    [SerializeField] GameObject         inDeckOverlay;
    [SerializeField] CanvasGroup        canvasGroup;

    CardData                                       m_card;
    bool                                           m_inDeck;
    PointerEventData                               m_pointerData;
    Action<DeckEditCardTile, PointerEventData>     m_onDragRequest;

    public CardData Card   => m_card;
    public bool     InDeck => m_inDeck;

    public void Bind(CardData _card, Action<DeckEditCardTile, PointerEventData> _onDragRequest)
    {
        m_card          = _card;
        m_onDragRequest = _onDragRequest;

        // 그리드에는 소유 카드만 올라오므로 owned는 true 고정(잠김 실루엣 경로가 아니다).
        if (view != null) view.Bind(_card, true);

        if (longPress != null)
        {
            // 프리팹 재사용/재바인딩 시 같은 핸들러가 중복 등록돼 드래그가 두 번 발화하는 것을 막는다.
            longPress.OnLongPress -= OnLongPressFired;
            longPress.OnLongPress += OnLongPressFired;
        }

        SetInDeck(false);
    }

    public void SetInDeck(bool _on)
    {
        m_inDeck = _on;

        if (inDeckOverlay != null) inDeckOverlay.SetActive(_on);
        if (canvasGroup   != null) canvasGroup.alpha = _on ? 0.45f : 1f;

        // 발화 시점 가드(OnLongPressFired)와 별개로 타이머 자체를 꺼두는 2중 가드.
        if (longPress != null) longPress.enabled = !_on;
    }

    // 참조만 보관한다. StandaloneInputModule은 포인터 id별로 PointerEventData 인스턴스를 재사용·갱신하므로
    // 이 참조는 항상 최신 위치를 가리킨다(복사해두면 오히려 낡은 좌표로 드래그가 시작된다).
    public void OnPointerDown(PointerEventData _data)
    {
        m_pointerData = _data;
    }

    void OnDestroy()
    {
        if (longPress != null) longPress.OnLongPress -= OnLongPressFired;
    }

    void OnLongPressFired()
    {
        if (m_inDeck || m_card == null || m_pointerData == null) return;

        m_onDragRequest?.Invoke(this, m_pointerData);
    }
}

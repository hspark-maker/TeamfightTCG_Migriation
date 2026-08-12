using System;
using UnityEngine;
using UnityEngine.EventSystems;

// 덱 편집 화면 하단 컬렉션 그리드의 카드 타일 1장.
// 표시는 CardVisualView에 전부 위임하고, 여기서는 "롱프레스 → 드래그 요청 중계", "클릭 → 자동 배치 요청 중계",
// "장착중 딤"만 한다.
//
// IDragHandler 계열(IBeginDragHandler/IDragHandler/IEndDragHandler)은 절대 구현하지 않는다 —
// 구현하면 uGUI가 드래그 타깃을 이 타일로 잡아버려 부모 ScrollRect가 드래그를 못 받는다
// (Assets/Scripts/UI/MainMenu/SynergyCountIcon.cs:89와 동일한 이유).
// 그래서 드래그 개시는 롱프레스로만 하고, 실제 이동은 DeckEditDragController가 Update 폴링으로 처리한다.
// 반면 IPointerClickHandler는 드래그 라우팅과 무관해 그대로 구현해도 ScrollRect가 죽지 않는다.
// 다만 LongPressDetector와 이 컴포넌트는 반드시 같은 GameObject(타일 루트)에 있어야 한다 —
// 자식에 IPointerDownHandler가 붙으면 pointerPress가 그 자식이 되고, 릴리즈 때 클릭 대상 비교가 어긋나
// 정상 탭에서도 OnPointerClick이 아예 오지 않는다.
public class DeckEditCardTile : MonoBehaviour, IPointerDownHandler, IPointerClickHandler
{
    [SerializeField] CardVisualView     view;
    [SerializeField] LongPressDetector  longPress;
    [SerializeField] GameObject         inDeckOverlay;
    [SerializeField] CanvasGroup        canvasGroup;

    const float IN_DECK_ALPHA = 0.45f;   // 이미 편성된 카드(클릭 대상 아님)
    const float FOCUS_DIM_ALPHA = 0.2f;  // 시너지 강조 중 해당 없는 카드

    CardData                                       m_card;
    bool                                           m_inDeck;
    bool                                           m_focusDimmed;
    PointerEventData                               m_pointerData;
    Action<DeckEditCardTile, PointerEventData>     m_onDragRequest;
    Action<DeckEditCardTile>                       m_onClick;

    public CardData Card   => m_card;
    public bool     InDeck => m_inDeck;

    public void Bind(CardData _card, Action<DeckEditCardTile, PointerEventData> _onDragRequest, Action<DeckEditCardTile> _onClick)
    {
        m_card          = _card;
        m_onDragRequest = _onDragRequest;
        m_onClick       = _onClick;

        // 그리드에는 소유 카드만 올라오므로 owned는 true 고정(잠김 실루엣 경로가 아니다).
        if (view != null) view.Bind(_card, true);

        if (longPress != null)
        {
            // 프리팹 재사용/재바인딩 시 같은 핸들러가 중복 등록돼 드래그가 두 번 발화하는 것을 막는다.
            longPress.OnLongPress -= OnLongPressFired;
            longPress.OnLongPress += OnLongPressFired;
        }

        m_focusDimmed = false;
        SetInDeck(false);
    }

    public void SetInDeck(bool _on)
    {
        m_inDeck = _on;

        if (inDeckOverlay != null) inDeckOverlay.SetActive(_on);
        ApplyAlpha();

        // 발화 시점 가드(OnLongPressFired)와 별개로 타이머 자체를 꺼두는 2중 가드.
        if (longPress != null) longPress.enabled = !_on;
    }

    // 시너지 아이콘 롱프레스 중 호출. 해당 시너지가 없는 카드를 눌러 대상 카드만 남긴다.
    public void SetSynergyFocus(bool _focusing, bool _match)
    {
        m_focusDimmed = _focusing && !_match;
        ApplyAlpha();
    }

    // 딤 요인이 둘(장착중 / 시너지 강조 제외)이라 곱하지 않고 한 곳에서 우선순위로 정한다 —
    // 두 곳에서 각자 alpha를 쓰면 해제 순서에 따라 흐린 채로 굳는다.
    void ApplyAlpha()
    {
        if (canvasGroup == null) return;

        canvasGroup.alpha = m_focusDimmed ? FOCUS_DIM_ALPHA
                          : m_inDeck      ? IN_DECK_ALPHA
                                          : 1f;
    }

    // 참조만 보관한다. StandaloneInputModule은 포인터 id별로 PointerEventData 인스턴스를 재사용·갱신하므로
    // 이 참조는 항상 최신 위치를 가리킨다(복사해두면 오히려 낡은 좌표로 드래그가 시작된다).
    public void OnPointerDown(PointerEventData _data)
    {
        m_pointerData = _data;
    }

    // 클릭 = 빈 칸 자동 배치 요청. 어느 칸에 넣을지는 편성 상태를 아는 컨트롤러가 정한다.
    //
    // 스크롤·드래그와 충돌하지 않는다: ScrollRect 스크롤로 임계값을 넘기면 입력 모듈이 eligibleForClick을 내리고,
    // 롱프레스 드래그는 DeckEditDragController가 Begin/Finish 양쪽에서 같은 플래그를 눌러둔다.
    public void OnPointerClick(PointerEventData _data)
    {
        // 입력 모듈은 우클릭·휠클릭에도 클릭 핸들러를 태운다 — 에디터/PC에서 오배치가 나지 않게 좌클릭만 받는다.
        if (_data != null && _data.button != PointerEventData.InputButton.Left) return;
        if (m_inDeck || m_card == null) return;   // 이미 편성된 카드(딤 상태)는 클릭 대상이 아니다

        m_onClick?.Invoke(this);
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

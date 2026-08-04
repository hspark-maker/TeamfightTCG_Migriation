using System;
using UnityEngine;
using UnityEngine.UI;

// 덱 편집 화면 상단 편성 6칸 중 한 칸.
// 칸 자체는 상태를 갖지 않고 Bind로 받은 값만 그린다 — 진실원은 DeckEditController.m_working 배열 하나뿐이라
// "화면엔 있는데 배열엔 없는" 불일치가 성립하지 않는다.
//
// 카드 표시(아트/이름/HP/키워드/시너지)는 전부 CardVisualView에 위임한다 — 도감·컬렉션 타일과 같은 컴포넌트를
// 쓰므로 편성 칸의 카드가 다른 화면과 다르게 보일 수 없다. 이 클래스가 남겨 가진 책임은
// 슬롯 상태(빈칸 표시 / 드래그 오버 하이라이트 / 클릭 해제)뿐이다.
// 드래그 드롭 판정은 DeckEditDragController가 Rect(월드 코너)로 하므로 이 클래스는 클릭(해제)만 다룬다.
public class DeckEditSlotView : MonoBehaviour
{
    [SerializeField] Button         clickButton;      // 칸 전체 버튼(클릭 = 해제)
    [SerializeField] CardVisualView cardVisual;       // 카드 비주얼 단일 진실원(빈 칸이면 스스로 숨는다)
    [SerializeField] GameObject     emptyMark;        // 빈 칸 표시(+ 아이콘 등). cardVisual 바깥에 두어야 통째로 꺼져도 남는다.
    [SerializeField] GameObject     highlightFrame;   // 드래그 오버 하이라이트 테두리

    int         m_index = -1;
    CardData    m_card;
    Action<int> m_onClick;

    public int           Index => m_index;
    public CardData      Card  => m_card;
    public RectTransform Rect  => (RectTransform)transform;

    // _card가 null이면 빈 칸 모드. 편집 중 매 변경마다 전량 재바인딩되는 전제라 상태를 남기지 않는다.
    //
    // _synergyState: 편성 중인 덱 6장의 시너지 스냅샷. 카드의 시너지 배지가 이걸로 활성/비활성 그림을 가른다
    // — 한 장 넣고 뺄 때마다 어떤 시너지가 열리고 닫히는지가 카드 위에서 바로 보이는 게 편성의 핵심 피드백이다.
    // 기본 null은 "판정할 덱이 없다"는 뜻이고, 그때는 배지가 전부 활성 그림으로 뜬다(CardVisualView 규약).
    public void Bind(int _index, CardData _card, Action<int> _onClick, SynergyState _synergyState = null)
    {
        m_index   = _index;
        m_card    = _card;
        m_onClick = _onClick;

        if (clickButton != null)
        {
            clickButton.onClick.RemoveAllListeners();   // 재바인딩마다 호출되므로 중복 등록 방지 필수
            clickButton.onClick.AddListener(() => m_onClick?.Invoke(m_index));
        }

        bool t_has = _card != null;

        // 편성 칸에는 소유한 카드만 올라간다(컬렉션 목록 자체가 소유분만 노출) → _owned는 true 고정.
        // 여기서 소유여부를 다시 계산하면 편성 배열과 소유 세이브라는 두 진실원이 생긴다.
        // _card가 null이면 cardVisual이 자기 오브젝트를 꺼서 빈 칸이 된다.
        if (cardVisual != null) cardVisual.Bind(_card, true, _applyGrowth: true, _synergyState: _synergyState);

        if (emptyMark != null) emptyMark.SetActive(!t_has);

        SetHighlight(false);   // 재바인딩은 드래그 종료 직후에도 일어난다 → 하이라이트 잔상 제거
    }

    public void SetHighlight(bool _on)
    {
        if (highlightFrame != null) highlightFrame.SetActive(_on);
    }
}

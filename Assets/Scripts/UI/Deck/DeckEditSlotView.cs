using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 덱 편집 화면 상단 편성 6칸 중 한 칸.
// 칸 자체는 상태를 갖지 않고 Bind로 받은 값만 그린다 — 진실원은 DeckEditController.m_working 배열 하나뿐이라
// "화면엔 있는데 배열엔 없는" 불일치가 성립하지 않는다.
// 드래그 드롭 판정은 DeckEditDragController가 Rect(월드 코너)로 하므로 이 클래스는 클릭(해제)만 다룬다.
public class DeckEditSlotView : MonoBehaviour
{
    [SerializeField] Button     clickButton;      // 칸 전체 버튼(클릭 = 해제)
    [SerializeField] Image      portrait;         // 카드 일러스트
    [SerializeField] TMP_Text   nameText;         // 카드명
    [SerializeField] GameObject emptyMark;        // 빈 칸 표시(+ 아이콘 등)
    [SerializeField] GameObject highlightFrame;   // 드래그 오버 하이라이트 테두리

    int         m_index = -1;
    CardData    m_card;
    Action<int> m_onClick;

    public int           Index => m_index;
    public CardData      Card  => m_card;
    public RectTransform Rect  => (RectTransform)transform;

    // _card가 null이면 빈 칸 모드. 편집 중 매 변경마다 전량 재바인딩되는 전제라 상태를 남기지 않는다.
    public void Bind(int _index, CardData _card, Action<int> _onClick)
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

        if (portrait != null)
        {
            portrait.sprite  = t_has ? _card.fullImage : null;
            // 스프라이트가 없는 카드는 Image를 꺼야 한다 — 켠 채 두면 흰 사각형이 남는다.
            portrait.enabled = portrait.sprite != null;
        }

        if (nameText != null)
        {
            // 표기 정본은 displayName. 에셋 파일명(_card.name)은 저장 키 전용이다(CardElement의 오용을 복제하지 않는다).
            nameText.text = t_has ? _card.displayName : string.Empty;
            nameText.gameObject.SetActive(t_has);
        }

        if (emptyMark != null) emptyMark.SetActive(!t_has);

        SetHighlight(false);   // 재바인딩은 드래그 종료 직후에도 일어난다 → 하이라이트 잔상 제거
    }

    public void SetHighlight(bool _on)
    {
        if (highlightFrame != null) highlightFrame.SetActive(_on);
    }
}

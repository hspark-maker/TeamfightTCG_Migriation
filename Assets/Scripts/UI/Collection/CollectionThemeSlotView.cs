using UnityEngine;
using TMPro;

// 도감 슬롯 한 칸(슬롯 프리팹 루트에 부착). 카드 그림은 CardVisualView에 위임하고 "빈 자리 + 번호"만 얹는다.
// 감싸는 이유: 슬롯 번호는 이 화면 전용 문맥이라 CardVisualView.Bind에 넣으면 8개 소비자의 공용 API가 오염된다.
//
// 테마 행(CollectionThemeRowView)과 평면 그리드(CollectionGridController)가 같은 슬롯 프리팹을 공유한다 —
// 빈 칸의 생김새와 "미소유면 번호만" 규칙이 도감 안에서 갈라지지 않게 하려는 것이다.
public class CollectionThemeSlotView : MonoBehaviour
{
    [SerializeField] CardVisualView cardView;    // 중첩 CardUIView 인스턴스
    [SerializeField] GameObject     emptySlot;   // 미소유 전용 노드(테두리 + 번호)
    [SerializeField] TMP_Text       numberText;
    [SerializeField] string         numberFormat = "000";

    /// <summary>롱프레스(상세 오버레이) 배선 대상. 슬롯을 만든 행이 사용한다.</summary>
    public CardVisualView CardView => cardView;

    // _card가 null이면(authoring 누락 슬롯) 미소유와 동일하게 번호만 보여준다.
    // 배선이 null인 필드는 조용히 건너뛴다(CardVisualView 관례).
    public void Bind(CardData _card, bool _owned, int _slotNumber)
    {
        if (_owned && _card != null)
        {
            if (emptySlot != null) emptySlot.SetActive(false);
            // Bind가 스스로 SetActive(true)를 부른다 — 껍데기가 다시 켤 필요 없다.
            if (cardView  != null) cardView.Bind(_card, true);
            return;
        }

        if (cardView   != null) cardView.gameObject.SetActive(false);
        if (emptySlot  != null) emptySlot.SetActive(true);
        if (numberText != null) numberText.text = _slotNumber.ToString(numberFormat);
    }
}

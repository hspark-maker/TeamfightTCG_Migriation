using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 앨범 페이지의 카드 칸 하나(AlbumCardSlot 부착).
// 카드 표시는 CardVisualView에 위임한다 — 덱편집·컬렉션 칸과 같은 컴포넌트라 도감 카드만 다르게 보일 수 없다.
// 여기 남은 책임은 칸 버튼을 오버레이에 넘기는 것뿐(클릭 배선은 오버레이 몫).
public class AlbumCardSlotView : MonoBehaviour
{
    [SerializeField] Button button;
    [SerializeField] CardVisualView cardVisual;
    [Tooltip("빈 칸 표시. cardVisual 바깥에 두어야 카드가 통째로 꺼져도 남는다.")]
    [SerializeField] GameObject emptyMark;
    [Tooltip("빈 칸에 찍히는 도감 번호. 번호는 오버레이가 테마 내 통번호로 넘긴다.")]
    [SerializeField] TMP_Text numberLabel;

    public Button Button => button;

    // 미소유는 잠김 실루엣이 아니라 빈 칸으로 둔다 — 획득한 카드가 자리를 채우는 게 도감의 그림이다.
    // (CardVisualView의 잠김 오버레이 경로는 여기서 아예 타지 않는다)
    //
    // _number: 빈 칸에 찍을 도감 번호(1부터). 0 이하면 번호를 숨긴다.
    public void Bind(CardData _card, bool _owned, int _number)
    {
        bool t_show = _card != null && _owned;

        if (cardVisual != null) cardVisual.Bind(t_show ? _card : null, true);
        if (emptyMark != null) emptyMark.SetActive(!t_show);
        if (button != null) button.interactable = t_show;

        if (numberLabel != null)
        {
            numberLabel.gameObject.SetActive(!t_show && _number > 0);
            if (_number > 0) numberLabel.text = _number.ToString("000");
        }
    }
}

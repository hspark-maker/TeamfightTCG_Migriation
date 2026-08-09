using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 앨범 페이지의 카드 칸 하나(AlbumCardSlot 부착) — **실제 슬리브와 같은 두 겹 구조**다.
//
// 형제 순서: Sleeve_Back → NumberLabel → InsertDock → Card → Sleeve_Front.
//   · `Sleeve_Back`(불투명) = 포켓 바닥. 빈 칸에서 보이는 그림이고 버튼 타깃·레이캐스트도 여기가 받는다.
//   · `NumberLabel` = 바닥에 인쇄된 번호. 카드보다 뒤라 **카드가 들어오면 저절로 덮인다**(끄고 켜는 분기가 없다).
//   · `InsertDock` = 삽입 중 드래그 카드의 부모. 번호 위·앞면 아래라 번호를 덮으며 비닐 너머로 잠긴다.
//   · `Card` = 안착한 카드.
//   · `Sleeve_Front`(반투명 비닐) = 카드 위를 덮는 앞면.
//
// 앞면 알파는 "칸이 비었는가"로만 정해진다: 빈 칸 = `emptyFrontAlpha`, 꽂힌 칸 = 0(카드가 선명해야 한다).
// 삽입 연출은 그 사이를 잇는 트윈 하나(`SettleFront`)일 뿐이라 **연출용 별도 상태가 없다** —
// 어느 경로로 `Bind`가 불려도 칸의 톤이 스스로 옳은 값으로 돌아온다.
//
// ⚠ Sleeve_*·NumberLabel은 "보이는 그림" 앵커(0.0728~0.9272)를, Card·InsertDock은 칸 전체(0~1)를 쓴다.
//   GridRatioFitter가 카드 스프라이트의 투명 여백만큼 셀을 겹쳐 배치하기 때문이다 —
//   씰을 칸 전체에 그리면 그 투명분만큼 옆 칸과 겹쳐 보인다.
//
// 카드 표시는 CardVisualView에 위임한다 — 덱편집·컬렉션 칸과 같은 컴포넌트라 도감 카드만 다르게 보일 수 없다.
// 여기 남은 책임은 칸 버튼을 오버레이에 넘기는 것뿐(클릭 배선은 오버레이 몫).
public class AlbumCardSlotView : MonoBehaviour
{
    [SerializeField] Button button;
    [SerializeField] CardVisualView cardVisual;
    [Tooltip("씰 뒷면(포켓 바닥). 불투명이라 뒤로는 아무것도 비치지 않는다. 버튼 타깃·레이캐스트도 이 이미지가 받는다.")]
    [SerializeField] Image sleeveImage;
    [Tooltip("씰 앞면(비닐). 카드 위를 덮는다 — 빈 칸에서만 보이고 카드가 꽂히면 0이 된다.")]
    [SerializeField] Image sleeveFrontImage;
    [Range(0f, 1f)]
    [Tooltip("빈 칸일 때 앞면 알파. 올릴수록 '비닐 너머'가 또렷하지만 번호와 삽입 중인 카드가 그만큼 흐려진다.")]
    [SerializeField] float emptyFrontAlpha = 0.4f;
    [Tooltip("삽입 중 드래그 카드가 들어앉는 자리. 번호 위·앞면 아래 형제라 번호를 덮으며 비닐 뒤로 잠긴다.")]
    [SerializeField] RectTransform insertDock;
    [Tooltip("포켓 바닥에 찍히는 도감 번호. 카드보다 뒤라 카드가 꽂히면 가려진다. 번호는 오버레이가 테마 내 통번호로 넘긴다.")]
    [SerializeField] TMP_Text numberLabel;

    public Button Button => button;

    /// <summary>씰 뒷면. 칸의 그림이 하나의 프리팹에서 온다는 것을 보장하는 창구다.</summary>
    public Image SleeveImage => sleeveImage;

    /// <summary>삽입 중 드래그 카드를 넣을 부모. 여기 들어간 것만 번호를 덮고 앞면 뒤로 잠긴다.</summary>
    public RectTransform InsertDock => insertDock;

    /// <summary>앞면을 빈 칸 톤에서 0까지 걷는 트윈 — 카드가 비닐 너머에서 선명해지는 것이 "안착했다"의 신호다.
    /// 직전 `Bind`가 이미 0으로 놓았으므로 시작값을 다시 세우고 출발한다.</summary>
    public Tween SettleFront(float _duration)
    {
        if (sleeveFrontImage == null) return null;

        this.SetFrontAlpha(emptyFrontAlpha);

        return sleeveFrontImage.DOFade(0f, _duration)
                               .SetEase(Ease.OutQuad)
                               .SetLink(sleeveFrontImage.gameObject);
    }

    // 미소유는 잠김 실루엣이 아니라 빈 칸으로 둔다 — 획득한 카드가 자리를 채우는 게 도감의 그림이다.
    // (CardVisualView의 잠김 오버레이 경로는 여기서 아예 타지 않는다)
    //
    // _number: 칸에 찍을 도감 번호(1부터). 0 이하면 번호를 숨긴다.
    public void Bind(CardData _card, bool _owned, int _number)
    {
        bool t_show = _card != null && _owned;

        if (cardVisual != null) cardVisual.Bind(t_show ? _card : null, true);

        // 꽂힌 카드 위에 비닐을 남기지 않는다 — 칸의 톤은 "비었는가" 하나로만 정해진다.
        this.SetFrontAlpha(t_show ? 0f : emptyFrontAlpha);

        if (button != null) button.interactable = t_show;

        // 번호는 소유 여부로 끄지 않는다 — 꽂힌 카드가 위에서 덮는 것이 이 구조의 자연스러운 은닉이다.
        if (numberLabel != null)
        {
            numberLabel.gameObject.SetActive(_number > 0);
            if (_number > 0) numberLabel.text = _number.ToString("000");
        }
    }

    void SetFrontAlpha(float _a)
    {
        if (sleeveFrontImage == null) return;

        sleeveFrontImage.DOKill();

        var t_c = sleeveFrontImage.color;
        t_c.a = _a;
        sleeveFrontImage.color = t_c;
    }
}

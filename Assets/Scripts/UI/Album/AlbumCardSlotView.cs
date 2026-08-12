using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
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
// 앞면 알파는 칸 상태와 무관하게 늘 `frontAlpha`다 — 꽂힌 카드도 비닐 너머로 보이는 것이 슬리브의 그림이다.
// (2026-08-10 이전에는 꽂힌 칸의 앞면을 0으로 걷었고 삽입 연출이 그 사이를 트윈했다. 되돌리지 말 것.)
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
    [Tooltip("씰 앞면(비닐). 빈 칸이든 꽂힌 칸이든 늘 같은 톤으로 카드 위를 덮는다.")]
    [SerializeField] Image sleeveFrontImage;
    [Range(0f, 1f)]
    [Tooltip("앞면 알파. 올릴수록 '비닐 너머'가 또렷하지만 번호와 카드가 그만큼 흐려진다.")]
    [FormerlySerializedAs("emptyFrontAlpha")]
    [SerializeField] float frontAlpha = 0.4f;
    [Tooltip("삽입 중 드래그 카드가 들어앉는 자리. 번호 위·앞면 아래 형제라 번호를 덮으며 비닐 뒤로 잠긴다.")]
    [SerializeField] RectTransform insertDock;
    [Tooltip("포켓 바닥에 찍히는 도감 번호. 카드보다 뒤라 카드가 꽂히면 가려진다. 번호는 오버레이가 테마 내 통번호로 넘긴다.")]
    [SerializeField] TMP_Text numberLabel;

    // 안내 타깃으로 등록된 상태. 남의 등록을 날리지 않으려고 자기 것만 해제한다(AlbumThemeCellView와 같은 관용구)
    bool m_anchored;

    public Button Button => button;

    /// <summary>이 칸을 튜토리얼 안내 타깃으로 세우거나 내린다. 칸은 페이지를 넘길 때마다 다른 카드로 다시
    /// 묶이므로 프리팹 표식(TutorialAnchor)을 붙일 수 없다 — 지금 무엇이 꽂혀 있는지 아는 오버레이가 정한다.</summary>
    public void ApplyTutorialAnchor(bool _on)
    {
        if (_on == m_anchored) return;
        m_anchored = _on;

        var t_rect = button != null ? button.transform as RectTransform : null;
        if (t_rect == null) return;

        if (_on) TutorialAnchorRegistry.Register(EOutgameTutorialAnchor.AlbumCardSlot, t_rect, button);
        else     TutorialAnchorRegistry.Unregister(EOutgameTutorialAnchor.AlbumCardSlot, t_rect);
    }

    /// <summary>씰 뒷면. 칸의 그림이 하나의 프리팹에서 온다는 것을 보장하는 창구다.</summary>
    public Image SleeveImage => sleeveImage;

    /// <summary>삽입 중 드래그 카드를 넣을 부모. 여기 들어간 것만 번호를 덮고 앞면 뒤로 잠긴다.</summary>
    public RectTransform InsertDock => insertDock;

    // 미소유는 잠김 실루엣이 아니라 빈 칸으로 둔다 — 획득한 카드가 자리를 채우는 게 도감의 그림이다.
    // (CardVisualView의 잠김 오버레이 경로는 여기서 아예 타지 않는다)
    //
    // _number: 칸에 찍을 도감 번호(1부터). 0 이하면 번호를 숨긴다.
    public void Bind(CardData _card, bool _owned, int _number)
    {
        bool t_show = _card != null && _owned;

        if (cardVisual != null) cardVisual.Bind(t_show ? _card : null, true);

        this.ApplyFrontAlpha();

        if (button != null)
        {
            this.NeutralizeDisabledTint();
            button.interactable = t_show;
        }

        // 번호는 소유 여부로 끄지 않는다 — 꽂힌 카드가 위에서 덮는 것이 이 구조의 자연스러운 은닉이다.
        if (numberLabel != null)
        {
            numberLabel.gameObject.SetActive(_number > 0);
            if (_number > 0) numberLabel.text = _number.ToString("000");
        }
    }

    // 칸은 꺼지거나 다른 카드로 갈린다 — 죽은 칸을 가리키는 등록이 남지 않게 여기서 놓는다
    void OnDisable()
    {
        ApplyTutorialAnchor(false);
    }

    // 빈 칸의 밝기는 **씰 그림**이 정한다. 누를 수 있는지(interactable)가 정하게 두면,
    // 페이지를 넘겨 그 칸의 소유 상태가 뒤집힐 때마다 Button의 Disabled 틴트가 0.1초 페이드로 끼어들어
    // 칸 몇 개가 제멋대로 어두워졌다 돌아온다 — 화면에는 "칸이 깜빡인다"로 보인다.
    // 그래서 Disabled 색을 Normal과 같게 못 박는다(누름·강조 피드백은 그대로 산다).
    void NeutralizeDisabledTint()
    {
        var t_colors = button.colors;
        if (t_colors.disabledColor == t_colors.normalColor) return;

        t_colors.disabledColor = t_colors.normalColor;
        button.colors = t_colors;
    }

    // 프리팹 저작 알파가 무엇이든 인스펙터 값으로 덮는다 — 칸마다 비닐 톤이 갈리지 않게.
    void ApplyFrontAlpha()
    {
        if (sleeveFrontImage == null) return;

        var t_c = sleeveFrontImage.color;
        t_c.a = frontAlpha;
        sleeveFrontImage.color = t_c;
    }
}

using System;
using DG.Tweening;
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
    [SerializeField] GameObject     highlightFrame;   // 드래그 오버 테두리
    [SerializeField] GameObject     swapGlow;         // 슬롯 픽(교체) 모드의 어두운 뒤판. cardVisual 바깥에 둬야 빈 칸에서도 남는다.
    [SerializeField] CanvasGroup    canvasGroup;      // 시너지 강조 때 대상 아닌 칸을 죽이는 용도

    // 시너지 강조 중 "해당 없음" 칸의 알파. 0으로 두면 빈 칸인지 흐린 칸인지 구분이 안 된다.
    const float FOCUS_DIM_ALPHA = 0.25f;

    // 강조 대상 칸을 살짝 띄우는 확대. 컬렉션 타일(DeckEditCardTile)과 같은 값을 쓴다 —
    // 위아래가 다른 배율로 커지면 같은 롱프레스에 두 가지 연출이 도는 것처럼 보인다.
    const float FOCUS_SCALE      = 1.08f;
    const float FOCUS_SCALE_TIME = 0.12f;

    // 슬롯 픽(교체) 모드는 반대로 카드를 눌러 둘레에 여백을 만든다 — 그 여백에 어두운 뒤판이 드러나
    // 카드가 밝은 크림색 덱 배경에서 떠오른다. 눈에 걸리는 것은 발광이 아니라 이 명도 대비다.
    // 확대와 축소가 서로 다른 트랜스폼에 걸린다(강조는 칸 루트, 이쪽은 카드 비주얼 루트).
    const float SWAP_SCALE      = 0.80f;
    const float SWAP_SCALE_TIME = 0.14f;

    // 6칸이 같은 박으로 숨쉬면 화면이 통째로 뛴다 → 칸 순서대로 발광 위상을 흩뿌린다.
    const float SWAP_GLOW_PHASE_STEP = 0.17f;

    // 카드가 새로 앉는 순간의 한 박. 연속 교체를 막지 않도록 기본값(UiPunch)보다 짧고 얕게 친다.
    const float EQUIP_PUNCH_SCALE = 0.22f;
    const float EQUIP_PUNCH_TIME  = 0.22f;

    int         m_index = -1;
    CardData    m_card;
    Action<int> m_onClick;
    Tween       m_focusTween;
    Tween       m_swapTween;
    Tween       m_punchTween;
    UiGlowBlink m_swapGlowBlink;
    bool        m_swapGlowResolved;

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

        // 편성 칸에는 소유한 카드만 올라간다(컬렉션 목록 자체가 소유분만 노출) → _owned는 true 고정.
        // 여기서 소유여부를 다시 계산하면 편성 배열과 소유 세이브라는 두 진실원이 생긴다.
        // _card가 null이면 cardVisual이 자기 오브젝트를 꺼서 빈 칸이 된다.
        if (cardVisual != null) cardVisual.Bind(_card, true);

        if (emptyMark != null) emptyMark.SetActive(!t_has);

        SetHighlight(false);          // 재바인딩은 드래그 종료 직후에도 일어난다 → 하이라이트 잔상 제거
        if (canvasGroup != null) canvasGroup.alpha = 1f;
        ApplyFocusScale(false, true); // 강조 도중 재바인딩돼도 확대가 남지 않게 트윈 없이 즉시 되돌린다
        SetSwapTarget(false, true);
    }

    public void SetHighlight(bool _on)
    {
        if (highlightFrame != null) highlightFrame.SetActive(_on);
    }

    // 칸을 흐리게 눌러 대비를 만든다(_match면 반대로 살짝 키워 띄운다).
    // 시너지 강조 전용 축이다 — 슬롯 픽(교체) 모드는 SetSwapTarget으로 갈라져 나갔다.
    // 테두리(highlightFrame)는 쓰지 않는다 — 그건 드래그 오버 전용 신호로 남긴다.
    public void SetFocus(bool _focusing, bool _match)
    {
        if (canvasGroup != null) canvasGroup.alpha = _focusing && !_match ? FOCUS_DIM_ALPHA : 1f;

        ApplyFocusScale(_focusing && _match, false);
    }

    /// <summary>슬롯 픽(교체) 모드 표시. 카드가 살짝 줄고 그 둘레 여백에 발광이 켜진다.
    /// 발광 호흡은 칸마다 m_index로 어긋낸다. _instant면 트윈 없이 즉시 맞춘다.</summary>
    public void SetSwapTarget(bool _on, bool _instant = false)
    {
        // 켜지는 김에 시너지 딤을 씻는다 — 해제 콜백이 유실돼도 흐린 칸이 남지 않게 하는 안전망이다.
        if (_on && canvasGroup != null) canvasGroup.alpha = 1f;

        if (swapGlow != null)
        {
            if (_on) ApplyGlowPhase();

            // 발광 컴포넌트를 끄면 같은 UIEffect를 나눠 쓰는 표현까지 죽는다(UiGlowBlink 계약) → 오브젝트를 끈다.
            swapGlow.SetActive(_on);
        }

        ApplySwapScale(_on, _instant);
    }

    /// <summary>카드가 이 칸에 새로 앉은 순간을 한 번 튀긴다(교체 확정 신호).
    /// 칸 루트가 아니라 카드 비주얼에 건다 — 루트 스케일은 시너지 강조 트윈이 쥐고 있다.</summary>
    public void PlayEquipPunch()
    {
        if (cardVisual == null || m_card == null) return;

        m_punchTween?.Kill();
        m_punchTween = UiPunch.Play(cardVisual.transform, EQUIP_PUNCH_SCALE, EQUIP_PUNCH_TIME);
    }

    void ApplyGlowPhase()
    {
        // 못 찾았으면 다음 기회에 다시 찾는다(UiGlowBlink.Resolve와 같은 규약) — 실패를 굳히면 그 칸만 영영 안 빛난다.
        if (!m_swapGlowResolved)
        {
            m_swapGlowBlink    = swapGlow.GetComponentInChildren<UiGlowBlink>(true);
            m_swapGlowResolved = m_swapGlowBlink != null;
        }

        if (m_swapGlowBlink != null) m_swapGlowBlink.SetPhase(m_index * SWAP_GLOW_PHASE_STEP);
    }

    void ApplySwapScale(bool _down, bool _instant)
    {
        if (cardVisual == null) return;

        var   t_tr     = cardVisual.transform;
        float t_target = _down ? SWAP_SCALE : 1f;

        m_swapTween?.Kill();
        m_swapTween = null;

        // 교체 펀치도 같은 트랜스폼의 스케일을 쥔다 — 살려두면 펀치가 끝나며 자기 시작 배율로 되돌려 축소를 지운다.
        m_punchTween?.Kill();
        m_punchTween = null;

        if (_instant || !Application.isPlaying || Mathf.Approximately(t_tr.localScale.x, t_target))
        {
            t_tr.localScale = Vector3.one * t_target;
            return;
        }

        m_swapTween = t_tr.DOScale(t_target, SWAP_SCALE_TIME)
                          .SetEase(_down ? Ease.OutBack : Ease.OutQuad)
                          .SetLink(gameObject);
    }

    // _instant면 트윈 없이 즉시 맞춘다(재바인딩·초기화 경로).
    void ApplyFocusScale(bool _up, bool _instant)
    {
        float t_target = _up ? FOCUS_SCALE : 1f;

        // 반대 방향 트윈이 살아 있으면 마지막에 끝난 쪽이 이겨 크기가 뒤집힌 채 굳는다 → 항상 먼저 죽인다.
        m_focusTween?.Kill();
        m_focusTween = null;

        if (_instant || !Application.isPlaying || Mathf.Approximately(transform.localScale.x, t_target))
        {
            transform.localScale = Vector3.one * t_target;
            return;
        }

        m_focusTween = transform.DOScale(t_target, FOCUS_SCALE_TIME)
                                .SetEase(_up ? Ease.OutBack : Ease.OutQuad)
                                .SetLink(gameObject);
    }

    void OnDestroy()
    {
        m_focusTween?.Kill();
        m_focusTween = null;
        m_swapTween?.Kill();
        m_swapTween = null;
        m_punchTween?.Kill();
        m_punchTween = null;
        transform.DOKill();
        if (cardVisual != null) cardVisual.transform.DOKill();
    }
}

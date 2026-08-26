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
    [SerializeField] CanvasGroup    canvasGroup;      // 시너지 강조 때 대상 아닌 칸을 죽이는 용도

    // 시너지 강조 중 "해당 없음" 칸의 알파. 0으로 두면 빈 칸인지 흐린 칸인지 구분이 안 된다.
    const float FOCUS_DIM_ALPHA = 0.25f;

    // 강조 대상 칸을 살짝 띄우는 확대. 컬렉션 타일(DeckEditCardTile)과 같은 값을 쓴다 —
    // 위아래가 다른 배율로 커지면 같은 롱프레스에 두 가지 연출이 도는 것처럼 보인다.
    const float FOCUS_SCALE      = 1.08f;
    const float FOCUS_SCALE_TIME = 0.12f;

    int         m_index = -1;
    int         m_card;
    Action<int> m_onClick;
    Tween       m_focusTween;

    public int           Index => m_index;
    public int           Card  => m_card;
    public RectTransform Rect  => (RectTransform)transform;

    // _card가 null이면 빈 칸 모드. 편집 중 매 변경마다 전량 재바인딩되는 전제라 상태를 남기지 않는다.
    public void Bind(int _index, int _card, Action<int> _onClick)
    {
        m_index   = _index;
        m_card    = _card;
        m_onClick = _onClick;

        if (clickButton != null)
        {
            clickButton.onClick.RemoveAllListeners();   // 재바인딩마다 호출되므로 중복 등록 방지 필수
            clickButton.onClick.AddListener(() => m_onClick?.Invoke(m_index));
        }

        bool t_has = _card > 0;

        // 편성 칸에는 소유한 카드만 올라간다(컬렉션 목록 자체가 소유분만 노출) → _owned는 true 고정.
        // 여기서 소유여부를 다시 계산하면 편성 배열과 소유 세이브라는 두 진실원이 생긴다.
        // _card가 null이면 cardVisual이 자기 오브젝트를 꺼서 빈 칸이 된다.
        if (cardVisual != null) cardVisual.Bind(_card, true);

        if (emptyMark != null) emptyMark.SetActive(!t_has);

        SetHighlight(false);          // 재바인딩은 드래그 종료 직후에도 일어난다 → 하이라이트 잔상 제거
        if (canvasGroup != null) canvasGroup.alpha = 1f;
        ApplyFocusScale(false, true); // 강조 도중 재바인딩돼도 확대가 남지 않게 트윈 없이 즉시 되돌린다
    }

    public void SetHighlight(bool _on)
    {
        if (highlightFrame != null) highlightFrame.SetActive(_on);
    }

    // 시너지 아이콘 롱프레스 중 호출. _match면 살짝 키워 띄우고, 아니면 흐리게 눌러 대비를 만든다.
    // 테두리(highlightFrame)는 쓰지 않는다 — 그건 드래그 오버 전용 신호로 남긴다.
    public void SetSynergyFocus(bool _focusing, bool _match)
    {
        if (canvasGroup != null) canvasGroup.alpha = _focusing && !_match ? FOCUS_DIM_ALPHA : 1f;

        ApplyFocusScale(_focusing && _match, false);
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
        transform.DOKill();
    }
}

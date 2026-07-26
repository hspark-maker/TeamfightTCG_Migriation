using System;
using DG.Tweening;
using TMPro;
using UnityEngine;

// 카드팩 개봉 시 등장하는 카드 1장의 표시 + 인터랙션 뷰(RevealCard 프리팹에 부착).
// CardView.prefab(전투 카드) 복사본에서 카드 표현 요소만 남긴 프리팹 기준 — uGUI가 아니라
// 아트=SpriteRenderer(Illustration), 이름/체력=TextMeshPro(월드, MeshRenderer 기반).
// 드래그 입력도 월드 방식: 프리팹에 BoxCollider2D가 있어 OnMouse* 콜백이 마우스/터치로 매핑된다.
// 순수 뷰 — 세이브·소유 갱신 없음. PackTearOpenView가 이미 획득된 DrawnCard를 넘겨주면 표시만 하고,
// 스택 겹침(SetSortingOrder)·드래그 넘김(OnMouse*)·페이드/이동을 자기 완결적으로 처리한다.
// (바인딩값 정본: fullImage/displayName/maxHp)
public class RevealCardView : MonoBehaviour
{
    [Header("바인딩 (CardView.prefab 요소)")]
    [SerializeField] SpriteRenderer illustration; // 카드 아트(CardData.fullImage) — Illustration(SpriteRenderer)
    [SerializeField] TMP_Text nameText;           // 카드 이름(CardData.displayName) — NameText(TextMeshPro)
    [SerializeField] TMP_Text hpText;             // 체력(CardData.maxHp) — HPText(TextMeshPro), 옵션

    [Header("신규/중복 표식")]
    [SerializeField] GameObject newBadge;   // 신규 획득 시 활성
    [SerializeField] GameObject dupBadge;   // 중복 시 활성
    [SerializeField] TMP_Text refundText;   // (옵션) 중복 환급 Gold 표시

    [Header("드래그 넘김")]
    [Tooltip("드래그 시작점 대비 이동거리(월드 유닛)가 이 값 이상이면 넘김 확정. 미만이면 원위치 복귀.")]
    [SerializeField] float dragThreshold = 1.5f;
    [Tooltip("넘김 확정 시 페이드아웃 시간(초).")]
    [SerializeField] float swipeFadeDuration = 0.25f;
    [Tooltip("threshold 미달로 놓았을 때 원위치로 되돌아가는 시간(초).")]
    [SerializeField] float returnDuration = 0.25f;

    // 페이드/정렬 대상 캐시(Awake 1회). 자식 전체 SpriteRenderer/TMP/Renderer를 훑는다(CardAnimator 관용구).
    SpriteRenderer[] m_sprites;
    TMP_Text[] m_texts;
    Renderer[] m_renderers;
    int[] m_baseOrders;     // 카드 내부의 원래 sortingOrder(아트↔텍스트 앞뒤 관계 보존용)

    // 드래그 상태.
    bool m_draggable;                 // 맨 위 카드만 true(PackTearOpenView가 SetDraggable로 지정)
    bool m_dragging;                  // OnMouseDown~Up 사이
    float m_dragDepth;                // ScreenToWorldPoint용 깊이(카드 평면까지 스크린 z), OnMouseDown에서 고정
    Vector3 m_pointerStartWorld;      // 드래그 시작 시 포인터 월드 좌표
    Vector3 m_dragStartLocalPos;      // 드래그 시작 시 카드 로컬 좌표(복귀 목표)
    Vector3 m_homeLocalPos;           // 스택/그리드 슬롯 좌표(MoveTo가 갱신). 드래그 기준·복귀 목표.
    Action<RevealCardView> m_onSwiped;// 넘김 확정 콜백(PackTearOpenView가 다음 top 활성)

    void Awake()
    {
        CacheVisuals();
    }

    // 자식 렌더러/텍스트 캐시. 이미 캐시됐으면 no-op(런타임 재획득 방지).
    void CacheVisuals()
    {
        if (m_renderers != null) return;

        m_sprites = GetComponentsInChildren<SpriteRenderer>(true);
        m_texts = GetComponentsInChildren<TMP_Text>(true);
        m_renderers = GetComponentsInChildren<Renderer>(true);
        m_baseOrders = new int[m_renderers.Length];
        for (int t_i = 0; t_i < m_renderers.Length; t_i++)
            m_baseOrders[t_i] = m_renderers[t_i] != null ? m_renderers[t_i].sortingOrder : 0;
    }

    // 카드 1장 공개(데이터 바인딩만). _isNew=신규 여부, _refund=중복 시 환급된 Gold(신규면 0).
    // 재화·소유는 이미 TryPurchase가 영속화했으므로 여기선 표시만 한다(이중 처리 금지).
    // 스택 배치·드래그 활성·정렬은 PackTearOpenView가 이어서 지정한다.
    public void Reveal(CardData _card, bool _isNew, long _refund)
    {
        // 아트 바인딩(스프라이트 없으면 렌더러 비활성으로 빈칸 방지).
        if (illustration != null)
        {
            illustration.sprite = _card != null ? _card.fullImage : null;
            illustration.enabled = illustration.sprite != null;
        }

        // 이름은 표시명 정본(_card.name=에셋 파일명 사용 금지).
        if (nameText != null)
            nameText.text = _card != null ? _card.displayName : string.Empty;

        // 체력 스탯(카드 기본 maxHp). 뷰에 hpText 미배선 시 생략.
        if (hpText != null)
            hpText.text = _card != null ? _card.maxHp.ToString() : string.Empty;

        // 신규/중복 뱃지는 단순 토글(둘 중 하나만).
        if (newBadge != null) newBadge.SetActive(_isNew);
        if (dupBadge != null) dupBadge.SetActive(!_isNew);

        // 중복 환급 텍스트(옵션): 신규거나 환급 0이면 숨긴다.
        if (refundText != null)
        {
            bool t_showRefund = !_isNew && _refund > 0;
            refundText.gameObject.SetActive(t_showRefund);
            if (t_showRefund) refundText.text = "+" + _refund.ToString("N0");
        }

        // 초기 상태: 완전 표시·정위치·드래그 비활성(스택 top만 이후 SetDraggable(true)).
        m_draggable = false;
        m_dragging = false;
        transform.DOKill();
        transform.localScale = Vector3.one;
        SetAlphaImmediate(1f);
    }

    // ── PackTearOpenView가 지시하는 인터랙션 API ────────────────────────────

    // 맨 위 카드만 true. false면 OnMouse 입력을 무시하고 진행 중 드래그도 취소(홈으로 즉시 리셋 — 잔상 방지).
    public void SetDraggable(bool _on)
    {
        m_draggable = _on;
        if (!_on && m_dragging)
        {
            m_dragging = false;
            transform.DOKill();
            transform.localPosition = m_homeLocalPos;
        }
    }

    // 넘김 확정 시 호출될 콜백 등록.
    public void SetSwipeCallback(Action<RevealCardView> _cb) => m_onSwiped = _cb;

    // 스택 겹침 순서. _stackOrder가 클수록 앞(위). 카드 내부 앞뒤 관계는 baseOrder로 보존한다.
    // 스택 밴드(SORTING_BAND) 단위로 띄워 카드끼리 렌더 순서가 섞이지 않게 한다.
    public void SetSortingOrder(int _stackOrder)
    {
        CacheVisuals();
        for (int t_i = 0; t_i < m_renderers.Length; t_i++)
            if (m_renderers[t_i] != null)
                m_renderers[t_i].sortingOrder = _stackOrder * SORTING_BAND + m_baseOrders[t_i];
    }

    const int SORTING_BAND = 16; // 한 카드가 차지하는 sortingOrder 밴드 폭(내부 요소 수보다 크게)

    // 페이드아웃/인(대상: 자식 SpriteRenderer=DOFade, TMP=DOFade). 이동과 독립(다른 트윈 타깃).
    public void FadeOut(float _dur) => FadeTo(0f, _dur);
    public void FadeIn(float _dur) => FadeTo(1f, _dur);

    // 로컬 이동. _dur<=0이면 즉시 세팅(스택 초기 배치용). 목표를 홈 슬롯으로 기억(드래그 기준·복귀 목표).
    public void MoveTo(Vector3 _localPos, float _dur)
    {
        m_homeLocalPos = _localPos;
        transform.DOKill();
        if (_dur <= 0f) { transform.localPosition = _localPos; return; }
        transform.DOLocalMove(_localPos, _dur).SetEase(Ease.OutBack);
    }

    // 좀비 트윈 방지용 전체 정리(파괴/비활성 시). 트윈 타깃이 transform이 아니라 렌더러라 각각 DOKill.
    public void KillAllTweens()
    {
        transform.DOKill();
        CacheVisuals();
        for (int t_i = 0; t_i < m_sprites.Length; t_i++)
            if (m_sprites[t_i] != null) m_sprites[t_i].DOKill();
        for (int t_i = 0; t_i < m_texts.Length; t_i++)
            if (m_texts[t_i] != null) m_texts[t_i].DOKill();
    }

    // ── 내부: 알파 처리 ───────────────────────────────────────────────────

    void FadeTo(float _a, float _dur)
    {
        CacheVisuals();
        for (int t_i = 0; t_i < m_sprites.Length; t_i++)
        {
            var t_sr = m_sprites[t_i];
            if (t_sr == null) continue;
            t_sr.DOKill();
            if (_dur <= 0f) { var t_c = t_sr.color; t_c.a = _a; t_sr.color = t_c; }
            else t_sr.DOFade(_a, _dur);
        }
        for (int t_i = 0; t_i < m_texts.Length; t_i++)
        {
            var t_tmp = m_texts[t_i];
            if (t_tmp == null) continue;
            t_tmp.DOKill();
            if (_dur <= 0f) t_tmp.alpha = _a;
            else t_tmp.DOFade(_a, _dur);
        }
    }

    void SetAlphaImmediate(float _a) => FadeTo(_a, 0f);

    // ── 월드 드래그 입력(BoxCollider2D 필요) ──────────────────────────────

    void OnMouseDown()
    {
        if (!m_draggable) return;

        var t_cam = Camera.main;
        // 카드 평면까지의 스크린 깊이를 고정(원근/직교 무관하게 XY 정확).
        m_dragDepth = t_cam != null ? t_cam.WorldToScreenPoint(transform.position).z : 0f;
        m_dragging = true;
        transform.DOKill(); // 복귀 트윈 중 재드래그 시 충돌 방지
        // 시작점은 (복귀 중 중간 좌표가 아니라) 홈 슬롯 기준 — 반복 드래그 드리프트 방지.
        m_dragStartLocalPos = m_homeLocalPos;
        transform.localPosition = m_homeLocalPos;
        m_pointerStartWorld = GetPointerWorld();
    }

    void OnMouseDrag()
    {
        if (!m_draggable || !m_dragging) return;

        Vector3 t_worldDelta = GetPointerWorld() - m_pointerStartWorld;
        t_worldDelta.z = 0f;
        // 컨테이너가 스케일/회전될 수 있으므로 월드 델타를 로컬로 변환해 따라오게 한다.
        Vector3 t_localDelta = transform.parent != null
            ? transform.parent.InverseTransformVector(t_worldDelta)
            : t_worldDelta;
        transform.localPosition = m_dragStartLocalPos + t_localDelta;
    }

    void OnMouseUp()
    {
        if (!m_draggable || !m_dragging) return;
        m_dragging = false;

        Vector3 t_worldDelta = GetPointerWorld() - m_pointerStartWorld;
        t_worldDelta.z = 0f;

        if (t_worldDelta.magnitude >= dragThreshold)
        {
            // 넘김 확정: 더 이상 입력 안 받고 페이드아웃 후 콜백(그리드용으로 파괴하지 않음).
            m_draggable = false;
            FadeOut(swipeFadeDuration);
            m_onSwiped?.Invoke(this);
        }
        else
        {
            // 미달: 원위치 복귀.
            transform.DOKill();
            transform.DOLocalMove(m_dragStartLocalPos, returnDuration).SetEase(Ease.OutBack);
        }
    }

    // 포인터 스크린 좌표 → 카드 평면 월드 좌표(마우스/터치 공통).
    Vector3 GetPointerWorld()
    {
        var t_cam = Camera.main;
        if (t_cam == null) return transform.position;
        Vector3 t_sp = Input.mousePosition;
        t_sp.z = m_dragDepth;
        return t_cam.ScreenToWorldPoint(t_sp);
    }

    // 트윈 진행 중 파괴 시 좀비 트윈 방지 + 드래그 상태 정리.
    void OnDestroy()
    {
        m_dragging = false;
        KillAllTweens();
    }
}

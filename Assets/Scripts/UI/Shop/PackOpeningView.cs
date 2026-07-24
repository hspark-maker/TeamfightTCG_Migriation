using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 카드팩 개봉 루프의 씬 상주 뷰(F-19). 단일 고정 packId 버튼 클릭 → CardPackOpener.TryPurchase →
// 결과 OpenedPack를 "스택 → 드래그 넘김 → 2×3 그리드" 인터랙션으로 오케스트레이션한다.
// 순수 뷰: 차감·드로우·소유부여·Save는 TryPurchase가 원자적으로 끝냈으므로 여기선 연출만(이중 처리 금지).
// 상점 목록·독립 씬·MainMenu 진입은 스코프 밖(단일 팩 버튼 하나).
//
// 상태머신(STRUCTURE.md F-19 stateDiagram 기준):
//   Idle → (팩 클릭·성공) → Stacking → (스택 소진) → Grid → Idle
//   Stacking 중엔 재클릭 무시(재진입 차단). 실패는 상태 전이 없이 버튼 흔들림만.
public class PackOpeningView : MonoBehaviour
{
    [Header("팩")]
    [Tooltip("이 뷰가 여는 고정 packId(CardShop의 CardPackData.PackId와 일치해야 함).")]
    [SerializeField] string packId;
    [SerializeField] Button packButton;             // 카드팩 구매 버튼

    [Header("카드 스택")]
    [SerializeField] Transform cardsContainer;      // 스택/그리드 카드가 놓일 컨테이너
    [SerializeField] RevealCardView cardPrefab;     // 카드 1장 뷰 프리팹(BoxCollider2D 필요)
    [Tooltip("스택에서 카드 한 장당 뒤로 밀리는 로컬 오프셋(겹침 표현). 뒤 카드가 이 방향으로 peek.")]
    [SerializeField] Vector3 stackOffset = new Vector3(0.1f, -0.1f, 0f);
    [Tooltip("top이 넘어간 뒤 남은 카드가 앞 슬롯으로 당겨지는 시간(초).")]
    [SerializeField] float stackAdvanceDuration = 0.2f;

    [Header("그리드")]
    [Tooltip("그리드 셀 간격(가로/세로, 로컬 유닛). 열은 3열 고정, 행은 카드 수로 유동.")]
    [SerializeField] Vector2 gridCellSize = new Vector2(2.2f, 3.0f);
    [Tooltip("그리드 자리로 이동하는 시간(초).")]
    [SerializeField] float gridMoveDuration = 0.4f;
    [Tooltip("그리드에서 페이드인하는 시간(초).")]
    [SerializeField] float gridFadeDuration = 0.3f;

    [Header("실패 피드백")]
    [Tooltip("실패(잔액 부족 등) 시 팩 버튼 흔들림 시간(초).")]
    [SerializeField] float failShakeDuration = 0.4f;

    [Header("독립 실행 부트스트랩 (테스트 씬 전용)")]
    [Tooltip("CardCatalog 미주입 독립 씬에서만 사용. 통합 시 IsReady 가드로 no-op(4번째 마스터목록 아님).")]
    [SerializeField] CardShop fallbackShop;
    [SerializeField] List<CardData> fallbackAllCards = new List<CardData>();

    // 그리드 열 수(고정 3열, 행은 ceil(count/3)로 유동).
    const int GRID_COLUMNS = 3;

    enum EViewState { Idle, Stacking, Grid }

    // 개봉/드래그 진행 중 재진입 차단용 상태(Stacking이면 재클릭 무시).
    EViewState m_state = EViewState.Idle;

    // 이번 개봉으로 생성된 카드 뷰들(드로우 순서 = 인덱스 순서). 넘겨도 파괴하지 않고 그리드에서 재사용.
    readonly List<RevealCardView> m_cards = new List<RevealCardView>();

    // 현재 스택 top(드래그 가능) 카드의 인덱스. 넘길 때마다 +1, count 도달 시 그리드로.
    int m_topIndex;

    void Start()
    {
        EnsureBoot();
        if (packButton != null) packButton.onClick.AddListener(OnPackClicked);
    }

    // 독립 실행 시 카탈로그/소유/재화/상점 부트를 보장. 이미 준비됐으면(통합) 아무것도 하지 않는다.
    // 도감 CollectionGalleryController.EnsureBoot 선례를 따른다.
    void EnsureBoot()
    {
        if (CardCatalog.IsReady) return;

        DataSaveManager.Load();                 // 저장된 진행도·재화 로드
        CardCatalog.SetSource(fallbackAllCards);
        OwnershipManager.Init();
        CurrencyManager.Init();                 // 저장된 골드로 잔액 맞춤
        CardPackOpener.SetShop(fallbackShop);   // 상점 SO 주입(null이면 빈 상점 fallback)
    }

    // 팩 버튼 클릭 → 구매 시도 → 결과 분기.
    void OnPackClicked()
    {
        // Idle이 아니면(스택/드래그 진행 중) 무시 — 재진입 차단(이중 구매 연출 방지).
        if (m_state != EViewState.Idle) return;

        var t_opened = CardPackOpener.TryPurchase(packId);

        if (t_opened != null && t_opened.Success)
        {
            BuildStack(t_opened);
        }
        else
        {
            // 실패(InsufficientGold/EmptyPool/PackNotFound/SpendFailed): 예외 던지지 않고 가벼운 피드백.
            var t_result = t_opened != null ? t_opened.Result : EPackOpenResult.PackNotFound;
            Debug.LogWarning($"[PackOpeningView] 개봉 실패 packId={packId}, result={t_result}");
            PlayFailFeedback();
        }
    }

    // 성공 경로: 이전 카드 정리 → 뽑힌 카드 N장을 덱처럼 겹쳐 스택 배치 → 맨 위만 드래그 활성.
    void BuildStack(OpenedPack _opened)
    {
        ClearSpawned();

        var t_cards = _opened.Cards;
        for (int t_i = 0; t_i < t_cards.Count; t_i++)
        {
            if (cardPrefab == null || cardsContainer == null) break;

            var t_view = Instantiate(cardPrefab, cardsContainer);
            var t_drawn = t_cards[t_i];
            t_view.Reveal(t_drawn.Card, t_drawn.IsNew, t_drawn.Refund);
            t_view.SetSwipeCallback(OnCardSwiped);
            m_cards.Add(t_view);
        }

        m_topIndex = 0;
        if (m_cards.Count == 0) { m_state = EViewState.Idle; return; }

        m_state = EViewState.Stacking;
        PlaceStack(false);   // 초기 배치는 즉시
        RefreshDraggable();  // 맨 위만 드래그 가능
    }

    // 남은 카드를 스택 슬롯에 배치. rank 0(=top)이 앞·정중앙, 뒤 카드는 stackOffset*rank 만큼 peek.
    void PlaceStack(bool _animate)
    {
        int t_count = m_cards.Count;
        for (int t_i = m_topIndex; t_i < t_count; t_i++)
        {
            var t_card = m_cards[t_i];
            if (t_card == null) continue;

            int t_rank = t_i - m_topIndex;
            // 앞(rank 0)일수록 sortingOrder 큼 → 화면 앞. 뒤 카드는 오프셋만큼 밀림.
            t_card.SetSortingOrder(t_count - t_rank);
            t_card.MoveTo(stackOffset * t_rank, _animate ? stackAdvanceDuration : 0f);
        }
    }

    // Stacking 상태에서 top 카드 1장만 드래그 가능하게 갱신.
    void RefreshDraggable()
    {
        for (int t_i = 0; t_i < m_cards.Count; t_i++)
        {
            if (m_cards[t_i] == null) continue;
            m_cards[t_i].SetDraggable(m_state == EViewState.Stacking && t_i == m_topIndex);
        }
    }

    // 카드 넘김 콜백(RevealCardView가 이미 자기 페이드아웃 시작). top 갱신 → 다음 카드 활성, 소진 시 그리드.
    void OnCardSwiped(RevealCardView _card)
    {
        if (m_state != EViewState.Stacking) return;
        // 현재 top이 넘긴 경우만 인정(오작동 방어).
        if (m_topIndex >= m_cards.Count || m_cards[m_topIndex] != _card) return;

        m_topIndex++;

        if (m_topIndex >= m_cards.Count)
        {
            LayoutGrid();   // 스택 소진 → 그리드
            return;
        }

        PlaceStack(true);   // 남은 카드 한 칸 당김
        RefreshDraggable(); // 새 top 활성
    }

    // 넘긴 전체 카드를 3열 그리드로 재배치·페이드인. 행 = ceil(count/3).
    void LayoutGrid()
    {
        m_state = EViewState.Grid;

        int t_n = m_cards.Count;
        int t_rows = Mathf.CeilToInt(t_n / (float)GRID_COLUMNS);

        for (int t_i = 0; t_i < t_n; t_i++)
        {
            var t_card = m_cards[t_i];
            if (t_card == null) continue;

            t_card.SetDraggable(false);

            int t_r = t_i / GRID_COLUMNS;
            int t_c = t_i % GRID_COLUMNS;
            // 3열 폭 기준으로 중앙 정렬(마지막 행이 덜 차도 좌측 정렬로 채움), 첫 행이 위.
            float t_x = (t_c - (GRID_COLUMNS - 1) * 0.5f) * gridCellSize.x;
            float t_y = ((t_rows - 1) * 0.5f - t_r) * gridCellSize.y;

            t_card.SetSortingOrder(0);                          // 그리드는 겹침 없음 → 평탄
            t_card.MoveTo(new Vector3(t_x, t_y, 0f), gridMoveDuration);
            t_card.FadeIn(gridFadeDuration);                    // 넘기며 사라졌던 카드 되살림
        }

        // 그리드 연출이 끝날 때까지 Grid 유지(연출 중 재클릭 잠금) → 완료 후 Idle 복귀.
        StartCoroutine(CoGridToIdle());
    }

    // 그리드 이동·페이드 연출 시간만큼 대기 후 Idle 복귀(다음 개봉 허용).
    IEnumerator CoGridToIdle()
    {
        float t_wait = Mathf.Max(gridMoveDuration, gridFadeDuration);
        if (t_wait > 0f) yield return new WaitForSeconds(t_wait);
        if (m_state == EViewState.Grid) m_state = EViewState.Idle;
    }

    // 실패 피드백: 팩 버튼 짧은 흔들림.
    void PlayFailFeedback()
    {
        if (packButton == null) return;
        packButton.transform.DOKill();
        packButton.transform.DOShakePosition(failShakeDuration, new Vector3(12f, 0f, 0f));
    }

    // 이전 개봉 카드 정리(파괴 + 트윈 정리).
    void ClearSpawned()
    {
        for (int t_i = 0; t_i < m_cards.Count; t_i++)
            if (m_cards[t_i] != null) Destroy(m_cards[t_i].gameObject);
        m_cards.Clear();
        m_topIndex = 0;
    }

    // 개봉/드래그 중 오브젝트 비활성 시 좀비 트윈 정리 + 상태 리셋.
    // (카드는 파괴하지 않고 트윈만 죽인다 — 재활성/다음 개봉 시 ClearSpawned가 정리.)
    void OnDisable()
    {
        StopAllCoroutines();

        for (int t_i = 0; t_i < m_cards.Count; t_i++)
        {
            if (m_cards[t_i] == null) continue;
            m_cards[t_i].SetDraggable(false);
            m_cards[t_i].KillAllTweens();
        }

        if (packButton != null)
        {
            packButton.transform.DOKill();
            packButton.interactable = true;
        }

        m_state = EViewState.Idle;
        m_topIndex = 0;
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

// 3D 팩 뜯기 개봉 뷰(도메인 G 재구성). 진입은 컨트롤러가 넘겨주는 OpenedPack(BeginOpen)뿐 —
// 구매·packButton·부트는 이 뷰 밖(상점/부트/컨트롤러)의 책임이다(구 PackOpeningView의 구매부 제거).
// 흐름: 3D 팩 등장 → 가로 드래그로 봉인 뜯기(PackTearHandle) → 카드 스택 → 드래그로 넘김 →
//   소진되면 2×3 그리드로 전개 → OnOpenComplete 발화(컨트롤러가 획득 버튼 노출).
// 스택/그리드/OnOpenComplete 로직은 구 PackOpeningView에서 그대로 이식했다(검증된 안전 패턴 유지).
public class PackTearOpenView : MonoBehaviour
{
    // 개봉 완료(그리드 배치 종료) 시 1회 발화. 컨트롤러가 구독해 획득 버튼을 노출한다.
    public event Action OnOpenComplete;

    [Header("3D 팩")]
    [Tooltip("씬에 배치된 3D 팩 모델(CardPack.prefab 인스턴스)의 뜯기 핸들. BeginOpen에서 활성·ArmTear.")]
    [SerializeField] PackTearHandle packHandle;
    [Tooltip("팩 모델 루트(뜯김 후 스택 등장 시 숨김). 미배선이면 packHandle의 오브젝트를 쓴다.")]
    [SerializeField] GameObject packRoot;

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

    // 그리드 열 수(고정 3열, 행은 ceil(count/3)로 유동).
    const int GRID_COLUMNS = 3;

    // Idle → PackShown(팩 등장·뜯기 대기) → Tearing(봉인 뜯김 확정 순간) → Stacking(넘김) → Grid.
    enum EViewState { Idle, PackShown, Tearing, Stacking, Grid }

    EViewState m_state = EViewState.Idle;

    // 이번 개봉 세션의 결과(뜯김 완료 시 이 카드로 스택 빌드). 재진입/중복 뜯기 가드에도 쓴다.
    OpenedPack m_pending;

    // 이번 개봉으로 생성된 카드 뷰들(드로우 순서 = 인덱스 순서). 넘겨도 파괴하지 않고 그리드에서 재사용.
    readonly List<RevealCardView> m_cards = new List<RevealCardView>();

    // 현재 스택 top(드래그 가능) 카드의 인덱스. 넘길 때마다 +1, count 도달 시 그리드로.
    int m_topIndex;

    // ── 공개 API ──────────────────────────────────────────────

    /// <summary>개봉 세션 시작: 3D 팩을 보이고 뜯기 대기(PackShown). 컨트롤러가 캐리어 결과로 호출.</summary>
    public void BeginOpen(OpenedPack _opened)
    {
        // 이미 진행 중(재진입)이면 무시 — 중복 개봉/뜯기 연출 방지.
        if (m_state != EViewState.Idle) return;
        if (_opened == null || !_opened.Success)
        {
            Debug.LogWarning("[PackTearOpenView] BeginOpen에 유효하지 않은 OpenedPack — 개봉 취소.");
            return;
        }

        m_pending = _opened;
        ClearSpawned();

        ShowPack();
    }

    // ── 3D 팩 뜯기 단계 ───────────────────────────────────────

    // 팩 모델을 보이고 뜯기 입력 대기. 뜯김 확정 콜백을 등록한다.
    void ShowPack()
    {
        m_state = EViewState.PackShown;

        var t_root = packRoot != null ? packRoot : (packHandle != null ? packHandle.gameObject : null);
        if (t_root != null) t_root.SetActive(true);

        if (packHandle == null)
        {
            // 팩 핸들 미배선: 뜯기 인터랙션 없이 바로 스택으로(소프트락 방지).
            Debug.LogWarning("[PackTearOpenView] packHandle 미배선 → 뜯기 생략하고 바로 스택 진행.");
            OnPackTorn();
            return;
        }

        packHandle.SetTearCallback(OnPackTorn);
        packHandle.ArmTear();
    }

    // 봉인 뜯김 확정 콜백. 팩을 숨기고 캐리어 카드로 스택을 빌드한다(1회 가드).
    void OnPackTorn()
    {
        if (m_state != EViewState.PackShown) return;   // 중복 뜯기/오작동 방어.
        m_state = EViewState.Tearing;

        // 봉인 슬라이드 연출은 PackTearHandle이 자기 완결로 처리 중 — 팩 루트는 스택 등장과 함께 숨긴다.
        var t_root = packRoot != null ? packRoot : (packHandle != null ? packHandle.gameObject : null);
        if (t_root != null) t_root.SetActive(false);

        BuildStack(m_pending);
    }

    // ── 스택/그리드(구 PackOpeningView 이식) ───────────────────

    // 뽑힌 카드 N장을 덱처럼 겹쳐 스택 배치 → 맨 위만 드래그 활성.
    void BuildStack(OpenedPack _opened)
    {
        ClearSpawned();

        var t_cards = _opened != null ? _opened.Cards : null;
        int t_count = t_cards != null ? t_cards.Count : 0;
        for (int t_i = 0; t_i < t_count; t_i++)
        {
            if (cardPrefab == null || cardsContainer == null) break;

            var t_view = Instantiate(cardPrefab, cardsContainer);
            var t_drawn = t_cards[t_i];
            t_view.Reveal(t_drawn.Card, t_drawn.IsNew, t_drawn.Refund);
            t_view.SetSwipeCallback(OnCardSwiped);
            m_cards.Add(t_view);
        }

        m_topIndex = 0;
        if (m_cards.Count == 0)
        {
            // 구매는 이미 원자 처리(소유 부여·Save)됐으나 시각화할 카드가 없음(프리팹/컨테이너 미배선 등).
            // 개봉은 성공했으므로 완료로 간주해 콜백을 발화한다 — 획득 버튼 대기 데드락 방지.
            Debug.LogWarning("[PackTearOpenView] 개봉 성공했으나 표시할 카드가 없음(프리팹/컨테이너 배선 확인). 완료 처리.");
            m_state = EViewState.Idle;
            OnOpenComplete?.Invoke();
            return;
        }

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

        // 그리드 연출이 끝날 때까지 Grid 유지(연출 중 잠금) → 완료 후 Idle 복귀.
        StartCoroutine(CoGridToIdle());
    }

    // 그리드 이동·페이드 연출 시간만큼 대기 후 Idle 복귀 + 완료 콜백.
    IEnumerator CoGridToIdle()
    {
        float t_wait = Mathf.Max(gridMoveDuration, gridFadeDuration);
        if (t_wait > 0f) yield return new WaitForSeconds(t_wait);

        // Grid에서만 완료로 인정 → Idle 전이. 이 가드가 개봉당 1회 발화를 보장한다.
        // (OnDisable로 중간에 끊기면 코루틴이 죽어 발화하지 않음 = 개봉 미완료 취급.)
        if (m_state == EViewState.Grid)
        {
            m_state = EViewState.Idle;
            OnOpenComplete?.Invoke();   // 컨트롤러가 여기서 획득 버튼 노출
        }
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

        m_state = EViewState.Idle;
        m_topIndex = 0;
    }
}

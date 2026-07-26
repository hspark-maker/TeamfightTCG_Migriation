using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

// 개봉 씬 상주 브레인. 캐리어(PackHandoff)로 넘어온 개봉 세션을 뷰에 태우고, 개봉 완료 →
// 획득 버튼 노출 → 획득 클릭 → 덱 슬롯 0 저장 → (튜토리얼이면 시작 후) 목적지 씬으로 이동한다.
//
// 경계: 목적지 분기는 캐리어 값(NextScene/StartTutorial)으로만 한다 — 첫시작 판정을 여기서 재계산하지 않는다.
//   이것이 구 FirstStartBattleRedirect 같은 별도 리다이렉트 레이어를 없앤 이유(구매한 쪽이 목적지를 이미 결정).
//   Battle 참조는 TutorialConfig.Begin 한 줄뿐(TutorialSetupUI 선례와 동일 방향, 전투 지식 격리).
public class PackAcquireController : MonoBehaviour
{
    [Header("참조")]
    [Tooltip("카드팩 개봉 뷰. BeginOpen으로 세션을 태우고 OnRevealComplete를 수신.")]
    [SerializeField] PackRevealView view;
    [Tooltip("개봉 완료(카드 배치) 후 노출되는 획득 버튼.")]
    [SerializeField] Button acquireButton;
    [Tooltip("StartTutorial일 때 전투 진입 전 시작할 튜토리얼 시나리오. 일반 경로면 미사용.")]
    [SerializeField] TutorialScenarioData scenario;

    // 캐리어에서 받은 목적지 컨텍스트(Start에서 캡처, 이후 재판정 안 함).
    string m_nextScene;
    bool m_startTutorial;

    // 이번 개봉으로 뽑힌 카드(획득 시 덱 슬롯 0에 저장). Consume은 1회뿐이라 여기 캐시한다.
    readonly List<CardData> m_cards = new List<CardData>();

    // 획득이 2회 이상 눌려도 씬 전이는 1회만.
    bool m_left;

    void Start()
    {
        // 카드 배치 전까지 획득 버튼 숨김 — OnRevealComplete에서 노출.
        if (acquireButton != null) acquireButton.gameObject.SetActive(false);

        if (!PackHandoff.HasPending)
        {
            // 정상 진입은 상점/부트가 캐리어를 채우고 이 씬을 로드한 경우뿐. 직접 진입 등은 열 팩이 없음.
            Debug.LogWarning("[PackAcquireController] PackHandoff 없음 — 정상 진입이 아님(열 팩 없음).");
            return;
        }

        // 목적지 컨텍스트를 먼저 캡처한 뒤 Consume(캐리어를 통째로 비움 → 다음 개봉 세션 격리).
        m_nextScene = PackHandoff.NextScene;
        m_startTutorial = PackHandoff.StartTutorial;
        var t_opened = PackHandoff.Consume();
        CacheCards(t_opened);

        if (view != null) view.BeginOpen(t_opened);
        else Debug.LogWarning("[PackAcquireController] view 미배선 → 개봉 진행 불가.");
    }

    void OnEnable()
    {
        if (view != null) view.OnRevealComplete += OnRevealComplete;
        if (acquireButton != null) acquireButton.onClick.AddListener(OnAcquirePressed);
    }

    void OnDisable()
    {
        if (view != null) view.OnRevealComplete -= OnRevealComplete;
        if (acquireButton != null) acquireButton.onClick.RemoveListener(OnAcquirePressed);
    }

    // 개봉 카드 목록 캐시(null 카드 제외).
    void CacheCards(OpenedPack _opened)
    {
        m_cards.Clear();

        var t_drawn = _opened != null ? _opened.Cards : null;
        if (t_drawn == null) return;

        for (int t_i = 0; t_i < t_drawn.Count; t_i++)
            if (t_drawn[t_i].Card != null) m_cards.Add(t_drawn[t_i].Card);
    }

    // 개봉 연출(카드 배치)이 끝났을 때 1회 호출 → 획득 버튼 노출.
    void OnRevealComplete()
    {
        if (acquireButton != null) acquireButton.gameObject.SetActive(true);
    }

    // 획득 클릭: 덱 슬롯 0 저장 → 튜토리얼이면 시작 → 목적지 씬으로 이동(1회 가드).
    void OnAcquirePressed()
    {
        if (m_left) return;
        m_left = true;

        SaveOpenedDeck();

        // 튜토리얼 세팅은 목적지(전투) 진입 직전 1회. scenario null이면 Begin이 End로 안전 처리.
        if (m_startTutorial) TutorialConfig.Begin(scenario);

        if (string.IsNullOrEmpty(m_nextScene))
        {
            // 배선 오류로 버튼이 죽어 씬에 갇히는 것을 막는다(덱 저장은 멱등 — 같은 카드 재저장).
            m_left = false;
            Debug.LogWarning("[PackAcquireController] NextScene 미지정 — 씬 전이 불가(캐리어 설정 확인).");
            return;
        }
        SceneManager.LoadScene(m_nextScene);
    }

    // 개봉 카드를 덱 슬롯 0에 덮어쓰고 다음 전투 덱으로도 넘긴다.
    void SaveOpenedDeck()
    {
        if (m_cards.Count == 0)
        {
            // 빈 덱으로 기존 슬롯 0을 날리지 않는다.
            Debug.LogWarning("[PackAcquireController] 개봉 카드 없음 — 덱 저장 생략.");
            return;
        }

        // 카드 수 불일치도 저장은 진행(소유는 이미 영속됐고, 유효 덱 판정은 DeckSaveManager.IsSlotValid가 담당).
        if (m_cards.Count != DeckSaveManager.DECK_SIZE)
            Debug.LogWarning($"[PackAcquireController] 개봉 카드 {m_cards.Count}장 ≠ DECK_SIZE {DeckSaveManager.DECK_SIZE} — 불완전 덱으로 저장.");

        // 슬롯 0만 파일에 반영 — SaveToFile(전 슬롯 flush)은 이 씬이 부트를 안 거쳤을 때 다른 덱을 지운다.
        DeckSaveManager.SaveSlotToFile(0, m_cards);
        DeckConfig.Set(m_cards);
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

// 개봉 오버레이 상주 브레인. 캐리어(PackHandoff)로 넘어온 개봉 세션을 뷰에 태우고, 개봉 완료 →
// 획득 버튼 노출 → 획득 클릭 → 보상 캐리어 적재 → (튜토리얼이면 시작 후) 목적지로 나간다.
// 목적지가 지금 있는 씬이면 오버레이를 닫고, 다른 씬이면 씬을 로드한다.
// 덱은 만들지 않는다 — 카드 소유는 구매 시점에 이미 영속됐고, 편성은 덱 화면의 몫이다.
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

    // 이번 개봉에서 처음 얻은 카드만(로비 도감 연출 대상). Consume은 1회뿐이라 여기 캐시한다.
    // 중복은 팩 결제 재화로 환급돼 코인 연출로 나가므로 도감 연출거리가 없다.
    readonly List<CardData> m_newCards = new List<CardData>();
    // 이번 개봉의 중복 환급 합계(연출 표시용). 카드와 같은 시점에 캐시해야 짝이 갈라지지 않는다.
    CurrencyGain m_refund;

    // 획득이 2회 이상 눌려도 씬 전이는 1회만.
    bool m_left;

    /// <summary>캐리어에 실린 개봉 세션을 뷰에 태운다. 오버레이가 열릴 때마다 호출된다
    /// — Start에 두면 오버레이는 한 번만 열리는 화면이 된다(재개봉 불가).</summary>
    public bool BeginSession()
    {
        // 카드 배치 전까지 획득 버튼 숨김 — OnRevealComplete에서 노출.
        if (acquireButton != null) acquireButton.gameObject.SetActive(false);
        m_left = false;

        if (!PackHandoff.HasPending)
        {
            // 정상 진입은 상점/부트가 캐리어를 채우고 오버레이를 연 경우뿐. 그 외는 열 팩이 없음.
            Debug.LogWarning("[PackAcquireController] PackHandoff 없음 — 정상 진입이 아님(열 팩 없음).");
            return false;
        }
        if (view == null)
        {
            Debug.LogWarning("[PackAcquireController] view 미배선 → 개봉 진행 불가.");
            return false;
        }

        // 목적지·팩 컨텍스트를 먼저 캡처한 뒤 Consume(캐리어를 통째로 비움 → 다음 개봉 세션 격리).
        m_nextScene = PackHandoff.NextScene;
        m_startTutorial = PackHandoff.StartTutorial;
        var t_pack = PackHandoff.Pack;
        var t_opened = PackHandoff.Consume();
        CacheCards(t_opened);

        view.BeginOpen(t_opened, t_pack);
        return true;
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

    IEnumerator CloseNextFrame()
    {
        yield return null;

        if (PackOpenOverlay.Instance != null) PackOpenOverlay.Instance.Close();
    }

    // 신규 획득 카드 + 환급 총액 캐시(null 카드 제외).
    void CacheCards(OpenedPack _opened)
    {
        m_newCards.Clear();
        m_refund = _opened != null ? _opened.TotalRefund : CurrencyGain.None;

        var t_drawn = _opened != null ? _opened.Cards : null;
        if (t_drawn == null) return;

        for (int t_i = 0; t_i < t_drawn.Count; t_i++)
        {
            var t_card = t_drawn[t_i].Card;
            if (t_card == null || !t_drawn[t_i].IsNew) continue;

            m_newCards.Add(t_card);
        }
    }

    // 개봉 연출(카드 배치)이 끝났을 때 1회 호출 → 획득 버튼 노출.
    void OnRevealComplete()
    {
        if (acquireButton != null) acquireButton.gameObject.SetActive(true);
    }

    // 획득 클릭: 튜토리얼이면 시작 → 목적지 씬으로 이동(1회 가드).
    // 개봉 카드로 덱을 만들지 않는다 — 첫 덱은 부트의 StarterDeck이 보장하고, 이후 편성은 유저 몫이다.
    void OnAcquirePressed()
    {
        if (m_left) return;
        m_left = true;

        // 골드·카드가 같은 개봉 세션 결과라 로비 복귀 직전 한 지점에서 함께 싣는다(지급·저장은 이미 끝났다 — 표시량뿐).
        // 카드는 신규만 — 중복분은 환급 골드로 이미 표현된다. 튜토리얼 경로로 전투에 먼저 가면 이후 로비 진입 시 재생된다.
        CardPackRewardHandoff.Set(m_refund, m_newCards);

        // 튜토리얼 세팅은 목적지(전투) 진입 직전 1회. scenario null이면 Begin이 End로 안전 처리.
        if (m_startTutorial) TutorialConfig.Begin(scenario);

        // 목적지가 지금 있는 씬이면 떠날 것이 없다 — 오버레이만 닫아 로비로 돌아간다(일반 구매 경로).
        // 목적지가 비었을 때도 같다: 단독 테스트 씬은 그 자리에 머무는 것이 기대 동작이다.
        // 다른 씬이면 진짜 씬 로드(첫실행의 전투 진입). 분기는 여기 한 곳뿐 — 목적지 판정을 늘리지 않는다.
        if (string.IsNullOrEmpty(m_nextScene) || m_nextScene == SceneManager.GetActiveScene().name)
        {
            if (PackOpenOverlay.Instance == null)
            {
                // 배선 오류로 버튼이 죽어 화면에 갇히는 것을 막는다(재실행돼도 캐리어 재적재뿐이라 부작용 없음).
                m_left = false;
                Debug.LogWarning("[PackAcquireController] PackOpenOverlay 없음 — 닫기 불가(오버레이 배선 확인).");
                return;
            }

            // 같은 버튼에 튜토리얼 게이트가 함께 걸려 있다 — 여기서 바로 닫으면 오버레이 닫힘이
            // 게이트의 완료 커밋보다 먼저 돌아 그 커밋이 유실된다(진행 불가).
            // 씬 로드 시절엔 로드가 프레임 끝이라 저절로 지켜지던 순서라, 그 타이밍을 명시로 되살린다.
            StartCoroutine(CloseNextFrame());
            return;
        }

        SceneManager.LoadScene(m_nextScene);
    }

}

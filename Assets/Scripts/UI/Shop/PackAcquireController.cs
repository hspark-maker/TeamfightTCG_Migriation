using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

// PackTest 씬 상주 브레인. 캐리어(PackHandoff)로 넘어온 개봉 세션을 뷰에 태우고,
// 개봉 완료 → 획득 버튼 노출 → 획득 클릭 → (튜토리얼이면 시작 후) 목적지 씬으로 이동한다.
//
// 경계: 목적지 분기는 캐리어 값(NextScene/StartTutorial)으로만 한다 — 첫시작 판정을 여기서 재계산하지 않는다.
//   이것이 구 FirstStartBattleRedirect 같은 별도 리다이렉트 레이어를 없앤 이유(구매한 쪽이 목적지를 이미 결정).
//   Battle 참조는 TutorialConfig.Begin 한 줄뿐(TutorialSetupUI 선례와 동일 방향, 전투 지식 격리).
public class PackAcquireController : MonoBehaviour
{
    [Header("참조")]
    [Tooltip("3D 팩 뜯기 개봉 뷰. BeginOpen으로 세션을 태우고 OnOpenComplete를 수신.")]
    [SerializeField] PackTearOpenView view;
    [Tooltip("개봉 완료(그리드 배치) 후 노출되는 획득 버튼.")]
    [SerializeField] Button acquireButton;
    [Tooltip("StartTutorial일 때 전투 진입 전 시작할 튜토리얼 시나리오. 일반 경로면 미사용.")]
    [SerializeField] TutorialScenarioData scenario;

    // 캐리어에서 받은 목적지 컨텍스트(Start에서 캡처, 이후 재판정 안 함).
    string m_nextScene;
    bool m_startTutorial;

    // 획득이 2회 이상 눌려도 씬 전이는 1회만.
    bool m_left;

    void Start()
    {
        // 카드 배열 전까지 획득 버튼 숨김 — OnOpenComplete에서 노출.
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

        if (view != null) view.BeginOpen(t_opened);
        else Debug.LogWarning("[PackAcquireController] view 미배선 → 개봉 진행 불가.");
    }

    void OnEnable()
    {
        if (view != null) view.OnOpenComplete += OnOpenComplete;
        if (acquireButton != null) acquireButton.onClick.AddListener(OnAcquirePressed);
    }

    void OnDisable()
    {
        if (view != null) view.OnOpenComplete -= OnOpenComplete;
        if (acquireButton != null) acquireButton.onClick.RemoveListener(OnAcquirePressed);
    }

    // 개봉이 그리드 연출까지 끝났을 때 1회 호출 → 획득 버튼 노출.
    void OnOpenComplete()
    {
        if (acquireButton != null) acquireButton.gameObject.SetActive(true);
    }

    // 획득 클릭: 튜토리얼이면 시작 후 목적지 씬으로 이동(1회 가드).
    void OnAcquirePressed()
    {
        if (m_left) return;
        m_left = true;

        // 튜토리얼 세팅은 목적지(전투) 진입 직전 1회. scenario null이면 Begin이 End로 안전 처리.
        if (m_startTutorial) TutorialConfig.Begin(scenario);

        if (string.IsNullOrEmpty(m_nextScene))
        {
            Debug.LogWarning("[PackAcquireController] NextScene 미지정 — 씬 전이 불가(캐리어 설정 확인).");
            return;
        }
        SceneManager.LoadScene(m_nextScene);
    }
}

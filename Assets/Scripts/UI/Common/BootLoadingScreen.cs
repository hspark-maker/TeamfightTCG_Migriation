using System.Collections;
using DG.Tweening;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 앱 기동 화면 = StartScene(빌드 0번)의 유일한 화면.
//
// 부트 로딩(DataLibrary의 Addressables UI 프리팹 로드)이 끝나고 최소 노출 시간이 지나면
// 다음 목적지를 스스로 판정해 씬을 넘긴다.
// 슬라이더 목표치는 "실제 진행도"와 "최소 시간 진척" 중 느린 쪽이라 로딩이 즉시 끝나도 바가 순간이동하지 않는다.
//
// 커버를 제자리에서 페이드아웃하지 않는 이유: 이 씬에는 커버 말고 아무것도 없어(카메라도 검은 단색 배경)
// 알파를 내리면 다음 씬이 오기 전에 검은 화면이 드러난다. 그래서 순서를 뒤집는다 —
// 커버를 DontDestroyOnLoad로 들고 씬을 넘어간 뒤 다음 씬 위에서 걷어야 로딩 아트가 그대로 다음 화면에 녹는다.
// 그 대가로 커버는 씬과 함께 죽지 않으므로, 걷은 뒤 반드시 Destroy해야 이후 모든 씬에 남지 않는다.
public class BootLoadingScreen : MonoBehaviour
{
    // 저작 데이터가 아니라 시스템 고정 경로라 상수로 둔다(OutgameTutorialRunner와 같은 규약).
    const string LobbyScene = "LobbyScene";

    [Tooltip("진행도 슬라이더. 미배선이면 표시 없이 대기만 한다(min/max 무관하게 정규값으로 쓴다).")]
    [SerializeField] Slider progressBar;

    [Tooltip("로딩이 즉시 끝나도 이 시간(초)만큼은 노출한다 — 한 프레임 깜빡임 방지.")]
    [SerializeField] float minDuration = 1f;

    [Tooltip("슬라이더가 목표치를 따라가는 속도(비율/초).")]
    [SerializeField] float barFollowSpeed = 2f;

    [Tooltip("로딩이 끝나지 않아도 이 시간(초)이 지나면 다음 씬으로 넘긴다 — 무한 대기 방지.")]
    [SerializeField] float maxDuration = 15f;

    [Tooltip("진행바가 100%에 닿은 뒤 씬을 넘기기 전 유지 시간(초).")]
    [SerializeField] float holdBeforeLoad = 0.15f;

    [Tooltip("다음 씬 위에서 커버를 걷는 시간(초).")]
    [SerializeField] float fadeDuration = 0.4f;

    // 커버 루트의 페이드 대상. Canvas·CanvasGroup·이 스크립트가 모두 같은 오브젝트라 배선 없이 잡는다.
    CanvasGroup m_group;

    void Awake()
    {
        m_group = GetComponent<CanvasGroup>();

        if (progressBar != null) progressBar.normalizedValue = 0f;
    }

    void Start()
    {
        StartCoroutine(CoRun());
    }

    IEnumerator CoRun()
    {
        float t_elapsed = 0f;
        float t_shown   = 0f;   // 실제로 슬라이더에 그려지는 값(보간 결과)

        while (true)
        {
            t_elapsed += Time.unscaledDeltaTime;

            // 로딩 완료와 최소 노출 중 느린 쪽이 목표 — 둘 다 충족돼야 100%에 도달한다.
            float t_target = Mathf.Min(DataLibrary.LoadProgress, t_elapsed / Mathf.Max(minDuration, 0.01f));
            t_shown = Mathf.MoveTowards(t_shown, t_target, barFollowSpeed * Time.unscaledDeltaTime);

            if (progressBar != null) progressBar.normalizedValue = t_shown;

            // PercentComplete는 프리팹 등록 콜백이 끝나기 전에 1이 될 수 있어 완료 플래그로 확정한다.
            if (t_shown >= 1f && DataLibrary.IsLoaded) break;

            if (t_elapsed >= maxDuration)
            {
                Debug.LogWarning($"[BootLoadingScreen] 로딩이 {maxDuration}초 안에 끝나지 않아 그대로 진행합니다.");
                break;
            }

            yield return null;
        }

        if (progressBar != null) progressBar.normalizedValue = 1f;

        yield return StartCoroutine(CoGoNext());
    }

    // 첫 스텝이 전투 직행이면 로비를 거치지 않는다. 다른 자동 스텝(AutoPurchase)까지 여기서 실행하지 않는 이유는
    // 그쪽이 실패하면 씬 전환 없이 돌아와(구매 실패) 탈출로가 없는 이 씬에 갇히기 때문 — 로비 브리지가 맡는다.
    IEnumerator CoGoNext()
    {
        // 바가 꽉 찬 것을 눈으로 확인시키는 홀드. 로딩 화면은 timeScale을 신뢰할 수 없어 unscaled.
        if (holdBeforeLoad > 0f) yield return new WaitForSecondsRealtime(holdBeforeLoad);

        // 로드보다 반드시 먼저 — 이 뒤로 누가 씬을 걸든 커버가 살아남아 전환 순간을 덮는다.
        DontDestroyOnLoad(gameObject);

        // 여기부터는 어떻게 빠져나가든 커버를 걷어야 한다. 남기면 DDOL + sortingOrder 1000 + blocksRaycasts가
        // 이후 모든 씬을 영구 입력 불가로 잠가 재시작 말고는 탈출로가 없다 — 그래서 finally에 건다.
        try
        {
            bool t_needLobby = true;

            if (OutgameTutorialRunner.TryGetCurrentStep(out var t_step)
                && t_step.kind == OutgameTutorialData.EStepKind.AutoBattle)
            {
                // 러너가 전투 씬을 걸었으면 false를 준다(커밋·시나리오 주입 포함). AutoBattle 진입은 실제로 항상 씬을 걸므로
                // 로비가 필요해지는 건 스텝 판정이 어긋난 예외뿐이다. 조기 return은 두지 않는다 — 커버를 걷어야 하니까.
                t_needLobby = OutgameTutorialRunner.EnterCurrentStep();
            }

            if (t_needLobby) yield return SceneManager.LoadSceneAsync(LobbyScene);

            // 새 씬이 최소 한 번 그려지도록 한 프레임만 양보한다. "준비 완료"를 뜻하지는 않는다 —
            // 전투 보드 생성(GameInitializer)은 비동기고, 로비는 곧장 다음 씬으로 넘어가기도 한다.
            yield return null;
        }
        finally
        {
            Reveal();
        }
    }

    // 다음 씬 위에서 커버를 걷고 자신을 파괴한다. DDOL로 살아남은 오브젝트라 비활성화로는 부족하다.
    void Reveal()
    {
        if (this == null) return;   // 오브젝트가 이미 파괴돼 코루틴이 잘려 들어온 경우 — 걷을 커버가 없다.

        if (m_group == null) { Destroy(gameObject); return; }

        // 페이드 내내 blocksRaycasts를 유지한다(퇴장 시작에 푸는 PopupTransition과 다른 선택) —
        // 반쯤 비치는 다음 화면을 오조작하지 않게. 커버가 통째로 사라지므로 입력이 막힌 채 남을 일은 없다.
        m_group.DOKill();
        m_group.DOFade(0f, fadeDuration)
            .SetUpdate(true)              // 다음 씬이 timeScale을 0으로 잡아도 걷히도록.
            .SetLink(gameObject)
            .OnComplete(() => Destroy(gameObject))
            .OnKill(() => { if (this != null) Destroy(gameObject); })   // 외부에서 트윈이 죽어도 커버는 남기지 않는다.
            .Play();                      // 재생 책임을 코드에 남긴다(전역 autoPlay 설정에 기대지 않게).
    }
}

using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 앱 기동 화면 = LoadingScene(빌드 0번)의 유일한 화면.
//
// 부트 로딩(DataLibrary의 Addressables UI 프리팹 로드)이 끝나고 최소 노출 시간이 지나면
// 다음 목적지를 스스로 판정해 씬을 넘긴다 — 커버를 걷는 개념이 없다(씬째로 사라진다).
// 슬라이더 목표치는 "실제 진행도"와 "최소 시간 진척" 중 느린 쪽이라 로딩이 즉시 끝나도 바가 순간이동하지 않는다.
//
// 페이드아웃을 두지 않는 이유: 알파를 내리면 다음 씬이 오기 전에 빈 LoadingScene이 한 프레임 드러난다.
// 불투명한 채로 씬을 교체하는 것이 맞다.
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

    void Awake()
    {
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

        GoNext();
    }

    // 첫 스텝이 전투 직행이면 로비를 거치지 않는다. 다른 자동 스텝(AutoPurchase)까지 여기서 실행하지 않는 이유는
    // 그쪽이 실패하면 씬 전환 없이 돌아와(구매 실패) 탈출로가 없는 이 씬에 갇히기 때문 — 로비 브리지가 맡는다.
    void GoNext()
    {
        if (OutgameTutorialRunner.TryGetCurrentStep(out var t_step)
            && t_step.kind == OutgameTutorialData.EStepKind.AutoBattle)
        {
            // false = 러너가 이미 전투 씬을 걸었다(커밋·시나리오 주입 포함).
            if (!OutgameTutorialRunner.EnterCurrentStep()) return;
        }

        SceneManager.LoadScene(LobbyScene);
    }
}

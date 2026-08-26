using System;
using System.Collections;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 씬 전환을 덮는 전체화면 로딩 커버. 두 가지 방식으로 산다.
//  - 부트: StartScene(빌드 0번)에 저작된 인스턴스. DataLibrary의 Addressables UI 로드를 기다렸다가
//          다음 목적지를 스스로 판정해 씬을 넘긴다.
//  - 전환: LoadScene(scene)이 동기 UI 카탈로그에서 띄운 인스턴스. 전투 → 로비 복귀처럼 부트가 이미 끝난
//          상태의 씬 전환을 덮는다(BattleCleanup 경유).
// 어느 쪽이든 로비로 들어오는 화면은 같은 커버를 탄다.
//
// 커버를 제자리에서 페이드아웃하지 않는 이유: 부트 씬에는 커버 말고 아무것도 없어(카메라도 검은 단색 배경)
// 알파를 내리면 다음 씬이 오기 전에 검은 화면이 드러난다. 그래서 순서를 뒤집는다 —
// 커버를 DontDestroyOnLoad로 들고 씬을 넘어간 뒤 다음 씬 위에서 걷어야 로딩 아트가 그대로 다음 화면에 녹는다.
// 그 대가로 커버는 씬과 함께 죽지 않으므로, 걷은 뒤 반드시 Destroy해야 이후 모든 씬에 남지 않는다.
public class LoadingCoverView : MonoBehaviour
{
    // 저작 데이터가 아니라 시스템 고정 경로라 상수로 둔다(OutgameTutorialRunner와 같은 규약).
    const string LobbyScene = "LobbyScene";

    // 전환 모드가 커버를 얻는 유일한 경로. Addressables 초기화보다 먼저 필요할 수 있으므로
    // Initialize 프리팹이 직렬화한 카탈로그에서 동기적으로 얻는다.
    [Tooltip("진행도 슬라이더. 미배선이면 표시 없이 대기만 한다(min/max 무관하게 정규값으로 쓴다).")]
    [SerializeField] Slider progressBar;
    [SerializeField] TMP_Text statusText;

    [Tooltip("로딩이 즉시 끝나도 이 시간(초)만큼은 노출한다 — 한 프레임 깜빡임 방지.")]
    [SerializeField] float minDuration = 1f;

    [Tooltip("슬라이더가 목표치를 따라가는 속도(비율/초).")]
    [SerializeField] float barFollowSpeed = 2f;

    [Tooltip("로딩이 끝나지 않아도 이 시간(초)이 지나면 다음 씬으로 넘긴다 — 무한 대기 방지.")]
    [SerializeField] float maxDuration = 15f;

    // 초기화 대기는 진행바 연출과 다른 축이다. maxDuration을 같이 쓰면 CoFillBar가 그 예산을 먼저
    // 소진한 뒤 초기화 대기가 0초로 시작해, 조금만 느려도 곧장 복구 화면으로 떨어진다.
    // 세이브 채택만으로도 최악 21초(auth 5s + 읽기 5s×3 + 백오프 0.5s×2)라 넉넉히 잡는다.
    [SerializeField] float initializeWaitTimeout = 45f;

    [Tooltip("진행바가 100%에 닿은 뒤 씬을 넘기기 전 유지 시간(초).")]
    [SerializeField] float holdBeforeLoad = 0.15f;

    [Tooltip("다음 씬 위에서 커버를 걷는 시간(초).")]
    [SerializeField] float fadeDuration = 0.4f;

    [Tooltip("전환 모드에서 커버가 이전 화면을 덮는 시간(초). 부트 모드는 덮을 화면이 없어 무시한다.")]
    [SerializeField] float fadeInDuration = 0.15f;

    // 지금 화면을 덮고 있는 커버(없으면 null). 커버는 DontDestroyOnLoad로 다음 씬 위까지 살아남으므로
    // 새 씬의 Start는 아직 가려진 화면에서 돈다 — 로비 연출은 이게 걷힌 뒤에 시작해야 눈에 보인다.
    static LoadingCoverView s_active;

    /// <summary>커버가 화면을 덮고 있는가(페이드아웃이 끝나 파괴되면 false).</summary>
    public static bool IsCovering => s_active != null;

    // 커버 루트의 페이드 대상. Canvas·CanvasGroup·이 스크립트가 모두 같은 오브젝트라 배선 없이 잡는다.
    CanvasGroup m_group;

    // 전환 모드의 목적지. null이면 부트 모드 — 목적지를 스스로 판정한다.
    string m_targetScene;

    // 씬 교체 직전에 돌려줄 정리 훅(전환 모드 전용). LoadScene의 _onBeforeLoad 참고.
    Action m_beforeLoad;

    // 커버가 최소 1초는 도니 커튼(0.25초)보다 넉넉하게 뺀다.
    const float BgmFadeOutSeconds = 0.5f;

    /// <summary>커버를 띄운 뒤 _scene을 비동기 로드하고, 새 씬 위에서 커버를 걷는다.
    /// 부트가 이미 끝난 뒤의 씬 전환용(전투 → 로비).</summary>
    /// <param name="_onBeforeLoad">씬 교체 **직전** 1회 호출. 화면을 망가뜨리는 정리(오브젝트 파괴·풀 비우기)는
    /// 반드시 여기로 넘긴다 — 커버는 1초 넘게 도는데, 그 전에 정리하면 이전 씬이 파괴된 오브젝트를 붙잡은 채
    /// 그 시간만큼 더 살아 돌며 진행 중이던 연출 체인이 깨어나 그걸 만진다(MissingReferenceException).</param>
    public static void LoadScene(string _scene, Action _onBeforeLoad = null)
    {
        // 씬을 벗어나는 모든 호출부가 이 창구를 지나므로 BGM 퇴장은 여기 한 곳에서 책임진다.
        SoundManager.Instance?.FadeOutBGM(BgmFadeOutSeconds);

        var t_prefab = SyncUiPrefabs.Get(ESyncUiPrefab.LoadingCover);
        var t_view   = t_prefab != null ? Instantiate(t_prefab).GetComponent<LoadingCoverView>() : null;

        // 커버를 못 얻어도 전환 자체는 반드시 되게 한다 — 연출 때문에 화면이 갇히면 탈출로가 없다.
        if (t_view == null)
        {
            Debug.LogWarning("[LoadingCoverView] 동기 UI 카탈로그에서 로딩 커버를 찾지 못해 커버 없이 전환합니다.");
            _onBeforeLoad?.Invoke();
            SceneManager.LoadScene(_scene);
            return;
        }

        t_view.m_beforeLoad = _onBeforeLoad;

        // Instantiate는 Awake를 그 자리에서 돌리지만 Start는 프레임 끝에 온다 — 이 대입이 모드 분기보다 먼저다.
        t_view.m_targetScene = _scene;

        // 페이드인 시작값도 같은 이유로 여기서 준다. Start를 기다리면 프리팹의 alpha 1이 한 프레임 그려져
        // 하드컷으로 덮은 뒤 페이드인이 도는 꼴이 된다. alpha 0이어도 blocksRaycasts는 그대로라 입력은 이미 막힌다.
        if (t_view.m_group != null && t_view.fadeInDuration > 0f) t_view.m_group.alpha = 0f;
    }

    void Awake()
    {
        s_active = this;
        m_group = GetComponent<CanvasGroup>();

        if (progressBar != null) progressBar.normalizedValue = 0f;
    }

    // 파괴 = 페이드아웃 완료(Reveal) 시점이다 — 여기가 "이제 화면이 보인다"의 유일한 신호다.
    void OnDestroy()
    {
        if (s_active == this) s_active = null;
    }

    void Start()
    {
        StartCoroutine(m_targetScene == null ? CoRunInitialize() : CoRunSceneLoad());
    }

    // ── 부트 모드 ─────────────────────────────────────────────────────────────

    IEnumerator CoRunInitialize()
    {
        if (GameInitialization.State == EGameInitState.UpdateRequired)
        {
            if (statusText != null)
                statusText.text = "새 버전으로 업데이트가 필요합니다.";
            if (progressBar != null)
                progressBar.gameObject.SetActive(false);
            yield break;
        }

        if (statusText != null)
            statusText.text = "플레이어 데이터를 동기화하는 중입니다.";

        // 완료 플래그 전에는 바가 1에 닿지 못하게 막는다 — PercentComplete는 프리팹 등록 콜백이
        // 끝나기 전에 1이 될 수 있어, 그대로 쓰면 등록이 덜 된 채로 다음 씬에 넘어간다.
        yield return CoFillBar(() => GameInitialization.Progress);

        // 타임아웃은 로딩 화면의 진행 정책으로 유지하되, 완료 조건은 전역 초기화 창구만 본다.
        float t_initializeWaitStarted = Time.realtimeSinceStartup;
        while (!GameInitialization.IsReady && !GameInitialization.IsTerminated)
        {
            if (Time.realtimeSinceStartup - t_initializeWaitStarted >= initializeWaitTimeout)
            {
                Debug.LogError($"[LoadingCoverView] 게임 초기화가 {initializeWaitTimeout}초 안에 끝나지 않아 복구 화면으로 전환합니다. " +
                               $"상태={GameInitialization.State}");
                GameInitialization.MarkRecoveryRequired();
                break;
            }
            yield return null;
        }

        if (GameInitialization.IsTerminated)
        {
            if (statusText != null)
            {
                statusText.text = GameInitialization.State == EGameInitState.UpdateRequired
                    ? "새 버전으로 업데이트가 필요합니다."
                    : "저장 데이터를 복구하지 못했습니다.";
            }
            if (progressBar != null)
                progressBar.gameObject.SetActive(false);
            yield break;
        }

        yield return CoGoNext();
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

            if (OutgameTutorialRunner.TryGetCurrentStep(out var t_step) && t_step.Action == EOutgameTutorialAction.AutoBattle)
            {
                // 러너가 전투 씬을 걸었으면 Gated가 아니다(커밋·시나리오 주입 포함). AutoBattle 진입은 실제로 항상 씬을 걸므로
                // 로비가 필요해지는 건 스텝 판정이 어긋난 예외뿐이다. 조기 return은 두지 않는다 — 커버를 걷어야 하니까.
                t_needLobby = OutgameTutorialRunner.EnterCurrentStep() == EOutgameTutorialStepResult.Gated;
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

    // ── 전환 모드 ─────────────────────────────────────────────────────────────

    IEnumerator CoRunSceneLoad()
    {
        // 덮을 이전 화면이 있는 쪽이라 등장도 연출한다(부트는 검은 배경뿐이라 페이드인할 것이 없다).
        // blocksRaycasts는 프리팹에서 켜진 채 유지 — 반쯤 비치는 이전 화면을 오조작하지 않게.
        if (fadeInDuration > 0f && m_group != null)
        {
            m_group.alpha = 0f;
            m_group.DOFade(1f, fadeInDuration).SetUpdate(true).SetLink(gameObject).Play();
        }

        // 로드보다 반드시 먼저 — 씬이 갈려도 커버가 살아남아 전환 순간을 덮는다.
        DontDestroyOnLoad(gameObject);

        // 여기서 걸리는 씬은 부트가 이미 데워둔 상태라 로드가 한두 프레임에 끝난다. 활성화를 붙잡지 않으면
        // 바가 차기도 전에 씬이 갈려 커버가 한 프레임만 번쩍인다 — 노출 길이를 정하는 건 로드 시간이 아니라 minDuration이다.
        var t_op = SceneManager.LoadSceneAsync(m_targetScene);
        t_op.allowSceneActivation = false;

        // 부트 경로와 같은 이유로 finally에 건다 — 커버를 남기면 이후 모든 씬이 입력 불가가 된다.
        try
        {
            // 활성화를 막아둔 동안 progress는 0.9에서 멈춘다 — 그 구간을 0~1로 편다.
            yield return CoFillBar(() => t_op.progress / 0.9f);

            if (holdBeforeLoad > 0f) yield return new WaitForSecondsRealtime(holdBeforeLoad);

            // 정리는 여기 — 씬 교체와 붙어 있어야 파괴된 오브젝트를 붙잡은 연출 체인이 깨어날 틈이 없다.
            // 커버 자신은 이 시점에 도는 트윈이 없어(페이드인은 끝났고 Reveal은 뒤에 만들어진다)
            // 훅이 DOTween.KillAll류를 돌려도 커버가 같이 죽지 않는다.
            m_beforeLoad?.Invoke();
            m_beforeLoad = null;

            t_op.allowSceneActivation = true;
            yield return t_op;

            yield return null;   // 새 씬이 최소 한 번 그려지도록 한 프레임 양보.
        }
        finally
        {
            Reveal();
        }
    }

    // ── 공용 ──────────────────────────────────────────────────────────────────

    // 바를 목표치까지 따라 올린다. 목표는 "실제 진행도"와 "최소 시간 진척" 중 느린 쪽이라
    // 로딩이 즉시 끝나도 바가 순간이동하지 않는다 — 커버가 minDuration만큼은 눈에 남는다.
    IEnumerator CoFillBar(Func<float> _progress)
    {
        float t_elapsed = 0f;
        float t_shown   = 0f;   // 실제로 슬라이더에 그려지는 값(보간 결과)

        while (true)
        {
            t_elapsed += Time.unscaledDeltaTime;

            float t_target = Mathf.Min(_progress(), t_elapsed / Mathf.Max(minDuration, 0.01f));
            t_shown = Mathf.MoveTowards(t_shown, t_target, barFollowSpeed * Time.unscaledDeltaTime);

            if (progressBar != null) progressBar.normalizedValue = t_shown;

            if (t_shown >= 1f) break;

            if (t_elapsed >= maxDuration)
            {
                Debug.LogWarning($"[LoadingCoverView] 로딩이 {maxDuration}초 안에 끝나지 않아 그대로 진행합니다.");
                break;
            }

            yield return null;
        }

        if (progressBar != null) progressBar.normalizedValue = 1f;
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

using Cysharp.Threading.Tasks;
using DG.Tweening;

public static class BattleCleanup
{
    public static void Run()
    {
        DOTween.KillAll();
        ParticlePooler.Flush();
        ObjectPooler.Flush<UnityEngine.GameObject>();

        CardView.Cleanup();
        TurnState.Reset();
        TurnRunner.Cleanup();
    }

    /// <summary>러너 종료를 기다리지 않고 씬을 로드하면 안 되는 이유 —
    /// Shutdown이 끝나기 전에 다음 씬이 뜨면 그 씬에서 `Runner.IsRunning`이 아직 true다.
    /// GameInitializer는 그걸 보고 모드를 판정하므로(스테일 러너 → 멀티 오진입),
    /// 이 대기를 빼면 전투가 시작조차 못 하는 경로가 열린다.
    /// 종료가 늦어져도 UI가 잠기지 않게 상한을 둔다 — 상한을 넘기면 그냥 진행한다.</summary>
    const float DisconnectTimeoutSec = 3f;

    // 요청과 실제 로드 사이에 await(러너 종료 대기)와 커버 연출이 끼어 있다. 그 사이 결과 팝업을 또 누르면
    // 대기가 하나 더 붙고 커버도 둘이 떠, 늦게 깬 쪽이 **다음 씬에 들어간 뒤** 또 LoadScene을 때린다.
    // 이 static은 씬 파괴에 안 묶이므로(그래서 씬 전환을 끝까지 책임질 수 있다) 가드가 필수다.
    static bool s_loading;

    public static void LoadScene(string _scene) => LoadSceneAsync(_scene).Forget();

    static async UniTaskVoid LoadSceneAsync(string _scene)
    {
        if (s_loading) return;
        s_loading = true;

        try
        {
            // 멀티플레이어: Runner 종료 (Disconnect 내부에서 새 Runner 덮어쓰기 방지)
            var t_session = NetworkSession.Instance;
            if (t_session != null)
            {
                int t_timedOut = await UniTask.WhenAny(
                    t_session.Disconnect(),
                    UniTask.Delay(System.TimeSpan.FromSeconds(DisconnectTimeoutSec), ignoreTimeScale: true));
                if (t_timedOut == 1)
                    UnityEngine.Debug.LogWarning($"[Net] Runner 종료가 {DisconnectTimeoutSec}초 안에 안 끝났다. 씬 전환은 진행한다.");
            }

            // 전투를 떠나는 유일한 문이라 여기서 커버를 태운다 — 로비로 들어오는 화면은 부트든 복귀든 같은 연출이다.
            // 커버를 못 얻으면 내부에서 Run() 후 맨 로드로 떨어지므로 전환 자체는 어떤 경우에도 보장된다.
            //
            // Run()을 지금 부르지 않고 커버에 넘기는 이유: 커버는 1초 넘게 돌고 그동안 전투 씬은 계속 살아 있다.
            // 여기서 미리 정리하면 그 시간 내내 파괴된 카드가 남고, 진행 중이던 연출 체인(BattleIntro의 배치 딜레이 등)이
            // 깨어나 그걸 만져 MissingReferenceException이 난다. 커버 아래에선 전투를 그대로 두고, 씬을 갈아끼우는
            // 프레임에 정리한다. 커버가 화면을 덮고 있으니 그동안 전투가 더 도는 건 보이지 않는다.
            LoadingCoverView.LoadScene(_scene, Run);
            // 커버가 뜬 뒤로는 전체화면 blocksRaycasts가 두 번째 입력을 막는다 — 한 프레임만 넘기고 풀어도 안전하다.
            await UniTask.NextFrame();
        }
        finally
        {
            // 예외가 나도 반드시 푼다. 안 그러면 이 앱 세션 내내 씬 전환이 막힌다(소프트락).
            s_loading = false;
        }
    }
}

using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine.SceneManagement;

public static class BattleCleanup
{
    public static void Run()
    {
        // 승패 여운이 남긴 전역 상태(배속·배경 블러·BGM 피치)부터 되돌린다. 트윈을 죽이는 것으로는 안 풀리고,
        // 블러와 피치는 씬·매니저 수명 밖에 있어 여기서 빠뜨리면 로비가 흐리거나 끌린 채로 뜬다.
        BattleResultBeat.Reset();
        BattleFinisher.Disarm();

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

    // Run()과 실제 로드 사이에 await가 생겼다. 그 사이 결과 팝업을 또 누르면 Run()이 두 번 돌고
    // 대기가 하나 더 붙어, 늦게 깬 쪽이 **다음 씬에 들어간 뒤** 또 LoadScene을 때린다.
    // 이 static은 씬 파괴에 안 묶이므로(그래서 씬 전환을 끝까지 책임질 수 있다) 가드가 필수다.
    static bool s_loading;

    public static void LoadScene(string _scene) => LoadSceneAsync(_scene).Forget();

    static async UniTaskVoid LoadSceneAsync(string _scene)
    {
        if (s_loading) return;
        s_loading = true;

        try
        {
            Run();

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

            SceneManager.LoadScene(_scene);
            // LoadScene은 프레임 끝에 적용된다 — 한 프레임 넘긴 뒤에 풀어야 같은 프레임의 두 번째 입력이 안 샌다.
            await UniTask.NextFrame();
        }
        finally
        {
            // 예외가 나도 반드시 푼다. 안 그러면 이 앱 세션 내내 씬 전환이 막힌다(소프트락).
            s_loading = false;
        }
    }
}

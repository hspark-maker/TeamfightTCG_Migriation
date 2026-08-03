using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine.SceneManagement;

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

    public static void LoadScene(string _scene)
    {
        Run();
        // 멀티플레이어: Runner 종료 (Disconnect 내부에서 새 Runner 덮어쓰기 방지)
        NetworkSession.Instance?.Disconnect().Forget();
        SceneManager.LoadScene(_scene);
    }

    public static void ReloadScene()
    {
        Run();
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}

using UnityEngine;

/// <summary>전투 씬 진입의 <b>단일 지점</b>. 멀티에서도 두 클라가 <b>각자</b> 부른다.
///
/// <para>예전에는 마스터만 <c>NetworkRunner.LoadScene</c> 을 불러 Fusion 이 상대를 끌어오게 했다.
/// 그 통로는 두 가지를 동시에 망가뜨렸다 — (1) 마스터가 먼저 진행하면 아직 로비 절차(덱 잠금 폴링)를
/// 돌고 있던 상대의 화면이 파괴되며 그 취소가 절차를 죽였고, (2) 마스터가 끊기면 상대는 씬을 열 방법
/// 자체가 없어 영영 로비에 갇혔다. 그래서 씬 전환의 주인을 각 클라로 되돌리고 Fusion 씬 동기화는
/// <see cref="NoRemoteSceneSyncManager"/> 로 껐다.</para>
///
/// <para>양쪽 로드 시점이 어긋나는 것은 <c>NetworkGameController.SendSceneReadyAndWaitAsync</c> 의
/// SceneReady 핸드셰이크가 맞춘다 — 씬 로드 자체를 동기화할 이유가 없다.</para></summary>
public static class BattleSceneEntry
{
    /// <summary>등록된 로컬/원격 전투 씬을 연다. 알 수 없는 씬이면 false를 돌려준다.</summary>
    public static bool Load(string _sceneName)
    {
        if (string.IsNullOrEmpty(_sceneName))
        {
            Debug.LogError("[BattleSceneEntry] The battle scene name is empty — cannot enter the battle.");
            return false;
        }
        if (!GameSceneLoadOperation.CanLoad(_sceneName))
        {
            Debug.LogError($"[BattleSceneEntry] '{_sceneName}' is not registered — cannot enter the battle.");
            return false;
        }

        LoadingCoverView.LoadScene(_sceneName);
        return true;
    }
}

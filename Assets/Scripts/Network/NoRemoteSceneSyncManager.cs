using Fusion;
using UnityEngine;

/// <summary>Fusion 의 <b>피어 간 씬 동기화만</b> 끈 씬 매니저.
///
/// <para>기본 <see cref="NetworkSceneManagerDefault"/> 는 한 피어가 씬을 바꾸면 그 지시를 다른 피어에게
/// 실어 보내 같이 로드시킨다. 이 프로젝트는 씬을 <c>SceneManager.LoadScene</c> 으로 각자 직접 로드하고
/// 진입 동기는 <see cref="NetworkGameController"/> 의 SceneReady 핸드셰이크가 따로 맞춘다 — 즉 Fusion 의
/// 씬 동기화는 중복이면서, <b>먼저 진행한 쪽이 늦은 쪽의 로비 절차를 강제 종료시키는</b> 통로였다.
/// 실제 증상: 상대가 덱 잠금 승인을 먼저 받고 전투 씬을 로드하면 이쪽도 끌려가 <c>MatchmakingShell</c> 이
/// 파괴되고, 그 <c>OnDestroy</c> 의 취소가 아직 폴링 중이던 lockDeck 을 죽였다(승인 정원은 이미 찬 채로).</para>
///
/// <para>끄는 게 안전한 근거: 자체 코드에 <c>NetworkObject</c>·<c>NetworkBehaviour</c>·<c>[Networked]</c>·
/// <c>Runner.Spawn</c> 사용이 없다(통신은 전부 ReliableData). 씬에 저작된 네트워크 오브젝트가 없으므로
/// Fusion 이 씬을 알 필요 자체가 없다. 그 전제가 깨지는 날 — 즉 NetworkObject 를 도입하는 날 —
/// 이 클래스를 지우는 게 아니라 <b>씬 전환의 주인을 하나로 정하는</b> 설계부터 다시 해야 한다.</para></summary>
public class NoRemoteSceneSyncManager : NetworkSceneManagerDefault
{
    bool suppressedOnce;

    /// <summary><c>true</c> = "이 변경은 내가 직접 처리했다" — 그리고 아무것도 하지 않는다.
    /// 기본 구현은 <c>false</c>(=Fusion 이 대신 로드)라서 이 한 줄이 동기화의 유일한 차단점이다.</summary>
    public override bool OnSceneInfoChanged(
        NetworkSceneInfo _sceneInfo, NetworkSceneInfoChangeSource _changeSource)
    {
        // 무음으로 삼키면 나중에 "왜 씬이 안 따라오지" 를 코드에서 찾을 방법이 없다. 한 번만 남긴다.
        if (!this.suppressedOnce)
        {
            this.suppressedOnce = true;
            Debug.Log($"[Net] 원격 씬 전환 지시를 무시한다(씬 전환은 각 클라가 소유). source={_changeSource}");
        }
        return true;
    }
}

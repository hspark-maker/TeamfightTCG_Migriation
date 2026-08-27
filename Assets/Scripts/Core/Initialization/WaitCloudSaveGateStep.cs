using Cysharp.Threading.Tasks;
using UnityEngine;

// 여기부터가 "프레임 이후" 단계다. 클라우드 세이브 채택 게이트가 열릴 때까지 기다린다.
// 복구 화면의 재시도도 이 스텝부터 다시 돈다(retryEntry).
public sealed class WaitCloudSaveGateStep : MainInitializer
{
    public override async UniTask Initialize(InitializationContext _context)
    {
        // 러너는 Awake(-210)에서 도므로, 한 프레임 넘겨 형제 매니저의 Awake를 전부 끝낸 뒤에 본다.
        // 이걸 빼면 아래 DataLibrary 검사가 DataLibrary.Awake보다 먼저 서서 헛되이 실패한다.
        await UniTask.Yield(PlayerLoopTiming.Update);

        await UniTask.WaitUntil(() => PlayerSaveCloud.IsGateComplete || GameInitialization.IsTerminated);

        if (GameInitialization.IsTerminated)
        {
            _context.Abort();
            return;
        }

        if (DataLibrary.instance == null)
        {
            Debug.LogError("[WaitCloudSaveGateStep] DataLibrary is missing from the initialization hierarchy.");
            GameInitialization.MarkRecoveryRequired();
            _context.Abort();
        }
    }
}

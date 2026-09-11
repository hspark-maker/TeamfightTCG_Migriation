using Cysharp.Threading.Tasks;
using UnityEngine;

// 튜토리얼 저작물 주입. 로딩 씬이 첫 목적지를 판정하려면 초기화 중에 꽂혀 있어야 한다.
// 주입은 멱등이라 씬 브리지가 같은 에셋을 다시 넣어도 조기 return한다.
public sealed class TutorialDataStep : MainInitializer
{
    // 튜토리얼 스텝 시퀀스 SO.
    [SerializeField] OutgameTutorialData tutorialData;

    public override UniTask Initialize(InitializationContext _context)
    {
        if (!ContentUnlockConfig.TrySetSource(tutorialData != null ? tutorialData.contentUnlocks : null, out string t_error))
        {
            FailToRecovery(_context, new System.InvalidOperationException(t_error));
            return UniTask.CompletedTask;
        }
        OutgameTutorialRunner.EnsureData(tutorialData);
        return UniTask.CompletedTask;
    }
}

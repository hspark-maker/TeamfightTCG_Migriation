using Cysharp.Threading.Tasks;
using UnityEngine;

// 튜토리얼 저작물 주입. 로딩 씬이 첫 목적지를 판정하려면 부트 중에 꽂혀 있어야 한다.
// 주입은 멱등이라 씬 브리지가 같은 에셋을 다시 넣어도 조기 return한다.
public sealed class TutorialDataStep : MainInitializer
{
    // 튜토리얼 스텝 시퀀스 SO.
    [SerializeField] OutgameTutorialData tutorialData;
    // 트리거 발화 튜토리얼 목록 SO(탭 첫 진입 등). 미배선이면 트리거는 조용히 발화하지 않는다.
    [SerializeField] TriggeredTutorialData triggeredTutorialData;

    public override UniTask Initialize(InitializationContext _context)
    {
        OutgameTutorialRunner.EnsureData(tutorialData);
        TriggeredTutorialRunner.EnsureData(triggeredTutorialData);
        return UniTask.CompletedTask;
    }
}

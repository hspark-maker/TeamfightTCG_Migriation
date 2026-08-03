using UnityEngine;

/// <summary>팩 개봉 대기. 걸 앵커가 없어(개봉 팩은 딤을 못 뚫는다) 배너만 띄우고 개봉 결과로 완료된다.</summary>
[CreateAssetMenu(fileName = "Step_WaitPackOpen", menuName = "Card Battle/Outgame Tutorial/Step/Wait Pack Open")]
public class WaitPackOpenStep : OutgameTutorialStep
{
    public override EOutgameTutorialCompletion Completion => EOutgameTutorialCompletion.PackOpen;

    public override bool Enter(OutgameTutorialStepContext _context) => true;
}

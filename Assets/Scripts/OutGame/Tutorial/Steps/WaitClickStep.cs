using UnityEngine;

/// <summary>앵커 버튼 클릭 대기. 클릭이 곧 완료다(눌러서 실패할 여지가 없는 이동·획득 버튼용).</summary>
[CreateAssetMenu(fileName = "Step_WaitClick", menuName = "Card Battle/Outgame Tutorial/Step/Wait Click")]
public class WaitClickStep : OutgameTutorialStep
{
    [Tooltip("클릭을 기다릴 안내 타깃 위젯")]
    [SerializeField] EOutgameTutorialAnchor anchor;

    public override EOutgameTutorialAnchor Anchor => anchor;
    public override EOutgameTutorialCompletion Completion => EOutgameTutorialCompletion.Click;

    // 진입 시 할 일이 없다 — 게이트를 걸고 클릭을 기다리는 게 전부.
    public override bool Enter(OutgameTutorialStepContext _context) => true;
}

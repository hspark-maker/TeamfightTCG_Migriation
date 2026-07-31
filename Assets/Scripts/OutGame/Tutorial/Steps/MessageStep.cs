using UnityEngine;

/// <summary>화면 탭으로 넘기는 설명 스텝. 누를 대상이 아니라 읽을 영역이라 앵커는 옵션이고 Button도 필요 없다.</summary>
[CreateAssetMenu(fileName = "Step_Message", menuName = "Card Battle/Outgame Tutorial/Step/Message")]
public class MessageStep : OutgameTutorialStep
{
    [Tooltip("강조할 영역(옵션). None이면 문구만 띄운다")]
    [SerializeField] EOutgameTutorialAnchor anchor;

    public override EOutgameTutorialAnchor Anchor => anchor;
    public override EOutgameTutorialCompletion Completion => EOutgameTutorialCompletion.Confirm;

    // 진입 시 할 일이 없다 — 문구를 띄우고 탭을 기다리는 게 전부.
    public override bool Enter(OutgameTutorialStepContext _context) => true;
}

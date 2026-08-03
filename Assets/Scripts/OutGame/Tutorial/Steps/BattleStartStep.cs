using UnityEngine;

/// <summary>덱 확인 화면의 전투 시작 버튼 클릭 대기. 클릭이 곧 전투 개시라 이 씬의 안내는 여기서 끝난다.
/// 씬은 그대로지만 전투가 화면을 넘겨받으므로 브리지 입장에서는 씬을 떠난 것과 같다(LeavesScene=true).</summary>
[CreateAssetMenu(fileName = "Step_BattleStart", menuName = "Card Battle/Outgame Tutorial/Step/Battle Start")]
public class BattleStartStep : OutgameTutorialStep
{
    [Tooltip("클릭을 기다릴 전투 시작 버튼")]
    [SerializeField] EOutgameTutorialAnchor anchor = EOutgameTutorialAnchor.MatchDeckBattleButton;

    public override EOutgameTutorialAnchor Anchor => anchor;

    // 눌러서 실패할 여지가 없다 — 버튼 자체가 유효 덱일 때만 interactable이고(MatchDeckPanelView.RenderMySlots),
    // 게이트는 못 누르는 타깃 앞에서 딤을 걷고 대기한다. 그래서 구매 스텝처럼 별도 성공 신호를 두지 않는다.
    public override EOutgameTutorialCompletion Completion => EOutgameTutorialCompletion.Click;

    public override bool LeavesScene => true;

    // 진입 시 할 일이 없다 — 시나리오는 이 씬에 넣어 준 스텝(BattleEntryStep/AutoBattleStep)이 이미 주입했다.
    public override bool Enter(OutgameTutorialStepContext _context) => true;
}

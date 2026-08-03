using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>로비를 거치지 않고 곧장 전투로 넣는 스텝(첫 전투). 입력이 없어 게이트도 없다.</summary>
[CreateAssetMenu(fileName = "Step_AutoBattle", menuName = "Card Battle/Outgame Tutorial/Step/Auto Battle")]
public class AutoBattleStep : OutgameTutorialStep
{
    // 전투 씬. 저작 데이터가 아니라 시스템 고정 경로라 SO 필드가 아닌 상수로 둔다.
    const string BattleScene = "BattleScene";

    [Tooltip("전투에 넘길 튜토리얼 시나리오")]
    [SerializeField] TutorialScenarioData scenario;

    [Tooltip("전투 전 덱 확인/편집 화면(MatchDeckRoot)을 띄운다. 전투 덱은 켜든 끄든 시나리오 고정이다.\n"
           + "첫 전투는 저장된 덱이 없어 화면에서 전투를 시작할 수 없으므로 반드시 꺼 둔다.")]
    [SerializeField] bool showDeckGate;

    public override EOutgameTutorialCompletion Completion => EOutgameTutorialCompletion.Auto;
    public override bool LeavesScene => true;

    public override bool Enter(OutgameTutorialStepContext _context)
    {
        // AutoPurchase와 같은 불변식: 커밋이 실행보다 앞선다. 씬을 떠나면 되돌릴 지점이 없어,
        // 커밋을 미루면 전투 중 강제종료가 이 스텝을 영원히 되풀이한다.
        _context.CommitAdvance();
        _context.CompleteIfLast();

        if (scenario == null)
            Debug.LogWarning($"[AutoBattleStep] 스텝 {_context.ChapterIndex}-{_context.StepIndex}('{name}')에 시나리오가 미배선 — 일반 전투로 진입합니다.");

        // 양 덱은 TutorialConfig가 고정 주입한다(GameInitializer) → 저장 덱이 없는 첫 실행도 그대로 진입 가능.
        TutorialConfig.Begin(scenario, showDeckGate);
        SceneManager.LoadScene(BattleScene);
        return false;
    }
}

using UnityEngine;

/// <summary>앵커 클릭으로 전투에 진입하는 스텝. 클릭이 곧 씬 전환이라 다음 스텝은 전투 후 씬의 브리지가 재개한다.</summary>
[CreateAssetMenu(fileName = "Step_BattleEntry", menuName = "Card Battle/Outgame Tutorial/Step/Battle Entry")]
public class BattleEntryStep : OutgameTutorialStep
{
    [Tooltip("클릭을 기다릴 전투 시작 버튼")]
    [SerializeField] EOutgameTutorialAnchor anchor;

    [Tooltip("전투에 넘길 튜토리얼 시나리오")]
    [SerializeField] TutorialScenarioData scenario;

    public override EOutgameTutorialAnchor Anchor => anchor;
    public override EOutgameTutorialCompletion Completion => EOutgameTutorialCompletion.Click;
    public override bool LeavesScene => true;

    public override bool Enter(OutgameTutorialStepContext _context)
    {
        // 클릭 리스너가 아니라 진입 시 미리 시작한다 — PlayBtn의 씬 PersistentCall(StartAiBattle)이
        // 런타임 리스너보다 먼저 LoadScene을 돌려 순서 의존이 생기기 때문. Begin은 멱등이라 재진입도 안전.
        if (scenario == null)
            Debug.LogWarning($"[BattleEntryStep] 스텝 {_context.Index}('{name}')에 시나리오가 미배선 — 일반 전투로 진입합니다.");

        TutorialConfig.Begin(scenario);
        return true;
    }
}

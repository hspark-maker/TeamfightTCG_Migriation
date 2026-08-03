using UnityEngine;

/// <summary>앵커 클릭으로 전투에 진입하는 스텝. 클릭이 곧 씬 전환이라 다음 스텝은 전투 후 씬의 브리지가 재개한다.</summary>
[CreateAssetMenu(fileName = "Step_BattleEntry", menuName = "Card Battle/Outgame Tutorial/Step/Battle Entry")]
public class BattleEntryStep : OutgameTutorialStep
{
    [Tooltip("클릭을 기다릴 전투 시작 버튼")]
    [SerializeField] EOutgameTutorialAnchor anchor;

    [Tooltip("전투에 넘길 튜토리얼 시나리오")]
    [SerializeField] TutorialScenarioData scenario;

    [Tooltip("전투 전 덱 확인/편집 화면(로비 오버레이 MatchDeckRoot)을 띄운다. 전투 덱은 켜든 끄든 시나리오 고정이다.\n"
           + "저장된 유효 덱이 없으면 이 화면에서 전투를 시작할 수 없으니, 덱이 생긴 뒤 챕터에만 켠다.")]
    [SerializeField] bool showDeckGate;

    public override EOutgameTutorialAnchor Anchor => anchor;
    public override EOutgameTutorialCompletion Completion => EOutgameTutorialCompletion.Click;

    // 게이트를 켜면 클릭이 로비 오버레이를 열 뿐이라 씬이 그대로다 — 다음 스텝(덱 화면 앵커)을
    // 같은 브리지가 이어가야 한다. 끄면 클릭이 곧 씬 전환이다.
    public override bool LeavesScene => !showDeckGate;

    public override bool Enter(OutgameTutorialStepContext _context)
    {
        // 클릭 리스너가 아니라 진입 시 미리 시작한다 — PlayBtn의 씬 PersistentCall(StartAiBattle)이
        // 런타임 리스너보다 먼저 돌기 때문. 그 진입점이 여기서 세운 ShowDeckGate·EnemyDeck을 읽으므로
        // 클릭 시점엔 이미 채워져 있어야 한다. Begin은 멱등이라 재진입도 안전.
        if (scenario == null)
            Debug.LogWarning($"[BattleEntryStep] 스텝 {_context.ChapterIndex}-{_context.StepIndex}('{name}')에 시나리오가 미배선 — 일반 전투로 진입합니다.");

        TutorialConfig.Begin(scenario, showDeckGate);
        return true;
    }
}

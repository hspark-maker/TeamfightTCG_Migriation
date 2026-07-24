using System.Collections.Generic;
using ScriptedAttack = TutorialScenarioData.ScriptedAttack;

/// <summary>
/// 튜토리얼 런타임 단일 진실원. 싱글 경로 전용(멀티 미접촉).
/// 활성 시: 덱 무셔플 고정순서(양측, BattleField.Initialize) + 공격이 스크립트 스텝을 따른다
/// (PlayerTurn 입력 게이트 / EnemyTurn 선택). 전투 규칙(CardInstance/AttackProcessor)은 무수정 —
/// 결정 지점 3곳(셔플·입력·적선택)만 대체한다.
///
/// 스텝 소비 모델: 공격 1건 = 스텝 1개. 일반 전투는 턴당 1공격이므로 스텝 1개가 곧 그 턴.
/// 처형(canAttackAgain) 재공격 시에는 같은 턴에 다음 스텝을 연속 소비한다.
/// 큐 소진 후 플레이어 입력은 무시(디자이너가 턴을 끝낼 스텝을 반드시 저작해야 함),
/// 적 턴은 즉시 종료된다.
/// </summary>
public static class TutorialConfig
{
    public static bool IsActive { get; private set; }

    /// <summary>고정 플레이어 덱(순서 = 등장 순서). 셔플 없음.</summary>
    public static List<CardData> PlayerDeck { get; private set; }
    /// <summary>고정 적 덱(순서 = 등장 순서). 셔플 없음.</summary>
    public static List<CardData> EnemyDeck { get; private set; }

    static Queue<ScriptedAttack> playerScript;
    static Queue<ScriptedAttack> enemyScript;

    public static void Begin(TutorialScenarioData _scenario)
    {
        if (_scenario == null) { End(); return; }
        Begin(_scenario.playerDeck, _scenario.enemyDeck, _scenario.playerScript, _scenario.enemyScript);
    }

    /// <summary>SO 없이 리스트로 직접 시작(셋업 씬 인스펙터 저작용).</summary>
    public static void Begin(List<CardData> _playerDeck, List<CardData> _enemyDeck,
                             List<ScriptedAttack> _playerScript, List<ScriptedAttack> _enemyScript)
    {
        IsActive = true;
        DeckConfig.SetMultiplayer(false);   // 튜토리얼은 항상 싱글 경로
        PlayerDeck   = new List<CardData>(_playerDeck ?? new List<CardData>());
        EnemyDeck    = new List<CardData>(_enemyDeck  ?? new List<CardData>());
        playerScript = new Queue<ScriptedAttack>(_playerScript ?? new List<ScriptedAttack>());
        enemyScript  = new Queue<ScriptedAttack>(_enemyScript  ?? new List<ScriptedAttack>());
    }

    public static void End()
    {
        IsActive     = false;
        PlayerDeck   = null;
        EnemyDeck    = null;
        playerScript = null;
        enemyScript  = null;
    }

    /// <summary>플레이어 현재 스텝 조회(소비 안 함). 입력 게이트 판정용.</summary>
    public static bool TryPeekPlayerStep(out ScriptedAttack _step)
    {
        if (IsActive && playerScript != null && playerScript.Count > 0)
        {
            _step = playerScript.Peek();
            return true;
        }
        _step = default;
        return false;
    }

    /// <summary>플레이어 스텝 소비. 공격이 실제 확정될 때 호출.</summary>
    public static void ConsumePlayerStep()
    {
        if (IsActive && playerScript != null && playerScript.Count > 0)
            playerScript.Dequeue();
    }

    /// <summary>적 다음 스텝 소비(dequeue). 적 공격 선택용.</summary>
    public static bool TryNextEnemyStep(out ScriptedAttack _step)
    {
        if (IsActive && enemyScript != null && enemyScript.Count > 0)
        {
            _step = enemyScript.Dequeue();
            return true;
        }
        _step = default;
        return false;
    }
}

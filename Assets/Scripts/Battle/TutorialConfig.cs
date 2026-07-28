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

    /// <summary>이 튜토리얼에서 시너지 배지 표시 + 덱 시너지 적용 여부. 기본 false(초반 튜토리얼).</summary>
    public static bool SynergyEnabled { get; private set; }

    /// <summary>스크립트 큐 소진 후 플레이어 자유 공격 전환 여부. 기본 false(디자이너가 턴 종료 스텝 저작).</summary>
    public static bool FreePlayAfterScript { get; private set; }

    /// <summary>확정승용 적 체력 오버라이드(>0이면 적 카드 체력을 이 값 이하로). 0=off.</summary>
    public static int EnemyMaxHpOverride { get; private set; }

    /// <summary>플레이어 스텝을 무효로 폐기한 적이 있는가(= 스크립트가 실제 보드와 어긋났다).
    /// 폐기로 큐가 조기에 비면 안내도 없이 턴이 잠기므로, 이 경우 자유 플레이로 빠진다(PlayerTurn).</summary>
    public static bool ScriptDerailed { get; private set; }

    /// <summary>고정 플레이어 덱(순서 = 등장 순서). 셔플 없음.</summary>
    public static List<CardData> PlayerDeck { get; private set; }
    /// <summary>고정 적 덱(순서 = 등장 순서). 셔플 없음.</summary>
    public static List<CardData> EnemyDeck { get; private set; }

    static Queue<ScriptedAttack> playerScript;
    static Queue<ScriptedAttack> enemyScript;

    // 스크립트 기준선 = "이 슬롯엔 이 카드가 있을 것"이라는 스크립트의 기대(진영별 슬롯 점유 CardData).
    // 스텝이 슬롯 인덱스로만 저작되므로, 카드가 죽고 그 자리를 대기 카드가 채우면 슬롯 지정이 엉뚱한
    // 카드에 붙는다. 이를 막는 유일한 식별 수단. 재동기 지점은 SyncBoardBaseline 주석 참조.
    static CardData[] playerBaseline;
    static CardData[] enemyBaseline;

    public static void Begin(TutorialScenarioData _scenario)
    {
        if (_scenario == null) { End(); return; }
        Begin(_scenario.playerDeck, _scenario.enemyDeck, _scenario.playerScript, _scenario.enemyScript,
              _scenario.enableSynergy, _scenario.freePlayAfterScript);
        EnemyMaxHpOverride = _scenario.enemyMaxHpOverride;   // 리스트 Begin이 0으로 리셋한 뒤 시나리오 값 반영.
    }

    /// <summary>SO 없이 리스트로 직접 시작(셋업 씬 인스펙터 저작용).</summary>
    public static void Begin(List<CardData> _playerDeck, List<CardData> _enemyDeck,
                             List<ScriptedAttack> _playerScript, List<ScriptedAttack> _enemyScript,
                             bool _enableSynergy = false, bool _freePlayAfterScript = false)
    {
        IsActive = true;
        SynergyEnabled = _enableSynergy;
        FreePlayAfterScript = _freePlayAfterScript;
        EnemyMaxHpOverride = 0;   // 리스트 직접 시작 기본값(시나리오 Begin이 이후 덮어씀).
        ScriptDerailed = false;
        playerBaseline = null;
        enemyBaseline  = null;
        DeckConfig.SetMultiplayer(false);   // 튜토리얼은 항상 싱글 경로
        PlayerDeck   = new List<CardData>(_playerDeck ?? new List<CardData>());
        EnemyDeck    = new List<CardData>(_enemyDeck  ?? new List<CardData>());
        playerScript = new Queue<ScriptedAttack>(_playerScript ?? new List<ScriptedAttack>());
        enemyScript  = new Queue<ScriptedAttack>(_enemyScript  ?? new List<ScriptedAttack>());
    }

    public static void End()
    {
        IsActive       = false;
        SynergyEnabled = false;
        FreePlayAfterScript = false;
        EnemyMaxHpOverride = 0;
        ScriptDerailed = false;
        PlayerDeck   = null;
        EnemyDeck    = null;
        playerScript = null;
        enemyScript  = null;
        playerBaseline = null;
        enemyBaseline  = null;
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

    /// <summary>플레이어 스텝 큐에서 앞에서 _offset번째 스텝 조회(소비 안 함). 0 = 현재 스텝.
    /// 안내 묶음(선행 Message/Inspect) 뒤의 공격 스텝을 미리 보고 실행 가능성을 판정할 때 쓴다.</summary>
    public static bool TryPeekPlayerStep(int _offset, out ScriptedAttack _step)
        => TryPeekAt(playerScript, _offset, out _step);

    /// <summary>플레이어 스텝 소비. 공격이 실제 확정될 때 호출.</summary>
    public static void ConsumePlayerStep()
    {
        if (IsActive && playerScript != null && playerScript.Count > 0)
            playerScript.Dequeue();
    }

    /// <summary>플레이어 스텝을 무효로 폐기(조용히 버림). 정상 소비와 구분해 ScriptDerailed를 남긴다 —
    /// 폐기로 스크립트가 끊긴 뒤엔 안내 없는 턴 잠김 대신 자유 플레이로 빠져야 하므로.</summary>
    public static void DiscardPlayerStep()
    {
        if (!IsActive || playerScript == null || playerScript.Count == 0) return;
        playerScript.Dequeue();
        ScriptDerailed = true;
    }

    /// <summary>적 현재 스텝 조회(소비 안 함). 스텝 종류(Attack/Message) 판정용.</summary>
    public static bool TryPeekEnemyStep(out ScriptedAttack _step)
    {
        if (IsActive && enemyScript != null && enemyScript.Count > 0)
        {
            _step = enemyScript.Peek();
            return true;
        }
        _step = default;
        return false;
    }

    /// <summary>적 스텝 큐에서 앞에서 _offset번째 스텝 조회(소비 안 함). 0 = 현재 스텝.</summary>
    public static bool TryPeekEnemyStep(int _offset, out ScriptedAttack _step)
        => TryPeekAt(enemyScript, _offset, out _step);

    /// <summary>적 스텝 소비(dequeue). 스텝 처리 완료 시 호출.</summary>
    public static void ConsumeEnemyStep()
    {
        if (IsActive && enemyScript != null && enemyScript.Count > 0)
            enemyScript.Dequeue();
    }

    /// <summary>적 스텝을 무효로 폐기(조용히 버림). 적 스크립트가 끊기면 일반 AI로 폴백되므로
    /// ScriptDerailed(플레이어 자유 플레이 전환용)는 세우지 않는다.</summary>
    public static void DiscardEnemyStep() => ConsumeEnemyStep();

    /// <summary>
    /// 스크립트 기준선 재동기 = "지금 보드가 곧 스크립트가 기대하는 보드다".
    /// 호출 지점은 전투 시작 직후와 <b>슬롯 지정 스텝대로 끝난 공격</b> 직후(빈 슬롯 보충 뒤)뿐이다.
    /// 자유공격·자유플레이·AI 폴백이 만든 보드 변화는 일부러 반영하지 않는다 —
    /// 그래야 뒤 스텝이 "내가 가리키던 카드가 아니다"를 알아채고 스스로 폐기된다.
    /// </summary>
    public static void SyncBoardBaseline(BattleField _playerField, BattleField _enemyField)
    {
        if (!IsActive) return;
        playerBaseline = CaptureBaseline(_playerField);
        enemyBaseline  = CaptureBaseline(_enemyField);
    }

    /// <summary>플레이어 슬롯 점유 카드가 스크립트가 기대한 카드인가(기준선 없으면 판정 보류=true).</summary>
    public static bool MatchesPlayerBaseline(int _slot, CardInstance _card)
        => MatchesBaseline(playerBaseline, _slot, _card);

    /// <summary>적 슬롯 점유 카드가 스크립트가 기대한 카드인가(기준선 없으면 판정 보류=true).</summary>
    public static bool MatchesEnemyBaseline(int _slot, CardInstance _card)
        => MatchesBaseline(enemyBaseline, _slot, _card);

    static bool TryPeekAt(Queue<ScriptedAttack> _queue, int _offset, out ScriptedAttack _step)
    {
        _step = default;
        if (!IsActive || _queue == null || _offset < 0 || _offset >= _queue.Count) return false;

        int t_i = 0;
        foreach (ScriptedAttack t_s in _queue)   // Queue<T> 열거 순서 = dequeue 순서
        {
            if (t_i++ != _offset) continue;
            _step = t_s;
            return true;
        }
        return false;
    }

    static CardData[] CaptureBaseline(BattleField _field)
    {
        var t_slots = new CardData[BattleField.SLOT_COUNT];
        if (_field == null) return t_slots;

        for (int i = 0; i < BattleField.SLOT_COUNT; i++)
        {
            CardInstance t_card = _field.GetSlot(i);
            t_slots[i] = t_card != null && t_card.IsAlive ? t_card.data : null;
        }
        return t_slots;
    }

    // 인스턴스가 아닌 CardData로 대조한다 — 같은 카드가 다시 등장한 경우(덱 중복)는 안내 문구가
    // 여전히 맞으므로 통과시키고, 다른 카드가 자리를 채운 경우만 걸러낸다.
    static bool MatchesBaseline(CardData[] _baseline, int _slot, CardInstance _card)
    {
        if (_baseline == null || _slot < 0 || _slot >= _baseline.Length) return true;
        if (_baseline[_slot] == null) return true;   // 기대 카드 미상 = 판정 보류
        return _card != null && _card.data == _baseline[_slot];
    }
}

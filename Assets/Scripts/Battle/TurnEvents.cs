using System;

/// <summary>
/// 턴 시스템 이벤트 허브 (Observer subject).
/// 발행: TurnRunner. 구독: HealerEffect, 턴 UI 등.
/// 구독자는 발행자(TurnRunner) 구체 타입이 아니라 이 허브에만 의존한다.
/// 씬 종료 시 Reset()으로 구독 정리 (TurnRunner.Cleanup에서 호출).
/// </summary>
public static class TurnEvents
{
    /// <summary>턴 시작. 인자: 이번 턴 행동 주체 field.</summary>
    public static event Action<BattleField> TurnStarted;

    /// <summary>턴 수 변경. 인자: 새 턴 수.</summary>
    public static event Action<int> TurnCountChanged;

    // 이벤트는 선언 클래스에서만 Invoke 가능 → 발행자용 Raise 메서드 제공.
    public static void RaiseTurnStarted(BattleField _field) => TurnStarted?.Invoke(_field);
    public static void RaiseTurnCountChanged(int _count)    => TurnCountChanged?.Invoke(_count);

    public static void Reset()
    {
        TurnStarted      = null;
        TurnCountChanged = null;
    }
}

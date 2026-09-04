using System;

/// <summary>
/// 순수 규칙이 선택적인 Unity 관측자에게 결과를 알리는 경계.
/// 서버 재생기에서는 주입하지 않으며 규칙 결과에는 영향을 주지 않는다.
/// </summary>
public static class BattleRuleBridge
{
    public static Action<BattleFieldState, BattleFieldState> ArmFinisher;
    public static Action<int, int, int, bool, bool> RecordAttack;
    public static Action<string> LogError;

    public static void Reset()
    {
        ArmFinisher = null;
        RecordAttack = null;
        LogError = null;
    }
}

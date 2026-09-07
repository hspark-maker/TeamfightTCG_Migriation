using UnityEngine;

/// <summary>BattleCore의 선택적 관측 seam을 현재 Unity 표현·로그 구현에 연결한다.</summary>
public static class BattleRuleUnityBridge
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Install()
    {
        BattleRuleBridge.Reset();
        BattleRuleBridge.ArmFinisher = BattleFinisher.Arm;
        BattleRuleBridge.RecordAttack = BattleCommandLog.RecordAttack;
        BattleRuleBridge.LogError = Debug.LogError;
    }
}

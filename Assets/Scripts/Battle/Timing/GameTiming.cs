using UnityEngine;

/// <summary>
/// 연출 타이밍 전역 접근점 + 속도 배율 단일 진입점.
/// SO(BattleTimingConfig)가 raw 값을 들고, 노출 프로퍼티가 배율을 적용한다.
/// 정적 소비자(AttackSequence/CardView.FadeView 등)도 쓰므로 static facade.
/// SO 미배선 시 기본 인스턴스 fallback → 씬 배선 없이도 동작(배선은 선택적 튜닝).
/// </summary>
public static class GameTiming
{
    // 배율: 높을수록 빠름(지속시간 짧아짐). 하한 클램프 필수(0이면 애니 미완성 진행).
    const float MIN_SPEED = 0.2f;
    const float MAX_SPEED = 5f;

    static float s_speed = 1f;

    public static float Speed
    {
        get => s_speed;
        set => s_speed = Mathf.Clamp(value, MIN_SPEED, MAX_SPEED);
    }

    /// <summary>지속시간에 곱하는 계수. Speed 2배 = Factor 0.5배(절반 시간).</summary>
    public static float Factor => 1f / s_speed;

    static BattleTimingConfig s_battle;

    public static BattleTimingConfig Battle
        => s_battle != null ? s_battle : (s_battle = ScriptableObject.CreateInstance<BattleTimingConfig>());

    /// <summary>DataLibrary 등 부트스트랩에서 실제 애셋 주입(선택). null이면 기본 유지.</summary>
    public static void SetConfig(BattleTimingConfig _battle)
    {
        if (_battle != null) s_battle = _battle;
    }
}

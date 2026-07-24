using System;

/// <summary>
/// 아웃게임 시각 단일 창구. "현재 시각" 읽기를 여기로 모아, 도감 오프라인 생산 등이
/// (마지막 정산 시각, 현재 시각) → 생산량을 계산할 때 기준 시각을 하나로 통일한다.
/// 디버그로 시간을 앞으로 점프시키면 앱 재시작 없이 오프라인 생산·상한·수확을 검증할 수 있다.
/// 시각은 UTC 고정(로컬 타임존·서머타임 비의존).
/// </summary>
public static class GameClock
{
    // 디버그용 누적 오프셋. 메모리 전용 — 세이브에 저장하지 않는다.
    static TimeSpan s_debugOffset = TimeSpan.Zero;

    /// <summary>현재 UTC 시각. 모든 아웃게임 시각 읽기는 이 창구를 거친다.</summary>
    public static DateTime UtcNow => DateTime.UtcNow + s_debugOffset;

    /// <summary>_sinceUtc 이후 경과 시간. 시계 역행 시 음수 없이 0으로 클램프한다(역행 규칙 단일 지점).</summary>
    public static TimeSpan Since(DateTime _sinceUtc)
    {
        var t_elapsed = UtcNow - _sinceUtc;
        return t_elapsed < TimeSpan.Zero ? TimeSpan.Zero : t_elapsed;
    }

    /// <summary>디버그: 시간을 앞으로 점프시킨다(누적). 오프라인 생산 검증용.</summary>
    public static void DebugAdvance(TimeSpan _delta) => s_debugOffset += _delta;

    /// <summary>디버그: 시간 오프셋을 초기화한다.</summary>
    public static void DebugReset() => s_debugOffset = TimeSpan.Zero;
}

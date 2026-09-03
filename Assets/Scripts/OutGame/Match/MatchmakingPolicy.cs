/// <summary>사람 상대를 찾는 시간과 티어 범위 확장 박자. 탐색 한 번마다 <see cref="MatchmakingPolicy.Roll"/>로
/// 총 시간을 뽑아 그 값을 끝까지 들고 다닌다 — 판정마다 새로 뽑으면 같은 탐색 안에서 기준이 흔들린다.</summary>
public readonly struct MatchmakingWindow
{
    /// <summary>이 탐색의 총 시간. 넘기면 AI 로 넘어간다.</summary>
    public readonly float SearchSeconds;

    /// <summary>티어 범위를 한 단계 넓히는 간격. 총 시간을 4등분한 값이라
    /// 총 시간이 바뀌어도 확장 박자가 같은 비율로 따라온다.</summary>
    float StageSeconds => this.SearchSeconds / 4f;

    public MatchmakingWindow(float _searchSeconds) => this.SearchSeconds = _searchSeconds;

    public int TierWindow(float _elapsedSeconds)
    {
        float t_stage = this.StageSeconds;
        if (_elapsedSeconds < t_stage) return 1;
        if (_elapsedSeconds < t_stage * 2f) return 3;
        if (_elapsedSeconds < t_stage * 3f) return 5;
        return int.MaxValue;
    }

    public float SecondsUntilNextStage(float _elapsedSeconds)
    {
        float t_stage = this.StageSeconds;
        float t_next = (UnityEngine.Mathf.Floor(_elapsedSeconds / t_stage) + 1f) * t_stage;
        return UnityEngine.Mathf.Min(t_stage, UnityEngine.Mathf.Max(0f, t_next - _elapsedSeconds));
    }
}

public static class MatchmakingPolicy
{
    /// <summary>탐색 총 시간의 하한·상한(초). 매번 같은 초에 AI로 넘어가면 짜인 티가 나서 구간으로 흔든다.</summary>
    public const float MinSearchSeconds = 8f;
    public const float MaxSearchSeconds = 10f;

    /// <summary>탐색 시작에서 한 번만 부른다. 전투 결정론과 무관한 축이라 UnityEngine.Random 을 쓴다
    /// (전투 RNG는 MatchRandom 이 따로 쥔다).</summary>
    public static MatchmakingWindow Roll()
        => new MatchmakingWindow(UnityEngine.Random.Range(MinSearchSeconds, MaxSearchSeconds));
}

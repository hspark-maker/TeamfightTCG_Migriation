public static class MatchmakingPolicy
{
    public const float SearchSeconds = 20f;
    const float StageSeconds = 5f;

    public static int TierWindow(float _elapsedSeconds)
    {
        if (_elapsedSeconds < StageSeconds) return 1;
        if (_elapsedSeconds < StageSeconds * 2f) return 3;
        if (_elapsedSeconds < StageSeconds * 3f) return 5;
        return int.MaxValue;
    }

    public static float SecondsUntilNextStage(float _elapsedSeconds)
    {
        float t_next = (UnityEngine.Mathf.Floor(_elapsedSeconds / StageSeconds) + 1f) * StageSeconds;
        return UnityEngine.Mathf.Min(StageSeconds, UnityEngine.Mathf.Max(0f, t_next - _elapsedSeconds));
    }
}

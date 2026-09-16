/// <summary>이 기기에서 졸업한 계정의 프리뷰 사용법 안내 이력.</summary>
internal static class GuideMissionHintHistory
{
    const string PREF_KEY = "outgame.guideMission.previewHint.";

    internal static bool IsPending => !string.IsNullOrEmpty(FirebaseAuthService.Instance.UserId)
        && LocalPrefs.GetInt(PREF_KEY + FirebaseAuthService.Instance.UserId, 0) == 1;

    internal static void Arm() => Set(1);
    internal static void Consume() => Set(2);

    internal static void Clear()
    {
        string t_uid = FirebaseAuthService.Instance.UserId;
        if (string.IsNullOrEmpty(t_uid)) return;
        LocalPrefs.DeleteKey(PREF_KEY + t_uid);
        LocalPrefs.Save();
    }

    static void Set(int _state)
    {
        string t_uid = FirebaseAuthService.Instance.UserId;
        if (string.IsNullOrEmpty(t_uid)) return;
        LocalPrefs.SetInt(PREF_KEY + t_uid, _state);
        LocalPrefs.Save();
    }
}

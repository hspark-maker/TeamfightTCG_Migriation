using System;
using UnityEngine;

public enum ESaveSourceMode
{
    Local = 0,
    Cloud = 1,
}

/// <summary>세이브 진실원이 로컬 파일인지 Firestore인지. 분기점이 이 한 창구만 본다.</summary>
public static class SaveSourceMode
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// <summary>개발용 오버라이드 키. 값이 없거나 해석 불가면 콘텐츠 프로필 값으로 떨어진다.</summary>
    public const string OverrideKey = "save.sourceMode";
#endif

    /// <summary>현재 세이브 진실원.</summary>
    public static ESaveSourceMode Current
    {
        get
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // 빌드를 다시 굽지 않고 클라우드 경로를 검증하기 위한 통로. 릴리스는 SO 값만 본다.
            string t_override = PlayerPrefs.GetString(OverrideKey, string.Empty);
            if (!string.IsNullOrEmpty(t_override)
                && Enum.TryParse(t_override, true, out ESaveSourceMode t_parsed))
                return t_parsed;
#endif
            return ResolveFromProfile();
        }
    }

    static ESaveSourceMode ResolveFromProfile()
    {
        // 부트 최이른 시점에도 불릴 수 있는 창구라, 프로필 로드 실패로 앱을 죽이는 대신 현행 동작(Local)으로 떨어진다.
        try
        {
            return ContentProfileConfig.Active.SaveSourceMode;
        }
        catch (Exception t_ex)
        {
            Debug.LogWarning($"[SaveSourceMode] 콘텐츠 프로필을 읽지 못해 Local로 폴백한다: {t_ex.Message}");
            return ESaveSourceMode.Local;
        }
    }
}

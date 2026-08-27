using System;
using UnityEngine;

public enum EContentRunMode
{
    Live = 0,
    Test = 1,
}

[CreateAssetMenu(fileName = "ContentProfile", menuName = "Card Battle/Content Profile")]
public class ContentProfileConfig : ScriptableObject
{
    const string LIVE_RESOURCE_PATH = "ContentProfiles/Live";
    const string TEST_RESOURCE_PATH = "ContentProfiles/Test";

#if UNITY_EDITOR
    public const string EditorRunModeKey = "ContentProfile.RunMode";
#endif

    static ContentProfileConfig s_active;

    [SerializeField] EContentRunMode runMode;
    [SerializeField] bool includeTestCards;
    [SerializeField] string saveFolder = "Save";

    // 릴리스 빌드에서는 아래 게터가 두 필드를 읽지 않아 CS0414 가 난다. 저작값은 살려 둬야 하므로 경고만 끈다.
#pragma warning disable CS0414
    [Tooltip("켜면 서버 검증 호출이 배포된 Cloud Functions 대신 로컬 에뮬레이터로 갑니다.\n" +
             "테스트 프로파일 + 에디터/개발 빌드에서만 효과가 있습니다. 릴리스 빌드에서는 이 값과 무관하게 항상 꺼진 것으로 취급됩니다.")]
    [SerializeField] bool useFunctionsEmulator;

    [Tooltip("Functions 에뮬레이터 주소입니다. `firebase emulators:start` 가 출력하는 functions 호스트/포트를 그대로 적습니다.\n" +
             "기본값은 firebase-tools 의 기본 포트입니다. 비워 두면 에뮬레이터를 켜 두었더라도 배포된 함수로 갑니다.")]
    [SerializeField] string functionsEmulatorOrigin = "http://127.0.0.1:5001";

#pragma warning restore CS0414

    public EContentRunMode RunMode => this.runMode;
    public bool IncludeTestCards => this.includeTestCards;
    public string SaveFolder => this.saveFolder;
    public string CloudEnvId => this.runMode == EContentRunMode.Test ? "test" : "live";

    /// <summary>Functions 에뮬레이터 주소. 배포된 함수를 써야 하는 상황에서는 빈 문자열이다.</summary>
    public string FunctionsEmulatorOrigin
    {
        get
        {
            // 릴리스 빌드가 localhost를 가리킬 경로 자체를 타입 수준에서 없앤다 — 저작 실수 하나로 라이브가 죽지 않게 한다.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (this.runMode != EContentRunMode.Test) return string.Empty;
            if (!this.useFunctionsEmulator) return string.Empty;
            return string.IsNullOrWhiteSpace(this.functionsEmulatorOrigin)
                ? string.Empty
                : this.functionsEmulatorOrigin.Trim();
#else
            return string.Empty;
#endif
        }
    }

    public static ContentProfileConfig Active
    {
        get
        {
            EContentRunMode t_mode = ResolveRunMode();
            if (s_active != null && s_active.runMode == t_mode) return s_active;

            string t_path = t_mode == EContentRunMode.Test ? TEST_RESOURCE_PATH : LIVE_RESOURCE_PATH;
            s_active = Resources.Load<ContentProfileConfig>(t_path);
            if (s_active == null)
                throw new InvalidOperationException($"ContentProfileConfig 리소스를 찾을 수 없습니다: {t_path}");

            return s_active;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetActive() => s_active = null;

    static EContentRunMode ResolveRunMode()
    {
#if UNITY_EDITOR
        return (EContentRunMode)UnityEditor.EditorPrefs.GetInt(
            EditorRunModeKey, (int)EContentRunMode.Test);
#elif DEVELOPMENT_BUILD
        return EContentRunMode.Test;
#else
        return EContentRunMode.Live;
#endif
    }
}

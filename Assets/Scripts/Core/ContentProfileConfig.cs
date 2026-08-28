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

    [NonSerialized] bool emulatorsResolved;
    [NonSerialized] FirebaseEmulatorConfig resolvedEmulators;

    [SerializeField] EContentRunMode runMode;
    [SerializeField] bool includeTestCards;
    [SerializeField] string saveFolder = "Save";

    // 릴리스 빌드에서는 아래 게터가 두 필드를 읽지 않아 CS0414 가 난다. 저작값은 살려 둬야 하므로 경고만 끈다.
#pragma warning disable CS0414
    [Tooltip("켜면 서버 호출·세이브 문서·익명 로그인이 모두 로컬 에뮬레이터로 갑니다. 셋은 함께 켜집니다 — " +
             "하나만 로컬이면 uid와 세이브 문서가 서로 다른 백엔드를 가리켜 왕복 검증이 성립하지 않습니다.\n" +
             "테스트 프로파일 + 에디터/개발 빌드에서만 효과가 있습니다. 릴리스 빌드에서는 이 값과 무관하게 항상 꺼진 것으로 취급됩니다.")]
    [SerializeField] bool useLocalEmulators;

    [Tooltip("Functions 에뮬레이터 주소입니다. `firebase emulators:start` 가 출력하는 functions 호스트/포트를 그대로 적습니다.\n" +
             "기본값은 firebase.json 의 emulators.functions.port 와 같습니다. 비워 두면 에뮬레이터 전체가 꺼집니다.")]
    [SerializeField] string functionsEmulatorOrigin = "http://127.0.0.1:5001";

    [Tooltip("Firestore 에뮬레이터 주소입니다. host:port 형식이어야 하며 스킴(http://)을 붙이지 않습니다.\n" +
             "기본값은 firebase.json 의 emulators.firestore.port 와 같습니다. 비워 두면 에뮬레이터 전체가 꺼집니다.")]
    [SerializeField] string firestoreEmulatorHost = "127.0.0.1:8080";

    [Tooltip("Auth 에뮬레이터 주소입니다. host:port 형식이어야 하며 스킴(http://)을 붙이지 않습니다.\n" +
             "기본값은 firebase.json 의 emulators.auth.port 와 같습니다. 비워 두면 에뮬레이터 전체가 꺼집니다.")]
    [SerializeField] string authEmulatorHost = "127.0.0.1:9099";

#pragma warning restore CS0414

    public EContentRunMode RunMode => this.runMode;
    public bool IncludeTestCards => this.includeTestCards;
    public string SaveFolder => this.saveFolder;
    public string CloudEnvId => this.runMode == EContentRunMode.Test ? "test" : "live";

    /// <summary>이번 실행이 향할 Firebase 백엔드. 배포된 서버를 써야 하는 상황에서는 꺼진 설정이다.</summary>
    // 해석은 1회뿐이다 — 주소가 잘못 저작된 경우 이 게터는 실패 사유를 담아 돌려주는데, 접근할 때마다 다시 돌면
    // 매 프레임 그리는 디버그 표면이 같은 진단을 반복 생성한다.
    public FirebaseEmulatorConfig FirebaseEmulators
    {
        get
        {
            if (this.emulatorsResolved) return this.resolvedEmulators;

            this.emulatorsResolved = true;
            this.resolvedEmulators = ResolveEmulators();
            return this.resolvedEmulators;
        }
    }

    FirebaseEmulatorConfig ResolveEmulators()
    {
        // 릴리스 빌드가 localhost를 가리킬 경로 자체를 타입 수준에서 없앤다 — 저작 실수 하나로 라이브가 죽지 않게 한다.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (this.runMode != EContentRunMode.Test) return FirebaseEmulatorConfig.Disabled;
        if (!this.useLocalEmulators) return FirebaseEmulatorConfig.Disabled;
        return FirebaseEmulatorConfig.Create(
            this.functionsEmulatorOrigin, this.firestoreEmulatorHost, this.authEmulatorHost);
#else
        return FirebaseEmulatorConfig.Disabled;
#endif
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

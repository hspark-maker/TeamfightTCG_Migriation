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

    public EContentRunMode RunMode => this.runMode;
    public bool IncludeTestCards => this.includeTestCards;
    public string SaveFolder => this.saveFolder;
    public string CloudSaveProfileId => this.runMode == EContentRunMode.Test ? "test" : "current";

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

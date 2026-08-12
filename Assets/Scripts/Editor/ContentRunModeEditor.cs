using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>릴리즈 상태(실행 모드·모드별 카드 표·마지막 적용 모드)의 단일 소유자.
///
/// 실행 모드 키는 <see cref="ContentProfileConfig.EditorRunModeKey"/>를 그대로 쓴다 —
/// 키를 두 곳에서 각자 정의하면 툴이 바꾼 모드와 실제 실행 모드가 갈린다.
///
/// **모드와 값은 한 몸이다.** 두 시트가 같은 CardData를 공유하므로 모드만 바꾸고 값을 안 실으면
/// 수치가 조용히 어긋난다. 그래서 전환은 <see cref="SwitchTo"/> 하나로만 하고, 여기서 값까지 적용한다.</summary>
public static class ContentRunModeEditor
{
    public const string DEFAULT_CARD_ROOT  = "Assets/SO/Cards";

    const string PREF_ROOT       = "CardTable.CardRoot";
    // 카드 에셋에 마지막으로 밀어 넣은 시트가 어느 모드 것인지. 실행 모드와 어긋나면 경고 대상.
    const string PREF_APPLIED    = "CardTable.AppliedRunMode";

    public static EContentRunMode Current
    {
        get => (EContentRunMode)EditorPrefs.GetInt(ContentProfileConfig.EditorRunModeKey, (int)EContentRunMode.Test);
        private set => EditorPrefs.SetInt(ContentProfileConfig.EditorRunModeKey, (int)value);
    }

    /// <summary>카드 에셋에 실제로 실려 있는 시트의 모드. 시트를 적용한 시점에만 갱신된다.</summary>
    public static EContentRunMode Applied
    {
        get => (EContentRunMode)EditorPrefs.GetInt(PREF_APPLIED, (int)Current);
        set => EditorPrefs.SetInt(PREF_APPLIED, (int)value);
    }

    /// <summary>실행 모드와 에셋에 실린 시트가 어긋났는가. true면 수치가 모드와 다르다.</summary>
    public static bool IsDesynced => Applied != Current;

    public static string CardRoot
    {
        get => EditorPrefs.GetString(PREF_ROOT, DEFAULT_CARD_ROOT);
        set => EditorPrefs.SetString(PREF_ROOT, value);
    }

    public static EContentRunMode Other(EContentRunMode _mode)
        => _mode == EContentRunMode.Live ? EContentRunMode.Test : EContentRunMode.Live;

    public static string Label(EContentRunMode _mode)
        => _mode == EContentRunMode.Live ? "라이브" : "테스트";

    public static ContentProfileConfig ProfileOf(EContentRunMode _mode)
        => Resources.Load<ContentProfileConfig>(
            _mode == EContentRunMode.Live ? "ContentProfiles/Live" : "ContentProfiles/Test");

    /// <summary>모드를 바꾸고 그 모드의 시트를 카드 에셋에 적용한다. 시트를 못 읽으면 모드도 바꾸지 않는다 —
    /// 모드만 바뀌고 수치가 그대로 남는 게 제일 위험한 상태라 아예 만들지 않는다.
    /// 반환값은 사람이 읽을 결과 문자열, 실패 시 _error에 사유.</summary>
    public static string SwitchTo(EContentRunMode _mode, out string _error)
    {
        // 값이 실제로 들어온 뒤에만 모드를 바꾼다 — 모드만 바뀌고 수치가 이전 것으로 남는 게 제일 위험하다.
        string t_report = ApplyTable(_mode, out _error);
        if (_error != null) return null;

        Current = _mode;
        return t_report;
    }

    /// <summary>지정 모드의 값을 카드 에셋에 적용(모드 자체는 건드리지 않는다).
    ///
    /// 소스는 **구글 스펙시트 하나**다(라이브=Card / 테스트=Card_Test). CSV 경로는 없앴다 —
    /// 값이 들어오는 문이 둘이면 어느 쪽으로 마지막에 덮었는지에 따라 에셋이 달라진다.</summary>
    public static string ApplyTable(EContentRunMode _mode, out string _error)
    {
        string t_report = CardSpecImporter.ImportToAssets(_mode, out _error);
        if (_error != null) return null;

        Applied = _mode;
        AssetDatabase.SaveAssets();
        return t_report;
    }

    /// <summary>지정 모드의 스펙시트와 카드 에셋을 값 단위로 대조한다(빈 목록 = 일치, null = 시트를 못 읽음).
    /// <see cref="Applied"/>는 "어느 시트를 실었는가"라는 도장일 뿐이라 적용 후 인스펙터로 고친 값을 못 잡는다 —
    /// 실제로 빌드에 실리는 건 SO이므로 그 SO를 직접 견주는 창구가 따로 있어야 한다.</summary>
    public static List<string> DiffTable(EContentRunMode _mode, out string _error)
        => CardSpecImporter.DiffAgainstSheet(_mode, out _error);
}

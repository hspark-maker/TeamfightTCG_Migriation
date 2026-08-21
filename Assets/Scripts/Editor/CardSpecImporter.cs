using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// 스펙시트(SpecData)의 `Card` 표를 CardData 에셋에 굽는 에디터 도구.
///
/// ── 왜 런타임에서 SpecData를 안 읽나 ──
/// 게임이 읽는 값의 진실원은 여전히 **CardData 에셋**이다. 시나리오·카드팩·AI덱·도감이 전부 SO를 guid로
/// 참조하고 있어서 런타임 타입을 갈면 그 배선이 통째로 끊긴다. 그래서 스펙시트는 "저작 입력"이고,
/// 이 도구가 그 값을 에셋에 옮겨 굽는다 — 빌드에 SpecData 로딩·복호화 경로가 생기지 않고
/// 결정론·멀티 미러도 종전 그대로다(값이 이미 에셋에 박혀 있다).
///
/// ── 왜 파싱을 여기서 다시 안 하나 ──
/// 표 → 에셋 변환은 <see cref="CardTableTool.ImportRows"/> 하나뿐이다. 여기서는 SpecData 행을
/// 그 함수가 먹는 모양(0행=헤더, 1행~=데이터)으로 옮겨 담기만 한다. 키워드/시너지 해석, id 예약,
/// hp 곡선, 경고, 레지스트리 등록은 CSV 경로와 **완전히 같은 코드**를 탄다.
///
/// ── 왜 생성된 CARD_KEYWORD를 안 쓰나 ──
/// 규칙 enum의 진실원은 우리 <see cref="CardKeyword"/> 하나다. 시트가 만든 동명 enum을 함께 쓰면
/// 같은 개념의 진실원이 둘이 되고, 비트값이 어긋나도 컴파일은 통과한다(에셋에 int로 직렬화돼 있다).
/// 그래서 시트의 keywords/channel/synergies는 string으로 받아 이 프로젝트의 타입으로 해석한다.
public static class CardSpecImporter
{
    const string CardRoot = "Assets/SO/Cards";

    // CardTableTool이 헤더 이름으로 열을 찾으므로 순서는 자유다. 이름은 SpecData `Card`의 필드명과 1:1.
    static readonly string[] Columns =
    {
        "id",
        "name", "displayName", "channel", "maxHp",
        "keywords", "keywordUnlockLevel",
        "synergies", "defaultEvolutionStage",
        "hp2", "hp3", "hp4",
        "cardExplain", "grade",
    };

    [MenuItem("Tools/Card Battle/스펙시트(라이브) → 카드 에셋 적용")]
    static void ApplyLiveFromMenu() => ApplyFromMenu(EContentRunMode.Live);

    [MenuItem("Tools/Card Battle/스펙시트(테스트) → 카드 에셋 적용")]
    static void ApplyTestFromMenu() => ApplyFromMenu(EContentRunMode.Test);

    static void ApplyFromMenu(EContentRunMode _mode)
    {
        if (!TryLoadRows(_mode, out IEnumerable t_rows, out System.Type t_rowType, out int t_count, out string t_error))
        {
            EditorUtility.DisplayDialog("스펙시트 가져오기", t_error, "확인");
            return;
        }

        if (!EditorUtility.DisplayDialog("스펙시트 가져오기",
                $"{SheetNameOf(_mode)} 시트의 카드 {t_count}장을 에셋에 적용한다.\n" +
                "표에 없는 축(아트·패시브·보이스)은 건드리지 않는다.", "적용", "취소"))
            return;

        string t_report = ImportToAssets(_mode, out string t_importError);
        EditorUtility.DisplayDialog("스펙시트 가져오기",
                                    string.IsNullOrEmpty(t_importError) ? t_report : t_importError, "확인");
    }

    /// <summary>대화상자 없이 도는 실동작. 릴리즈 관리 창처럼 자체 UI가 있는 호출부가 쓴다 —
    /// 메뉴와 창이 각자 가져오기를 구현하면 두 경로의 결과가 갈라진다.</summary>
    public static string ImportToAssets(EContentRunMode _mode, out string _error)
    {
        if (!TryLoadRows(_mode, out IEnumerable t_rows, out System.Type t_rowType, out _, out _error))
        {
            Debug.LogError($"[CardSpec] {_error}");
            return null;
        }

        string t_report = CardTableTool.ImportRows(ToRows(t_rows, t_rowType), CardRoot, out _error);
        if (!string.IsNullOrEmpty(_error))
        {
            Debug.LogError($"[CardSpec] 가져오기 실패: {_error}");
            return null;
        }

        Debug.Log($"[CardSpec] {t_report}");
        return $"[{SheetNameOf(_mode)} 시트 적용]\n{t_report}";
    }

    /// <summary>시트와 카드 에셋을 값 단위로 대조한다(빈 목록 = 일치, null = 시트를 못 읽음).
    /// 적용(<see cref="ImportToAssets"/>)과 **같은 행 목록**을 견준다 — 소스가 갈리면
    /// "대조는 통과했는데 적용하면 값이 달라지는" 상태가 생긴다.</summary>
    public static List<string> DiffAgainstSheet(EContentRunMode _mode, out string _error)
    {
        if (!TryLoadRows(_mode, out IEnumerable t_rows, out System.Type t_rowType, out _, out _error))
            return null;

        return CardTableTool.DiffRows(ToRows(t_rows, t_rowType), out _error);
    }

    /// <summary>모드 → 시트 이름. 시트 이름이 곧 생성 클래스 이름이라 이 매핑이 유일한 연결 고리다.</summary>
    public static string SheetNameOf(EContentRunMode _mode)
        => _mode == EContentRunMode.Test ? nameof(Card_Test) : nameof(Card);

    /// <summary>빌드에 실리는 리소스(SpecData.bytes)를 그대로 읽는다 — 에디터가 보는 값과 게임이 받는 값이
    /// 갈라지지 않게, 시트를 다시 내려받지 않고 **마지막으로 생성된 결과물**을 소스로 삼는다.
    /// 즉 시트를 고쳤으면 SpecData 창에서 "시트 적용 & CS 생성"을 먼저 돌려야 한다.
    ///
    /// 라이브·테스트는 **같은 카드의 다른 값 세트**다. 두 시트가 같은 id를 써야 같은 에셋을 덮어쓴다 —
    /// id가 갈리면 그 카드는 새 에셋으로 복제된다(매칭 키가 id이므로).</summary>
    static bool TryLoadRows(EContentRunMode _mode, out IEnumerable _rows, out System.Type _rowType,
                            out int _count, out string _error)
    {
        _rows    = null;
        _rowType = null;
        _count   = 0;
        _error   = null;

        string t_json = SpecDataResourceLoader.LoadSpecData();
        if (string.IsNullOrEmpty(t_json))
        {
            _error = "SpecData 리소스를 못 읽었다. CookApps > SpecData 창에서 '시트 적용 & CS 생성'을 먼저 실행할 것.";
            return false;
        }

        var t_manager = new SpecDataManager();
        if (!t_manager.Load(t_json))
        {
            _error = "SpecData 파싱 실패. 생성된 리소스가 손상됐을 수 있다(재생성 필요).";
            return false;
        }

        if (_mode == EContentRunMode.Test)
        {
            IReadOnlyList<Card_Test> t_test = t_manager.Card_Test?.All;
            _rows    = t_test;
            _rowType = typeof(Card_Test);
            _count   = t_test?.Count ?? 0;
        }
        else
        {
            IReadOnlyList<Card> t_live = t_manager.Card?.All;
            _rows    = t_live;
            _rowType = typeof(Card);
            _count   = t_live?.Count ?? 0;
        }

        if (_count == 0)
        {
            _error = $"스펙시트에 {SheetNameOf(_mode)} 행이 없다. 시트 이름과 데이터 행을 확인할 것.";
            return false;
        }

        return true;
    }

    /// <summary>SpecData 행 묶음 → CardTableTool이 먹는 행 목록(0행 = 헤더).
    ///
    /// 행 타입을 고정하지 않고 <paramref name="_rowType"/>으로 받는 이유: 라이브·테스트가 **다른 시트**라
    /// 생성된 클래스도 서로 다른데(`Card` / `Card_test`), 필드 구성은 같다. 여기서 이름으로 값을 꺼내면
    /// 시트가 늘어도 이 함수는 그대로다 — 열을 추가할 때도 <see cref="Columns"/>만 늘리면 된다.</summary>
    static List<List<string>> ToRows(IEnumerable _rows, System.Type _rowType)
    {
        var t_rows = new List<List<string>> { new List<string>(Columns) };

        FieldInfo[] t_fields = _rowType.GetFields(BindingFlags.Public | BindingFlags.Instance);
        var t_byName = new Dictionary<string, FieldInfo>(t_fields.Length);
        foreach (FieldInfo t_f in t_fields) t_byName[t_f.Name] = t_f;

        foreach (string t_column in Columns)
            if (!t_byName.ContainsKey(t_column))
                Debug.LogWarning($"[CardSpec] {_rowType.Name} 시트에 '{t_column}' 열이 없다 — 그 축은 이번 적용에서 건너뛴다.");

        foreach (object t_card in _rows)
        {
            var t_row = new List<string>(Columns.Length);
            foreach (string t_column in Columns)
                t_row.Add(t_byName.TryGetValue(t_column, out FieldInfo t_field) ? Text(t_field.GetValue(t_card)) : "");

            t_rows.Add(t_row);
        }

        return t_rows;
    }

    /// <summary>셀 문자열화. 숫자는 **불변 문화권**으로 찍는다 — 로캘에 따라 소수점·자릿수 구분자가 바뀌면
    /// 받는 쪽 int 파싱이 조용히 0으로 떨어진다.</summary>
    static string Text(object _value) => _value switch
    {
        null      => "",
        string s  => s,
        bool b    => b ? "1" : "0",
        System.IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _         => _value.ToString(),
    };
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>서버에 쓰지 않고 팁 선택·스냅샷 호환·수신 실패·프리팹 배선을 검증한다.</summary>
public static class LoadingTipValidation
{
    const string REPORT_PATH = "Temp/LoadingTipValidation.txt";
    static readonly List<string> s_checks = new List<string>();

    [MenuItem("Tools/Card Battle/검증/전투 복귀 로딩 팁")]
    public static async void Run()
    {
        s_checks.Clear();
        try
        {
            Check(LoadingTipAuthoring.TryLoad(out List<LoadingTip> t_rows, out string t_error), t_error ?? "CSV schema");
            ValidateSnapshot(t_rows);
            ValidateIndex();
            ValidateSelection(t_rows);
            ValidatePrefab(t_rows);
            await ValidateOptionalDownload();
            File.WriteAllLines(REPORT_PATH, s_checks);
            Debug.Log($"[LoadingTipValidation] PASS {s_checks.Count} checks. {REPORT_PATH}");
        }
        catch (Exception t_exception)
        {
            s_checks.Add("FAIL " + t_exception);
            File.WriteAllLines(REPORT_PATH, s_checks);
            Debug.LogException(t_exception);
        }
    }

    static void ValidateSnapshot(List<LoadingTip> _rows)
    {
        // 내장본은 읽기 전용 테스트 fixture일 뿐 런타임 폴백이 아니다.
        JObject t_json = JObject.Parse(SpecDataResourceLoader.LoadSpecData());
        t_json.Remove("LoadingTip");
        SpecDataManager t_old = Load(t_json);
        Check(SpecPayloadCodec.TryBuildSnapshotTables(t_old, out List<SpecTablePayload> t_oldTables, out string t_error), t_error ?? "old snapshot");
        Check(t_oldTables.Count == SpecPayloadCodec.TableNames.Length, "old snapshot excludes absent tip");
        var t_required = new List<SpecTablePayload>();
        foreach (string t_name in SpecPayloadCodec.TableNames)
        {
            Check(SpecPayloadCodec.TryBuildLocalTable(t_old, t_name, out SpecTablePayload t_table, out t_error), t_error ?? t_name);
            t_required.Add(t_table);
        }
        string t_oldHash = SpecPayloadCodec.CombinedHash("test", t_oldTables);
        Check(t_oldHash == SpecPayloadCodec.CombinedHash("test", t_required), "old fingerprint preserved");
        t_json["LoadingTip"] = JArray.FromObject(_rows);
        SpecDataManager t_new = Load(t_json);
        Check(SpecPayloadCodec.TryBuildSnapshotTables(t_new, out List<SpecTablePayload> t_newTables, out t_error), t_error ?? "new snapshot");
        Check(t_newTables.Count == t_oldTables.Count + 1, "tip included in snapshot");
        Check(SpecPayloadCodec.CombinedHash("test", t_newTables) != t_oldHash, "tip affects cache fingerprint");
        Check(t_oldTables[0].PayloadHash == t_newTables[0].PayloadHash, "Card battle fingerprint unchanged");
        var t_roundTrip = new SpecDataManager();
        Check(t_roundTrip.Load(SpecPayloadCodec.BuildManagerJson(t_newTables)), "snapshot round trip loads");
        Check(SpecPayloadCodec.TryBuildSnapshotTables(t_roundTrip, out List<SpecTablePayload> t_roundTripTables, out t_error), t_error ?? "round trip tables");
        Check(SpecPayloadCodec.CombinedHash("test", t_newTables) == SpecPayloadCodec.CombinedHash("test", t_roundTripTables), "round trip fingerprint");
        t_json["LoadingTip"][0]["text"] = "수정한 팁";
        Check(SpecPayloadCodec.TryBuildSnapshotTables(Load(t_json), out List<SpecTablePayload> t_changed, out t_error), t_error ?? "changed snapshot");
        Check(t_changed.Last().PayloadHash != t_newTables.Last().PayloadHash, "tip edit changes hash");
        t_json.Remove("LoadingTip");
        Check(SpecPayloadCodec.TryBuildSnapshotTables(Load(t_json), out List<SpecTablePayload> t_removed, out t_error), t_error ?? "removed snapshot");
        Check(SpecPayloadCodec.CombinedHash("test", t_removed) == t_oldHash, "tip removal restores required-only fingerprint");
        t_json.Remove("Card");
        Check(!SpecPayloadCodec.TryBuildSnapshotTables(Load(t_json), out _, out _), "missing required table rejected");
    }

    static void ValidateIndex()
    {
        MethodInfo t_read = typeof(BattleContentSync).GetMethod("ReadRemoteVector", BindingFlags.Static | BindingFlags.NonPublic);
        var t_tables = new Dictionary<string, object>();
        foreach (string t_name in SpecPayloadCodec.TableNames)
            t_tables[t_name] = new Dictionary<string, object> { { "payloadHash", "hash" }, { "blobPath", "unused/" + t_name } };
        var t_index = new Dictionary<string, object> { { "major", ContentVersion.Major }, { "minor", 1L }, { "tables", t_tables } };
        object t_vector = t_read.Invoke(null, new object[] { t_index });
        Check(IndexHashes(t_vector).Count == SpecPayloadCodec.TableNames.Length, "old server index accepted");
        t_tables["LoadingTip"] = new Dictionary<string, object> { { "payloadHash", "tip" }, { "blobPath", "unused/tip" } };
        Check(IndexHashes(t_read.Invoke(null, new object[] { t_index })).ContainsKey("LoadingTip"), "optional index accepted");
        t_tables["LoadingTip"] = new Dictionary<string, object>();
        Check(!IndexHashes(t_read.Invoke(null, new object[] { t_index })).ContainsKey("LoadingTip"), "invalid optional index ignored");
        t_tables.Remove("Card");
        bool t_rejected = false;
        try { t_read.Invoke(null, new object[] { t_index }); }
        catch (TargetInvocationException t_exception) when (t_exception.InnerException is InvalidOperationException) { t_rejected = true; }
        Check(t_rejected, "invalid required index rejected");
    }

    static void ValidateSelection(List<LoadingTip> _rows)
    {
        Type t_type = typeof(LoadingCoverView).Assembly.GetType("BattleReturnTips");
        MethodInfo t_select = t_type.GetMethod("Select", BindingFlags.Static | BindingFlags.NonPublic);
        var t_random = new System.Random(71);
        string Select(IReadOnlyList<LoadingTip> rows, string previous = null)
            => (string)t_select.Invoke(null, new object[] { rows, previous, t_random });
        Check(Select(null) == null, "no tips uses fallback");
        var t_filtered = new[] { new LoadingTip { enabled = 0, text = "disabled" }, new LoadingTip { enabled = 1, text = " " }, null };
        Check(Select(t_filtered) == null, "disabled empty null rows excluded");
        var t_one = new[] { new LoadingTip { enabled = 1, text = "Tip : 하나" }, new LoadingTip { enabled = 1, text = "하나" } };
        Check(Select(t_one, "하나") == "하나", "duplicate text and prefix normalized; single tip allowed");
        string t_previous = null;
        var t_seen = new HashSet<string>();
        for (int i = 0; i < 2000; i++)
        {
            string t_tip = Select(_rows, t_previous);
            if (t_tip == t_previous) throw new InvalidOperationException("consecutive tip repeated");
            t_seen.Add(t_tip);
            t_previous = t_tip;
        }
        Check(t_seen.Count == _rows.Select(t => t.text).Distinct().Count(), "2000 selections: all tips, no consecutive repeats");
    }

    static void ValidatePrefab(List<LoadingTip> _rows)
    {
        var t_prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Assets/Prefabs/UI/Common/BattleReturnLoadingCover.prefab");
        var t_text = (TMP_Text)new SerializedObject(t_prefab.GetComponent<LoadingCoverView>()).FindProperty("_tipText").objectReferenceValue;
        Check(t_text != null && t_text.name == "Text_Value (1)", "existing TMP reference wired");
        foreach (LoadingTip t_row in _rows)
        {
            Vector2 t_size = t_text.GetPreferredValues("Tip : " + t_row.text);
            Rect t_rect = t_text.rectTransform.rect;
            Check(t_size.x <= t_rect.width && t_size.y <= t_rect.height, "tip " + t_row.id + " fits existing rect");
        }
    }

    static async Task ValidateOptionalDownload()
    {
        MethodInfo t_wait = typeof(BattleContentSync).GetMethod("WaitForOptionalTableAsync", BindingFlags.Static | BindingFlags.NonPublic);
        Task<SpecTablePayload> Wait(Task<SpecTablePayload> fetch)
            => (Task<SpecTablePayload>)t_wait.Invoke(null, new object[] { "LoadingTip", fetch });
        Check(await Wait(Task.FromException<SpecTablePayload>(new IOException("expected test failure"))) == null, "optional fetch failure uses fallback");
        var t_pending = new TaskCompletionSource<SpecTablePayload>();
        Check(await Wait(t_pending.Task) == null, "optional fetch timeout uses fallback");
        t_pending.SetException(new IOException("expected late failure"));
        Check(await Wait(Task.FromResult<SpecTablePayload>(null)) == null, "optional missing table uses fallback");
    }

    static SpecDataManager Load(JObject _json)
    {
        var t_manager = new SpecDataManager();
        Check(t_manager.Load(_json.ToString()), "manager loads fixture");
        return t_manager;
    }

    static Dictionary<string, string> IndexHashes(object _vector)
        => (Dictionary<string, string>)_vector.GetType().GetField("Hashes").GetValue(_vector);

    static void Check(bool _ok, string _name)
    {
        if (!_ok) throw new InvalidOperationException(_name);
        s_checks.Add("PASS " + _name);
    }
}

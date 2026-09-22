using System;
using System.Collections.Generic;
using System.Linq;

public static class TitleOwnershipHarness
{
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }

    public static void Main()
    {
        SpecFirestoreUploader.TestTitleConditions();
        TitleManager.SetCatalog(new TitleCatalog());
        Require(!TitleManager.IsOwned("first") && !TitleManager.TryEquip("first"), "Unowned title equipped.");
        var profile = DataSaveManager.Data.Profile;
        profile.OwnedTitleIds.Add("future");
        Require(TitleManager.IsOwned("future") && !TitleManager.CanEquip("future"), "Unknown ownership must survive.");
        int notifications = 0;
        TitleManager.OnChanged += () => notifications++;
        TitleManager.NotifyRehydrated();
        TitleManager.NotifyRehydrated();
        Require(notifications == 1 && DataSaveManager.SaveCalls == 0, "Adoption saved or repeated notification.");
        profile.OwnedTitleIds.Add("first");
        TitleManager.NotifyRehydrated();
        Require(notifications == 2 && TitleManager.EquippedId == "", "Grant auto-equipped or missed notification.");
        Require(TitleManager.TryEquip("first") && TitleManager.EquippedId == "first", "Owned title not equipped.");
        Require(DataSaveManager.SaveCalls == 1 && notifications == 3, "Equipment change did not persist once.");
        TitleManager.TryEquip("first");
        Require(DataSaveManager.SaveCalls == 1, "Repeated equipment saved again.");
        Require(TitleManager.TryEquip("") && TitleManager.EquippedId == "", "Unequip failed.");
        Require(profile.OwnedTitleIds.SequenceEqual(new[] { "future", "first" }), "Equipment changed ownership.");
        DataSaveManager.Data.Profile = new ProfileSaveData {
            OwnedTitleIds = new List<string> { "future", "first" }, EquippedTitleId = "first"
        };
        TitleManager.NotifyRehydrated();
        Require(TitleManager.EquippedId == "first" && DataSaveManager.SaveCalls == 2, "Reload did not restore without saving.");
        TitleUnlocks.Adopt(new[] { new TitleUnlockDefinition { TitleId = "first", Description = "누적 1승" } });
        Require(TitleUnlocks.Description("first", "flavor") == "누적 1승", "Server condition was not shown.");
        Require(TitleUnlocks.Description("future", "flavor") == "flavor", "Unknown title description lost.");
        TitleUnlocks.ResetSession();
        Require(TitleUnlocks.Description("first", "flavor") == "flavor", "Previous account conditions leaked.");
        AccountRewardHandoff.Enqueue("no-reward", null, null, null, null);
        Require(!AccountRewardHandoff.HasPending, "Empty battle reward queued.");
        var titles = new List<GrantedTitle> { new GrantedTitle() };
        AccountRewardHandoff.Enqueue("max-level", null, null, null, new AccountExperienceResult(), null, titles);
        Require(AccountRewardHandoff.HasPending && AccountRewardHandoff.Consume().Titles == titles,
            "Max-level title disappeared with zero experience.");
        AccountRewardHandoff.Enqueue("title-only", null, null, null, null, null, titles);
        AccountRewardHandoff.MarkExperienceShown("title-only");
        Require(AccountRewardHandoff.Consume().Titles == titles, "Title-only response failed without experience.");
        AccountRewardHandoff.ResetSession();
        Console.WriteLine("Title ownership harness: PASS (ownership, notifications, equipment, condition cache, title-only rewards)");
    }
}

public static partial class SpecFirestoreUploader
{
    public static void TestTitleConditions()
    {
        var rows = new System.Collections.ArrayList();
        foreach (string line in System.IO.File.ReadAllLines("docs/SpecData/Title_sheet.csv").Skip(3))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            string[] fields = line.Split(',');
            if (fields.Length != 6) throw new Exception("Title must contain six columns.");
            rows.Add(new TitleUploadRow { id = int.Parse(fields[0]), titleId = fields[1], eventKey = fields[2],
                synergyId = fields[3], targetCount = int.Parse(fields[4]), description = fields[5] });
        }
        if (rows.Count != 8 || !ValidateTitles(rows, out string error)) throw new Exception("Invalid title CSV.");
        var first = (TitleUploadRow)rows[0];
        first.targetCount = 0;
        if (ValidateTitles(rows, out _)) throw new Exception("Automatic title accepted zero target.");
        first.targetCount = 1;
        first.eventKey = "Unknown";
        if (ValidateTitles(rows, out _)) throw new Exception("Title accepted unknown event.");
        first.eventKey = "";
        first.targetCount = 0;
        if (!ValidateTitles(rows, out error)) throw new Exception("Manual title rejected: " + error);
        Console.WriteLine("Title CSV validation: PASS (8 independent definitions, invalid targets/events, manual grant)");
    }
}

public class ProfileSaveData
{
    public List<string> OwnedTitleIds = new List<string>();
    public string EquippedTitleId = "";
}
public class UserSaveData { public ProfileSaveData Profile = new ProfileSaveData(); }
public static class DataSaveManager
{
    public static UserSaveData Data = new UserSaveData();
    public static int SaveCalls;
    public static void Save() { SaveCalls++; }
}
public class TitleCatalog
{
    public bool TryGet(string id, out object entry) { entry = null; return id == "first"; }
}
internal class ServerCommandResult { }
internal class ClaimRewardGain { }
internal class OpenPackCard { }
internal class ClaimRewardPack { }
internal class GrantedCosmetic { }
internal class GrantedTitle { }
internal class AccountExperienceResult
{
    internal long GrantedExp;
    internal int PreviousLevel;
    internal int Level;
    internal long? TotalExp;
    internal bool IsLevelUp => Level > PreviousLevel;
}
namespace Newtonsoft.Json
{
    public class JsonPropertyAttribute : Attribute { public JsonPropertyAttribute(string name) { } }
}
namespace UnityEngine
{
    public enum RuntimeInitializeLoadType { SubsystemRegistration }
    public class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType type) { }
    }
}

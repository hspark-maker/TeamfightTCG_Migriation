using System;
using System.Collections.Generic;
using System.Linq;

public static class Harness
{
    static void Require(bool _value, string _message) { if (!_value) throw new Exception(_message); }

    public static void Main()
    {
        var config = new ProfileConfig();
        config.Avatars.Add("avatar_00", new ProfileAvatarEntry());
        config.Avatars.Add("avatar_01", new ProfileAvatarEntry());
        config.Frames.Add("frame_default", new ProfileFrameEntry());
        config.Frames.Add("frame_locked", new ProfileFrameEntry());
        var emotes = new EmoteCatalog();
        for (int i = 1; i <= 7; i++) emotes.Entries.Add(new EmoteEntry { id = i });
        ProfileManager.SetConfig(config);
        ProfileManager.SetEmoteCatalog(emotes);
        ProfileManager.Init();
        Require(!ProfileManager.IsAvatarOwned("avatar_00") && ProfileManager.AvatarId == "", "Missing ownership must not grant the catalog.");
        Require(ProfileManager.EmoteIds.Count == 6 && ProfileManager.EmoteIds.All(id => id == 0), "Missing ownership must leave six empty emote slots.");

        var slot = DataSaveManager.Data.Profile;
        slot.Nickname = "ExistingLongNickname";
        slot.AvatarId = "avatar_00";
        slot.FrameId = "frame_default";
        slot.OwnedAvatarIds = new List<string> { "avatar_00", "future_avatar" };
        slot.OwnedFrameIds = new List<string> { "frame_default" };
        slot.OwnedEmoteIds = new List<int> { 1, 2, 3, 4, 5, 6, 999 };
        slot.EmoteIds = new List<int> { 2, 0, 1, 2, 7, 999 };
        ProfileManager.Init();
        Require(ProfileManager.EmoteIds.SequenceEqual(new[] { 2, 0, 1, 0, 0, 0 }), "Emote restore must retain order and reject duplicate, unowned, and unknown IDs.");
        Require(ProfileManager.IsAvatarOwned("future_avatar") && slot.OwnedEmoteIds.Contains(999), "Unknown ownership must survive presentation filtering.");
        ProfileManager.Apply(ProfileManager.Nickname, "avatar_01", "frame_locked", ProfileManager.EmoteIds);
        Require(ProfileManager.AvatarId == "avatar_00" && ProfileManager.FrameId == "frame_default", "Unowned equipment accepted.");
        Require(ProfileManager.Nickname == "ExistingLongNickname", "Existing nickname truncated by cosmetic editing.");

        int ownershipEvents = 0;
        ProfileManager.OnOwnershipChanged += () => ownershipEvents++;
        int savedBefore = DataSaveManager.SaveCalls;
        slot.OwnedAvatarIds.Add("avatar_01");
        ProfileManager.NotifyOwnershipRehydrated();
        ProfileManager.NotifyOwnershipRehydrated();
        Require(ownershipEvents == 1 && DataSaveManager.SaveCalls == savedBefore, "Ownership rehydration must notify once without saving.");
        Require(ProfileManager.AvatarId == "avatar_00", "Grant auto-equipped the item.");
        ProfileManager.Apply(ProfileManager.Nickname, "avatar_01", "frame_default", new[] { 6, 0, 1, 6, 7, 2 });
        Require(ProfileManager.AvatarId == "avatar_01" && ProfileManager.EmoteIds.SequenceEqual(new[] { 6, 0, 1, 0, 0, 2 }), "Owned equipment or six-slot validation failed.");

        slot = DataSaveManager.Data.Profile;
        slot.OwnedTitleIds.Add("title_local");
        slot.EquippedTitleId = "title_local";
        var remote = new ProfileSaveData {
            Nickname = "stale", AvatarId = "avatar_00", FrameId = "stale", EmoteIds = new List<int> { 1 },
            OwnedAvatarIds = new List<string> { "avatar_00", "avatar_01", "new_server" },
            OwnedFrameIds = new List<string> { "frame_default" }, OwnedEmoteIds = new List<int> { 1, 2, 6, 7 },
            AccountExp = 300,
            OwnedTitleIds = new List<string> { "title_local", "title_server" },
        };
        DataSaveManager.AdoptServerSlots(new ServerSlotPatch { Profile = remote });
        Require(remote.Nickname == slot.Nickname && remote.AvatarId == "avatar_01" && remote.FrameId == slot.FrameId && remote.EmoteIds.SequenceEqual(slot.EmoteIds), "Server grant reverted local profile edits.");
        Require(remote.OwnedTitleIds.SequenceEqual(new[] { "title_local", "title_server" }) && remote.EquippedTitleId == "title_local", "Server title ownership was overwritten or equipment draft was lost.");
        Require(remote.AccountExp == 300 && remote.OwnedAvatarIds.Contains("new_server"), "Server-owned fields were not adopted.");
        Require(ReferenceEquals(remote.ContentUnlocks, slot.ContentUnlocks), "Content unlock history was lost.");
        var fields = PlayerSaveDocument.ToSlotFieldMap(DataSaveManager.Data, ESaveSlot.Profile, 4);
        Require(!fields.ContainsKey("profile"), "Profile upload replaces the whole server-owned map.");
        Require(!fields.ContainsKey("profile.ownedTitleIds") && (string)fields["profile.equippedTitleId"] == "title_local", "Title upload must contain equipment only.");
        Require(!fields.ContainsKey("profile.ownedAvatarIds") && !fields.ContainsKey("profile.ownedFrameIds") && !fields.ContainsKey("profile.ownedEmoteIds") && !fields.ContainsKey("profile.accountExp") && !fields.ContainsKey("profile.accountRewardLevel"), "Profile upload includes server-owned fields.");
        Require((string)fields["profile.avatarId"] == "avatar_01" && ReferenceEquals(fields["profile.emoteIds"], remote.EmoteIds), "Valid equipment missing from upload.");
        remote.AvatarId = null; remote.FrameId = null; remote.EmoteIds = null;
        fields = PlayerSaveDocument.ToSlotFieldMap(DataSaveManager.Data, ESaveSlot.Profile, 5);
        Require(!fields.ContainsKey("profile.avatarId") && !fields.ContainsKey("profile.frameId") && !fields.ContainsKey("profile.emoteIds"), "Unset equipment must be omitted, not written as null.");
        remote.EmoteIds = new List<int> { 0, 0, 0, 0, 0, 0 };
        fields = PlayerSaveDocument.ToSlotFieldMap(DataSaveManager.Data, ESaveSlot.Profile, 6);
        Require(((List<int>)fields["profile.emoteIds"]).Count == 6, "Explicitly emptied equipment must still upload.");
        remote.AvatarId = "avatar_01";
        remote.FrameId = "frame_locked";
        remote.EmoteIds = new List<int> { 7, 0, 0, 0, 0, 0 };
        var reset = new ProfileSaveData {
            OwnedAvatarIds = new List<string> { "avatar_00" },
            OwnedFrameIds = new List<string> { "frame_default" },
            OwnedEmoteIds = new List<int> { 1, 2, 3, 4, 5, 6 },
        };
        savedBefore = DataSaveManager.SaveCalls;
        DataSaveManager.AdoptServerSlots(new ServerSlotPatch { Profile = reset }, _preserveProfileEquipment: false);
        Require(reset.AvatarId == null && reset.FrameId == null && reset.EmoteIds == null, "Explicit save reset retained formerly owned cosmetic equipment.");
        Require(reset.Nickname == remote.Nickname && reset.OwnedTitleIds.Count == 0 && reset.EquippedTitleId == "" && ReferenceEquals(reset.ContentUnlocks, remote.ContentUnlocks), "Explicit server reset must reset title ownership and equipment while retaining local identity.");
        ProfileManager.Init();
        Require(ProfileManager.AvatarId == "avatar_00" && ProfileManager.FrameId == "frame_default" && ProfileManager.EmoteIds.SequenceEqual(new[] { 1, 2, 3, 4, 5, 6 }), "Post-reset manager initialization did not restore owned defaults.");
        Require(DataSaveManager.SaveCalls == savedBefore, "Explicit server reset triggered an extra profile save.");
        Console.WriteLine("PASS: profile ownership, equipment validation, rehydration, local-edit preservation, upload field ownership, and explicit reset.");
    }
}

namespace UnityEngine
{
    public class Sprite { }
    public struct Color { public static Color white => new Color(); }
    public static class Debug { public static void LogWarning(object _value) { } public static void LogException(Exception _error) { throw _error; } }
    public static class Application { public static string version => "test"; }
}
namespace Firebase.Firestore
{
    public enum UnknownPropertyHandling { Ignore }
    public class FirestoreDataAttribute : Attribute { public UnknownPropertyHandling UnknownPropertyHandling { get; set; } }
    public class FirestorePropertyAttribute : Attribute { public FirestorePropertyAttribute(string _name) { } }
    public static class FieldValue { public static object ServerTimestamp => new object(); }
    public class DocumentSnapshot
    {
        public bool Exists => false;
        public bool TryGetValue<T>(string _key, out T _value) { _value = default; return false; }
        public Dictionary<string, object> ToDictionary() => new Dictionary<string, object>();
    }
}
public static class LocalPrefs
{
    public static string GetString(string _key, string _default) => _default;
    public static void SetString(string _key, string _value) { }
    public static void Save() { }
}
public class ContentUnlockSaveData { }
public interface INicknameFilter { bool IsBlocked(string _name); }
public class ProfileAvatarEntry { public UnityEngine.Sprite large = new UnityEngine.Sprite(); public UnityEngine.Sprite SmallOrLarge => large; public UnityEngine.Color color; }
public class ProfileFrameEntry { public UnityEngine.Sprite sprite = new UnityEngine.Sprite(); public UnityEngine.Color color; }
public struct ProfileLook { public ProfileLook(UnityEngine.Sprite a, UnityEngine.Color b, UnityEngine.Sprite c, UnityEngine.Sprite d, UnityEngine.Color e) { } }
public class ProfileConfig
{
    public Dictionary<string, ProfileAvatarEntry> Avatars = new Dictionary<string, ProfileAvatarEntry>();
    public Dictionary<string, ProfileFrameEntry> Frames = new Dictionary<string, ProfileFrameEntry>();
    public string DefaultAvatarId => "avatar_00"; public string DefaultFrameId => "frame_default";
    public UnityEngine.Sprite AvatarPlate => null;
    public bool TryGetAvatar(string id, out ProfileAvatarEntry entry) { entry = null; return id != null && Avatars.TryGetValue(id, out entry); }
    public bool TryGetFrame(string id, out ProfileFrameEntry entry) { entry = null; return id != null && Frames.TryGetValue(id, out entry); }
    public ProfileLook LookOf(string a, string f) => default;
}
public class EmoteEntry { public int id; public UnityEngine.Sprite sprite = new UnityEngine.Sprite(); }
public class EmoteCatalog
{
    public const int SLOT_COUNT = 6;
    public List<EmoteEntry> Entries = new List<EmoteEntry>();
    public int Count => Entries.Count;
    public EmoteEntry PoolAt(int i) => Entries[i];
    public bool TryGet(int id, out EmoteEntry entry) { entry = Entries.Find(e => e.id == id); return entry != null; }
}
public class TitleCatalog { public bool TryGet(string id, out object entry) { entry = null; return true; } }
[Flags] public enum ESaveSlot { None = 0, Ownership = 1, Deck = 2, CardGrowth = 4, Rank = 8, AlbumReward = 16, Adventure = 32, Tutorial = 64, Profile = 128 }
public class UserSaveData
{
    public const int VERSION = 1;
    public object Ownership, Deck, CardGrowth, Rank, AlbumReward, Adventure, Tutorial;
    public ProfileSaveData Profile = new ProfileSaveData();
}
public class ServerSlotPatch : UserSaveData { }
public static class DataSaveManager
{
    public static UserSaveData Data = new UserSaveData();
    public static int SaveCalls;
    public const int SaveSlotCount = 8;
    public static ESaveSlot SaveSlotAt(int i) => (ESaveSlot)(1 << i);
    public static object GetSlotValue(UserSaveData data, ESaveSlot slot) => null;
    public static event Action<ESaveSlot> OnServerSlotsAdopted;
    public static void Save() { SaveCalls++; }
    static UserSaveData Normalize(UserSaveData data) => data;
    /* PRODUCTION_ADOPT */
}

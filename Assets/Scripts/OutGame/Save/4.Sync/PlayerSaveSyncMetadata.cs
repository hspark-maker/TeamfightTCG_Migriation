using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

[Serializable]
sealed class PlayerSaveSyncMetadata
{
    public string firebaseUid;
    public string profileId;
    public string lastSyncedHash;
    public long lastRemoteRevision = -1;
    public int schemaVersion;
}

static class PlayerSaveSyncMetadataStore
{
    const string KEY_PREFIX = "outgame_sync_state";

    internal static async UniTask<PlayerSaveSyncMetadata> LoadAsync(string _firebaseUid, string _profileId)
    {
        string t_json = await DataSaveManager.LoadSyncMetadataAsync(KeyOf(_firebaseUid, _profileId));
        if (string.IsNullOrEmpty(t_json)) return null;

        try
        {
            return JsonUtility.FromJson<PlayerSaveSyncMetadata>(t_json);
        }
        catch (Exception t_exception)
        {
            Debug.LogWarning($"[PlayerSaveSync] Sync metadata ignored: {t_exception.Message}");
            return null;
        }
    }

    internal static async UniTask<bool> SaveConfirmedAsync(
        string _firebaseUid,
        string _profileId,
        string _fullHash,
        long _remoteRevision)
    {
        var t_metadata = new PlayerSaveSyncMetadata
        {
            firebaseUid = _firebaseUid,
            profileId = _profileId,
            lastSyncedHash = _fullHash,
            lastRemoteRevision = _remoteRevision,
            schemaVersion = UserSaveData.VERSION
        };

        try
        {
            await DataSaveManager.SaveSyncMetadataAsync(
                KeyOf(_firebaseUid, _profileId),
                JsonUtility.ToJson(t_metadata));
            return true;
        }
        catch (Exception t_exception)
        {
            Debug.LogWarning($"[PlayerSaveSync] Sync metadata save failed: {t_exception.Message}");
            return false;
        }
    }

    static string KeyOf(string _firebaseUid, string _profileId)
    {
        return $"{KEY_PREFIX}_{_firebaseUid}_{_profileId}";
    }
}

using System;
using UnityEngine;

[Serializable]
sealed class PlayerSaveSyncMetadata
{
    public string firebaseUid;
    public string envId;
    public string lastSyncedHash;
    public long lastRemoteRevision = -1;
    public int schemaVersion;
}

static class PlayerSaveSyncMetadataStore
{
    const string KEY_PREFIX = "outgame_sync_state";

    internal static PlayerSaveSyncMetadata Load(string _firebaseUid, string _envId)
    {
        string t_json = DataSaveManager.LoadSyncMetadata(KeyOf(_firebaseUid, _envId));
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

    internal static bool SaveConfirmed(
        string _firebaseUid,
        string _envId,
        string _fullHash,
        long _remoteRevision)
    {
        var t_metadata = new PlayerSaveSyncMetadata
        {
            firebaseUid = _firebaseUid,
            envId = _envId,
            lastSyncedHash = _fullHash,
            lastRemoteRevision = _remoteRevision,
            schemaVersion = UserSaveData.VERSION
        };

        try
        {
            DataSaveManager.SaveSyncMetadata(
                KeyOf(_firebaseUid, _envId),
                JsonUtility.ToJson(t_metadata));
            return true;
        }
        catch (Exception t_exception)
        {
            Debug.LogWarning($"[PlayerSaveSync] Sync metadata save failed: {t_exception.Message}");
            return false;
        }
    }

    static string KeyOf(string _firebaseUid, string _envId)
    {
        return $"{KEY_PREFIX}_{_firebaseUid}_{_envId}";
    }
}

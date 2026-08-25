using System;

[Serializable]
sealed class PlayerSaveConflictSnapshot
{
    public string firebaseUid;
    public string profileId;
    public string payload;
    public string payloadHash;
    public long revision;
    public long schemaVersion;
    public long capturedUnix;
}

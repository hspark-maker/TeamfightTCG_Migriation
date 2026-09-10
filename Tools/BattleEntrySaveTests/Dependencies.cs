using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

// Save transport and session state are controlled by tests. The entry wait itself is extracted
// from PlayerSaveCloud.cs by run.ps1, so no copy of the production algorithm lives here.
static partial class PlayerSaveCloud
{
    static int s_generation, s_dirtySerial, s_uploadedSerial, s_serverCommandDepth;
    static int s_pendingVersion, s_sessionImmediateRequests;
    static long s_pendingUploadDeadlineTicks;
    static bool s_initialized, s_gateComplete, s_uploadApproved;
    static UniTaskCompletionSource s_uploadCompletion;
    internal static EPlayerSaveCloudState State;
    internal static bool HasPendingUpload => s_dirtySerial != s_uploadedSerial;
    internal static bool CanRunServerCommand => s_gateComplete &&
        (State == EPlayerSaveCloudState.Ready || State == EPlayerSaveCloudState.Offline || State == EPlayerSaveCloudState.Uploading);

    sealed class Upload
    {
        internal int Serial;
        internal UniTaskCompletionSource Completion;
    }
    static readonly Queue<Upload> uploads = new Queue<Upload>();
    internal static int UploadCount;
    internal static bool IsSuspended { set => s_serverCommandDepth = value ? 1 : 0; }
    internal static void Reset()
    {
        s_generation++;
        s_dirtySerial = s_uploadedSerial = s_serverCommandDepth = 0;
        s_pendingVersion = s_sessionImmediateRequests = 0;
        s_pendingUploadDeadlineTicks = 0;
        s_initialized = s_gateComplete = s_uploadApproved = true;
        s_uploadCompletion = null;
        State = EPlayerSaveCloudState.Ready;
        UploadCount = 0;
        uploads.Clear();
    }
    internal static void MarkDirty() => s_dirtySerial++;
    internal static void ReplaceSession() => s_generation++;
    internal static void SetInitialized(bool value) => s_initialized = value;
    internal static void SetGateComplete(bool value) => s_gateComplete = value;
    internal static void SetApproved(bool value) => s_uploadApproved = value;
    internal static void StartExistingUpload() => UploadAsync(s_generation, true).Forget();
    static UniTask UploadAsync(int generation, bool immediate)
    {
        if (generation != s_generation || s_serverCommandDepth > 0 || !HasPendingUpload)
            return UniTask.CompletedTask;
        if (s_uploadCompletion != null) throw new Exception("Overlapping upload requested.");
        UploadCount++;
        s_uploadCompletion = new UniTaskCompletionSource();
        uploads.Enqueue(new Upload { Serial = s_dirtySerial, Completion = s_uploadCompletion });
        State = EPlayerSaveCloudState.Uploading;
        return s_uploadCompletion.Task;
    }
    internal static void CompleteUpload(bool success = true)
    {
        if (uploads.Count == 0) throw new Exception("No pending upload.");
        Upload upload = uploads.Dequeue();
        if (success) s_uploadedSerial = upload.Serial;
        State = success ? EPlayerSaveCloudState.Ready : EPlayerSaveCloudState.Offline;
        s_uploadCompletion = null;
        upload.Completion.TrySetResult();
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;

static class Tests
{
    static int passed;
    static int Main()
    {
        try
        {
            Run("clean ready save completes without network", Clean);
            Run("suspended dirty save waits for resume and actual upload", SuspendedDirty);
            Run("existing upload followed by newly dirty save", ExistingUpload);
            Run("changes during entry upload are flushed before success", ChangesDuringUpload);
            Run("failed own or existing upload returns false without retry", FailedUploads);
            Run("unavailable sessions and offline clean save cannot approve entry", Unavailable);
            Run("suspension cancellation stops entry wait", CancelSuspended);
            Run("upload cancellation leaves transport running without another flush", CancelUploading);
            Run("replaced session invalidates suspended and in-flight waits", SessionReplacement);
            Run("session becoming blocked or unapproved releases suspension wait", SessionBlocked);
            Console.WriteLine($"PASS {passed} battle entry save tests");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }
    static void Run(string name, Action test)
    {
        Reset();
        test();
        passed++;
        Console.WriteLine("PASS " + name);
    }
    static void Reset() { FakePlayerLoop.Reset(); PlayerSaveCloud.Reset(); }
    static Task<bool> Flush(CancellationToken token = default)
        => PlayerSaveCloud.FlushForBattleEntryAsync(token).Inner;
    static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }
    static bool Result(Task<bool> task)
    {
        Check(task.IsCompleted, "Expected the entry wait to be complete.");
        return task.GetAwaiter().GetResult();
    }
    static void Pending(Task task) => Check(!task.IsCompleted, "Entry completed before required save work.");
    static void Canceled(Task task)
    {
        Check(task.IsCompleted, "Cancellation did not complete the entry wait.");
        try { task.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { return; }
        throw new Exception("Expected cancellation.");
    }
    static void Clean()
    {
        Check(Result(Flush()), "Ready clean state should succeed.");
        Check(PlayerSaveCloud.UploadCount == 0, "Clean entry started an upload.");
    }
    static void SuspendedDirty()
    {
        PlayerSaveCloud.MarkDirty();
        PlayerSaveCloud.IsSuspended = true;
        var task = Flush();
        Pending(task);
        Check(PlayerSaveCloud.UploadCount == 0, "Upload started during server command suspension.");
        FakePlayerLoop.Tick();
        Pending(task);
        PlayerSaveCloud.IsSuspended = false;
        FakePlayerLoop.Tick();
        Pending(task);
        Check(PlayerSaveCloud.UploadCount == 1, "Resume did not start required upload.");
        PlayerSaveCloud.CompleteUpload();
        Check(Result(task), "Resume and upload should complete entry.");
    }
    static void ExistingUpload()
    {
        PlayerSaveCloud.MarkDirty();
        PlayerSaveCloud.StartExistingUpload();
        PlayerSaveCloud.MarkDirty();
        var task = Flush();
        Pending(task);
        Check(PlayerSaveCloud.UploadCount == 1, "Entry started a concurrent upload.");
        PlayerSaveCloud.CompleteUpload();
        Pending(task);
        Check(PlayerSaveCloud.UploadCount == 2, "Changes after the existing snapshot were skipped.");
        PlayerSaveCloud.CompleteUpload();
        Check(Result(task), "Latest serial must complete entry.");
    }
    static void ChangesDuringUpload()
    {
        PlayerSaveCloud.MarkDirty();
        var task = Flush();
        PlayerSaveCloud.MarkDirty();
        PlayerSaveCloud.CompleteUpload();
        Pending(task);
        Check(PlayerSaveCloud.UploadCount == 2, "Changes during upload were skipped.");
        PlayerSaveCloud.CompleteUpload();
        Check(Result(task) && !PlayerSaveCloud.HasPendingUpload, "Dirty save cannot approve entry.");
    }
    static void FailedUploads()
    {
        foreach (bool existing in new[] { false, true })
        {
            Reset();
            PlayerSaveCloud.MarkDirty();
            if (existing) PlayerSaveCloud.StartExistingUpload();
            var task = Flush();
            PlayerSaveCloud.CompleteUpload(false);
            Check(!Result(task), "Upload failure approved entry.");
            Check(PlayerSaveCloud.UploadCount == 1 && PlayerSaveCloud.HasPendingUpload,
                "Failure must retain dirty save without looping retries.");
        }
    }
    static void Unavailable()
    {
        foreach (var state in new[] { EPlayerSaveCloudState.Disabled, EPlayerSaveCloudState.Loading,
            EPlayerSaveCloudState.Failed, EPlayerSaveCloudState.Blocked, EPlayerSaveCloudState.Offline })
        {
            Reset();
            PlayerSaveCloud.State = state;
            Check(!Result(Flush()), "Unavailable clean state approved entry: " + state);
            Check(PlayerSaveCloud.UploadCount == 0, "Unavailable clean state started an upload.");
        }
        foreach (Action<bool> setter in new Action<bool>[] { PlayerSaveCloud.SetInitialized,
            PlayerSaveCloud.SetGateComplete, PlayerSaveCloud.SetApproved })
        {
            Reset(); setter(false);
            Check(!Result(Flush()), "Incomplete session approved entry.");
        }
    }
    static void CancelSuspended()
    {
        using (var stop = new CancellationTokenSource())
        {
            PlayerSaveCloud.MarkDirty();
            PlayerSaveCloud.IsSuspended = true;
            var task = Flush(stop.Token);
            stop.Cancel();
            FakePlayerLoop.Tick();
            Canceled(task);
            PlayerSaveCloud.IsSuspended = false;
            FakePlayerLoop.Tick();
            Check(PlayerSaveCloud.UploadCount == 0, "Canceled waiter resumed upload.");
        }
    }
    static void CancelUploading()
    {
        using (var stop = new CancellationTokenSource())
        {
            PlayerSaveCloud.MarkDirty();
            var task = Flush(stop.Token);
            PlayerSaveCloud.MarkDirty();
            stop.Cancel();
            Canceled(task);
            PlayerSaveCloud.CompleteUpload();
            Check(PlayerSaveCloud.UploadCount == 1 && PlayerSaveCloud.HasPendingUpload,
                "Canceled entry wait started an additional upload or lost a change.");
        }
    }
    static void SessionReplacement()
    {
        PlayerSaveCloud.MarkDirty();
        PlayerSaveCloud.IsSuspended = true;
        var suspended = Flush();
        PlayerSaveCloud.ReplaceSession();
        FakePlayerLoop.Tick();
        Check(!Result(suspended) && PlayerSaveCloud.UploadCount == 0, "Old suspension approved a new session.");
        Reset();
        PlayerSaveCloud.MarkDirty();
        var uploading = Flush();
        PlayerSaveCloud.ReplaceSession();
        PlayerSaveCloud.CompleteUpload();
        Check(!Result(uploading), "Old upload approved a new session.");
    }
    static void SessionBlocked()
    {
        foreach (bool block in new[] { true, false })
        {
            Reset();
            PlayerSaveCloud.MarkDirty();
            PlayerSaveCloud.IsSuspended = true;
            var task = Flush();
            if (block) PlayerSaveCloud.State = EPlayerSaveCloudState.Blocked;
            else PlayerSaveCloud.SetApproved(false);
            FakePlayerLoop.Tick();
            Check(!Result(task) && PlayerSaveCloud.UploadCount == 0, "Invalidated suspension remained pending.");
        }
    }
}

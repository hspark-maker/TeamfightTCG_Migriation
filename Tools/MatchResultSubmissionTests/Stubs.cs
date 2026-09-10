// Compile the complete production source against controlled dependencies.
#pragma warning disable CS0649 // Domain fixture fields deliberately keep default values.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

#if !REAL_UNITY
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class)]
    public sealed class AsyncMethodBuilderAttribute : Attribute
    { public AsyncMethodBuilderAttribute(Type builderType) { } }
}
namespace Cysharp.Threading.Tasks
{
    public enum DelayType { DeltaTime, UnscaledDeltaTime, Realtime }
    [AsyncMethodBuilder(typeof(UniTaskBuilder))]
    public struct UniTask
    {
        internal Task Inner;
        public UniTask(Task task) { Inner = task; }
        public TaskAwaiter GetAwaiter() => (Inner ?? Task.CompletedTask).GetAwaiter();
        public static UniTask CompletedTask => new UniTask(Task.CompletedTask);
        public static UniTask Delay(TimeSpan delay, DelayType delayType = DelayType.DeltaTime,
            CancellationToken cancellationToken = default, bool cancelImmediately = false)
            => new UniTask(FakeClock.Delay(delay.TotalSeconds, cancellationToken, delayType));
        public void Forget() => Forgotten.Observe(Inner ?? Task.CompletedTask);
    }
    [AsyncMethodBuilder(typeof(UniTaskBuilder<>))]
    public struct UniTask<T>
    {
        internal Task<T> Inner;
        public UniTask(Task<T> task) { Inner = task; }
        public TaskAwaiter<T> GetAwaiter() => Inner.GetAwaiter();
    }
    [AsyncMethodBuilder(typeof(UniTaskVoidBuilder))]
    public struct UniTaskVoid { public void Forget() { } }
    public struct UniTaskBuilder
    {
        AsyncTaskMethodBuilder inner;
        public static UniTaskBuilder Create() => new UniTaskBuilder { inner = AsyncTaskMethodBuilder.Create() };
        public UniTask Task => new UniTask(inner.Task);
        public void SetResult() => inner.SetResult();
        public void SetException(Exception e) => inner.SetException(e);
        public void SetStateMachine(IAsyncStateMachine s) => inner.SetStateMachine(s);
        public void Start<T>(ref T s) where T : IAsyncStateMachine => inner.Start(ref s);
        public void AwaitOnCompleted<T, S>(ref T a, ref S s) where T : INotifyCompletion where S : IAsyncStateMachine => inner.AwaitOnCompleted(ref a, ref s);
        public void AwaitUnsafeOnCompleted<T, S>(ref T a, ref S s) where T : ICriticalNotifyCompletion where S : IAsyncStateMachine => inner.AwaitUnsafeOnCompleted(ref a, ref s);
    }
    public struct UniTaskBuilder<T>
    {
        AsyncTaskMethodBuilder<T> inner;
        public static UniTaskBuilder<T> Create() => new UniTaskBuilder<T> { inner = AsyncTaskMethodBuilder<T>.Create() };
        public UniTask<T> Task => new UniTask<T>(inner.Task);
        public void SetResult(T value) => inner.SetResult(value);
        public void SetException(Exception e) => inner.SetException(e);
        public void SetStateMachine(IAsyncStateMachine s) => inner.SetStateMachine(s);
        public void Start<S>(ref S s) where S : IAsyncStateMachine => inner.Start(ref s);
        public void AwaitOnCompleted<A, S>(ref A a, ref S s) where A : INotifyCompletion where S : IAsyncStateMachine => inner.AwaitOnCompleted(ref a, ref s);
        public void AwaitUnsafeOnCompleted<A, S>(ref A a, ref S s) where A : ICriticalNotifyCompletion where S : IAsyncStateMachine => inner.AwaitUnsafeOnCompleted(ref a, ref s);
    }
    public struct UniTaskVoidBuilder
    {
        AsyncTaskMethodBuilder inner;
        public static UniTaskVoidBuilder Create() => new UniTaskVoidBuilder { inner = AsyncTaskMethodBuilder.Create() };
        public UniTaskVoid Task => default;
        public void SetResult() => inner.SetResult();
        public void SetException(Exception e) { Forgotten.Faults.Add(e); inner.SetResult(); }
        public void SetStateMachine(IAsyncStateMachine s) => inner.SetStateMachine(s);
        public void Start<T>(ref T s) where T : IAsyncStateMachine => inner.Start(ref s);
        public void AwaitOnCompleted<T, S>(ref T a, ref S s) where T : INotifyCompletion where S : IAsyncStateMachine => inner.AwaitOnCompleted(ref a, ref s);
        public void AwaitUnsafeOnCompleted<T, S>(ref T a, ref S s) where T : ICriticalNotifyCompletion where S : IAsyncStateMachine => inner.AwaitUnsafeOnCompleted(ref a, ref s);
    }
    public static class UniTaskExtensions
    {
        public static UniTask AsUniTask(this Task task) => new UniTask(task);
        public static UniTask<T> AsUniTask<T>(this Task<T> task) => new UniTask<T>(task);
        public static UniTask<T> AttachExternalCancellation<T>(this UniTask<T> task, CancellationToken token) => task;
    }
}
static class Forgotten
{
    public static readonly List<Exception> Faults = new List<Exception>();
    public static void Observe(Task task) => task.ContinueWith(t => Faults.Add(t.Exception),
        CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
}
static class FakeClock
{
    sealed class Wait
    {
        public double Due;
        public CancellationToken Token;
        public TaskCompletionSource<bool> Source = new TaskCompletionSource<bool>();
        public CancellationTokenRegistration Registration;
    }
    static readonly List<Wait> waits = new List<Wait>();
    public static double Now;
    public static bool ImmediateCancellation;
    public static int ActiveCount => waits.Count(w => !w.Token.IsCancellationRequested);
    public static readonly List<double> Delays = new List<double>();
    public static readonly List<Cysharp.Threading.Tasks.DelayType> DelayTypes = new List<Cysharp.Threading.Tasks.DelayType>();
    public static Task Delay(double seconds, CancellationToken token, Cysharp.Threading.Tasks.DelayType type)
    {
        Delays.Add(seconds); DelayTypes.Add(type);
        var wait = new Wait { Due = Now + seconds, Token = token };
        waits.Add(wait);
        if (ImmediateCancellation)
            wait.Registration = token.Register(() => { waits.Remove(wait); wait.Source.TrySetCanceled(); });
        return wait.Source.Task;
    }
    public static void Advance(double seconds)
    {
        Now += seconds;
        foreach (var wait in waits.ToArray())
            if (wait.Token.IsCancellationRequested && waits.Remove(wait))
            { wait.Registration.Dispose(); wait.Source.TrySetCanceled(); }
            else if (wait.Due <= Now && waits.Remove(wait))
            { wait.Registration.Dispose(); wait.Source.TrySetResult(true); }
    }
    public static void Reset() { waits.Clear(); Delays.Clear(); DelayTypes.Clear(); Now = 0; ImmediateCancellation = false; }
}
namespace UnityEngine
{
    public static class Mathf { public static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value)); }
    public static class Debug { public static void Log(object value) { } public static void LogWarning(object value) { } public static void LogError(object value) { } }
    public static class JsonUtility
    {
        // Snapshot DTO fields to preserve persistence semantics without a JSON package.
        static readonly Dictionary<string, object> snapshots = new Dictionary<string, object>();
        static object Clone(object value)
        {
            if (value == null || value is string || value.GetType().IsValueType) return value;
            if (value is Array array) return array.Clone();
            object copy = Activator.CreateInstance(value.GetType(), true);
            if (value is IList list) { foreach (object item in list) ((IList)copy).Add(Clone(item)); }
            else foreach (FieldInfo field in value.GetType().GetFields()) field.SetValue(copy, Clone(field.GetValue(value)));
            return copy;
        }
        public static string ToJson(object value) { string key = Guid.NewGuid().ToString(); snapshots[key] = Clone(value); return key; }
        public static T FromJson<T>(string value) => (T)Clone(snapshots[value]);
    }
}
#endif
namespace Firebase { public sealed class FirebaseApp { public static readonly FirebaseApp DefaultInstance = new FirebaseApp(); } }
namespace Firebase.Functions
{
    public enum FunctionsErrorCode { None, InvalidArgument, AlreadyExists, PermissionDenied, FailedPrecondition, Unauthenticated, Unavailable }
    public sealed class FunctionsException : Exception
    {
        public FunctionsErrorCode ErrorCode { get; }
        public FunctionsException(FunctionsErrorCode code) : base(code.ToString()) { ErrorCode = code; }
    }
    public sealed class HttpsCallableResult { public object Data; }
    public sealed class HttpsCallableReference
    { public Task<HttpsCallableResult> CallAsync(object payload) => FakeServer.Call(payload); }
    public sealed class FirebaseFunctions
    {
        public static FirebaseFunctions GetInstance(Firebase.FirebaseApp app, string region) => new FirebaseFunctions();
        public HttpsCallableReference GetHttpsCallable(string name)
        { if (name != "submitMatchResult") throw new Exception("Unexpected function: " + name); return new HttpsCallableReference(); }
    }
}
static class FakeServer
{
    public static readonly List<TaskCompletionSource<Firebase.Functions.HttpsCallableResult>> Calls = new List<TaskCompletionSource<Firebase.Functions.HttpsCallableResult>>();
    public static readonly List<Dictionary<string, object>> Payloads = new List<Dictionary<string, object>>();
    public static Task<Firebase.Functions.HttpsCallableResult> Call(object payload)
    {
        Payloads.Add((Dictionary<string, object>)payload);
        var source = new TaskCompletionSource<Firebase.Functions.HttpsCallableResult>(); Calls.Add(source); return source.Task;
    }
    public static void Reply(string status, int index = -1) => Calls[index < 0 ? Calls.Count - 1 : index]
        .SetResult(new Firebase.Functions.HttpsCallableResult { Data = new Dictionary<string, object> { ["status"] = status } });
    public static void Fail(Firebase.Functions.FunctionsErrorCode code, int index = -1) => Calls[index < 0 ? Calls.Count - 1 : index]
        .SetException(new Firebase.Functions.FunctionsException(code));
    public static void Reset() { Calls.Clear(); Payloads.Clear(); }
}
static class LocalPrefs
{
    public static readonly Dictionary<string, string> Values = new Dictionary<string, string>();
    public static string GetString(string key, string fallback) => Values.TryGetValue(key, out string value) ? value : fallback;
    public static void SetString(string key, string value) => Values[key] = value;
    public static void DeleteKey(string key) => Values.Remove(key);
    public static void Save() { }
}
sealed class FirebaseAuthService
{
    public static readonly FirebaseAuthService Instance = new FirebaseAuthService();
    public bool IsCurrentUserActive = true;
    public string UserId = "user", State = "test", LastError;
    public TaskCompletionSource<bool> Initialization;
    public Cysharp.Threading.Tasks.UniTask InitializeAsync() => Cysharp.Threading.Tasks.UniTaskExtensions.AsUniTask(Initialization?.Task ?? Task.CompletedTask);
}
static class FirebaseManager { public static CancellationToken Lifetime => CancellationToken.None; }
sealed class MultiplayerTurnRunner { public static MultiplayerTurnRunner Instance; public string MatchId, SeedSource; }
sealed class NetworkGameController
{
    public static NetworkGameController Instance;
    public string LocalDeckHash, OpponentDeckHash;
    public ulong FinalStateHash, StateHashChain, StateHashChainPrev;
    public int StateHashChainLength;
}
static class DeckConfig { public static bool IsMultiplayer; }
static class SoloMatchHandoff
{
    public static string MatchId = "match-1";
    public static bool TryGetResultIdentity(out string matchId, out string deck, out string opponent)
    { matchId = MatchId; deck = "deck"; opponent = "opponent"; return true; }
}
static class SpecSource { public static string BattleFingerprint = "fingerprint"; }
static class BattleCommandLog { public static string SerializeBase64() => "log"; public static string HashHex() => "hash"; public static int Count = 1; public static bool IsTruncated; }
static class BattleBoardOrder { public static int[] For(int player) => new[] { 1, 2 }; }
static class PlayerSaveCloud { public static int UploadCountThisSession; }
static class MissionCommands { public static int Invalidations; public static void Invalidate() { Invalidations++; } }
static class MatchResultFailurePopup { public static int Shown; public static void Show() { Shown++; } }
static class PayoutInbox
{
    public static int Retries;
    public static void RetryPending() { Retries++; }
    public static void RefreshOnResume() { }
    public static void Initialize(string env) { }
    public static void Shutdown() { }
    public static Cysharp.Threading.Tasks.UniTask FlushAsync() => Cysharp.Threading.Tasks.UniTask.CompletedTask;
}
struct FirebaseContext { public string EnvId; }
interface IFirebaseModule
{
    void Initialize(in FirebaseContext context);
    void RetryPending();
    Cysharp.Threading.Tasks.UniTask FlushPendingAsync();
    void Shutdown();
}

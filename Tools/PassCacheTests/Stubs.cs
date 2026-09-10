// Standalone dependency stubs. The actual pass commands, manager and DTOs are compiled unchanged.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

#if !REAL_UNITY
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class)]
    public sealed class AsyncMethodBuilderAttribute : Attribute
    {
        public AsyncMethodBuilderAttribute(Type builderType) { }
    }
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
        public static UniTask<T> FromResult<T>(T value) => new UniTask<T>(System.Threading.Tasks.Task.FromResult(value));
        public static UniTask CompletedTask => new UniTask(Task.CompletedTask);
        public static UniTask WaitUntil(Func<bool> predicate, System.Threading.CancellationToken cancellationToken = default) => new UniTask(FakeClock.WaitUntil(predicate));
        public static UniTask Delay(TimeSpan delay, DelayType type = DelayType.DeltaTime)
        {
            FakeClock.DelayTypes.Add(type);
            return new UniTask(FakeClock.Delay(delay.TotalSeconds));
        }
        public void Forget() { }
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
        AsyncVoidMethodBuilder inner;
        public static UniTaskVoidBuilder Create() => new UniTaskVoidBuilder { inner = AsyncVoidMethodBuilder.Create() };
        public UniTaskVoid Task => default;
        public void SetResult() => inner.SetResult();
        public void SetException(Exception e) => inner.SetException(e);
        public void SetStateMachine(IAsyncStateMachine s) => inner.SetStateMachine(s);
        public void Start<T>(ref T s) where T : IAsyncStateMachine => inner.Start(ref s);
        public void AwaitOnCompleted<T, S>(ref T a, ref S s) where T : INotifyCompletion where S : IAsyncStateMachine => inner.AwaitOnCompleted(ref a, ref s);
        public void AwaitUnsafeOnCompleted<T, S>(ref T a, ref S s) where T : ICriticalNotifyCompletion where S : IAsyncStateMachine => inner.AwaitUnsafeOnCompleted(ref a, ref s);
    }
    public sealed class UniTaskCompletionSource
    {
        readonly TaskCompletionSource<bool> inner = new TaskCompletionSource<bool>();
        public UniTask Task => new UniTask(inner.Task);
        public bool TrySetResult() => inner.TrySetResult(true);
    }
    public sealed class UniTaskCompletionSource<T>
    {
        readonly TaskCompletionSource<T> inner = new TaskCompletionSource<T>();
        public UniTask<T> Task => new UniTask<T>(inner.Task);
        public bool TrySetResult(T value) => inner.TrySetResult(value);
    }
}

static class FakeClock
{
    static readonly List<Tuple<Func<bool>, TaskCompletionSource<bool>>> waits = new List<Tuple<Func<bool>, TaskCompletionSource<bool>>>();
    public static readonly List<Cysharp.Threading.Tasks.DelayType> DelayTypes = new List<Cysharp.Threading.Tasks.DelayType>();
    public static Task WaitUntil(Func<bool> predicate, System.Threading.CancellationToken cancellationToken = default)
    {
        if (predicate()) return Task.CompletedTask;
        var source = new TaskCompletionSource<bool>();
        waits.Add(Tuple.Create(predicate, source));
        return source.Task;
    }
    public static Task Delay(double seconds)
    {
        double due = UnityEngine.Time.realtimeSinceStartupAsDouble + seconds;
        return WaitUntil(() => UnityEngine.Time.realtimeSinceStartupAsDouble >= due);
    }
    public static void Advance(double seconds)
    {
        UnityEngine.Time.realtimeSinceStartupAsDouble += seconds;
        foreach (var wait in waits.ToArray())
            if (wait.Item1()) { waits.Remove(wait); wait.Item2.SetResult(true); }
    }
    public static void Reset() { waits.Clear(); DelayTypes.Clear(); UnityEngine.Time.realtimeSinceStartupAsDouble = 0; }
}
namespace UnityEngine
{
    public static class Time { public static double realtimeSinceStartupAsDouble; }
    public enum RuntimeInitializeLoadType { SubsystemRegistration }
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute { public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType type) { } }
    public static class Debug { public static void Log(object value) { } public static void LogException(Exception value) { } public static void LogWarning(object value) { } public static void LogError(object value) { } }
    public static class JsonUtility
    {
        // Only the private AppliedStore persistence DTO is used in these tests.
        public static string ToJson(object value) => string.Join("|", (List<string>)value.GetType().GetField("matchIds").GetValue(value));
        public static T FromJson<T>(string value)
        {
            T result = (T)Activator.CreateInstance(typeof(T), true);
            typeof(T).GetField("matchIds").SetValue(result, value.Split('|').ToList());
            return result;
        }
    }
}
#endif
namespace Newtonsoft.Json
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    sealed class JsonPropertyAttribute : Attribute { public JsonPropertyAttribute(string name) { } }
}
sealed class ContentProfileConfig
{
    public static ContentProfileConfig Active = new ContentProfileConfig();
    public string CloudEnvId = "test";
}
static class FirebaseManager { public static System.Threading.CancellationToken Lifetime => default; }
static class RankManager { public static long Points; }
class ServerCommandResult { }
sealed class ClaimRewardItem { public long Amount { get; set; } }
sealed class ClaimRewardGain { public long Amount { get; set; } }
sealed class ClaimRewardPack { }
sealed class OpenPackCard { }
sealed class ServerCommandRejectedException : Exception { public string Reason = "rejected"; }
sealed class ServerAdoptionException : Exception { }
static class TestTask
{
    public static Cysharp.Threading.Tasks.UniTask<T> Wrap<T>(Task<T> task)
    {
#if REAL_UNITY
        return Cysharp.Threading.Tasks.UniTaskExtensions.AsUniTask(task);
#else
        return new Cysharp.Threading.Tasks.UniTask<T>(task);
#endif
    }
}
static class ServerSaveCommands
{
    public static readonly Queue<TaskCompletionSource<PassGetResponse>> Reads = new Queue<TaskCompletionSource<PassGetResponse>>();
    public static readonly Queue<TaskCompletionSource<ClaimPassRewardResult>> Claims = new Queue<TaskCompletionSource<ClaimPassRewardResult>>();
    public static int ReadCalls, ClaimCalls;
    public static Cysharp.Threading.Tasks.UniTask<T> InvokeReadOnlyAsync<T>(string command, object payload)
    {
        if (command != "getPass") throw new Exception("Unexpected query " + command);
        ReadCalls++;
        return TestTask.Wrap((Task<T>)(object)Reads.Dequeue().Task);
    }
    public static Cysharp.Threading.Tasks.UniTask<T> InvokeAsync<T>(string command, object payload)
    {
        if (command != "claimPassReward") throw new Exception("Unexpected mutation " + command);
        ClaimCalls++;
        return TestTask.Wrap((Task<T>)(object)Claims.Dequeue().Task);
    }
    public static TaskCompletionSource<PassGetResponse> QueueRead()
    {
        var source = new TaskCompletionSource<PassGetResponse>(); Reads.Enqueue(source); return source;
    }
    public static TaskCompletionSource<ClaimPassRewardResult> QueueClaim()
    {
        var source = new TaskCompletionSource<ClaimPassRewardResult>(); Claims.Enqueue(source); return source;
    }
    public static void Reset() { Reads.Clear(); Claims.Clear(); ReadCalls = ClaimCalls = 0; }
}

// Standalone dependencies only. PayoutInbox.cs itself is compiled without copying its policy.
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
        public static UniTask CompletedTask => new UniTask(Task.CompletedTask);
        public static UniTask WaitUntil(Func<bool> predicate) => new UniTask(FakeClock.WaitUntil(predicate));
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
}

static class FakeClock
{
    static readonly List<Tuple<Func<bool>, TaskCompletionSource<bool>>> waits = new List<Tuple<Func<bool>, TaskCompletionSource<bool>>>();
    public static readonly List<Cysharp.Threading.Tasks.DelayType> DelayTypes = new List<Cysharp.Threading.Tasks.DelayType>();
    public static Task WaitUntil(Func<bool> predicate)
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
    public static class Debug { public static void LogWarning(object value) { } public static void LogError(object value) { } }
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
static class LocalPrefs
{
    public static readonly Dictionary<string, string> Values = new Dictionary<string, string>();
    public static string GetString(string key, string fallback) => Values.TryGetValue(key, out string value) ? value : fallback;
    public static void SetString(string key, string value) => Values[key] = value;
    public static void DeleteKey(string key) => Values.Remove(key);
    public static void Save() { }
}
static class SaveDependentManagersStep { public static bool IsInstalled = true; }
static class GameInitialization { public static bool IsTerminated; }
static class MatchResultSubmission
{
    public static bool SignedIn = true;
    public static Cysharp.Threading.Tasks.UniTask<bool> EnsureSignedIn() => TestTask.Wrap(Task.FromResult(SignedIn));
}
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
sealed class PayoutListResult { public List<PayoutEntry> Payouts; }
sealed class PayoutAckResult { public List<string> Acked; }
sealed class PayoutEntry
{
    public string MatchId;
    public long RankSequence, SettledAtMs;
    public PayoutCurrency Currency;
    public PayoutRank Rank;
    public RankProgressResult RankProgress;
}
sealed class PayoutCurrency { public string Currency = "gold"; public int Amount = 10; }
sealed class PayoutRank { public int Before = 1, After = 2; }
sealed class RankProgressResult { public string SeasonId; public int BestTierIndex; }
struct RankApplyResult { }
enum ECurrencyType { Gold }
struct CurrencyGain
{
    public static CurrencyGain None => default;
    public int Amount;
    public CurrencyGain(ECurrencyType type, int amount) { Amount = amount; }
}
static class CurrencyCode { public static bool TryParse(string value, out ECurrencyType type) { type = ECurrencyType.Gold; return value == "gold"; } }
static class RankManager
{
    public static bool IsConfigured = true;
    public static int Applied;
    public static RankApplyResult ApplyServerPayout(int before, int after, string season, int bestTier, object ignored) { Applied++; return default; }
}
static class DataSaveManager { public static void SaveImmediate() { } }
static class MissionCommands { public static int Invalidations; public static void Invalidate() { Invalidations++; } }
static class RankResultHandoff { public static void Set(RankApplyResult value) { } }
static class BattleRewardHandoff { public static int Amount; public static void Set(CurrencyGain value) { Amount += value.Amount; } }
static class ServerSaveCommands
{
    public static readonly Queue<TaskCompletionSource<PayoutListResult>> Lists = new Queue<TaskCompletionSource<PayoutListResult>>();
    public static readonly Queue<TaskCompletionSource<PayoutAckResult>> Acks = new Queue<TaskCompletionSource<PayoutAckResult>>();
    public static int ListCalls, AckCalls;
    public static Cysharp.Threading.Tasks.UniTask<T> InvokeReadOnlyAsync<T>(string command, object payload)
    {
        if (typeof(T) != typeof(PayoutListResult)) throw new Exception("Unexpected read DTO");
        ListCalls++;
        return TestTask.Wrap((Task<T>)(object)Lists.Dequeue().Task);
    }
    public static Cysharp.Threading.Tasks.UniTask<T> InvokeAsync<T>(string command, object payload)
    {
        if (typeof(T) != typeof(PayoutAckResult)) throw new Exception("Unexpected write DTO");
        AckCalls++;
        return TestTask.Wrap((Task<T>)(object)Acks.Dequeue().Task);
    }
    public static TaskCompletionSource<PayoutListResult> QueueList()
    {
        var source = new TaskCompletionSource<PayoutListResult>(); Lists.Enqueue(source); return source;
    }
    public static TaskCompletionSource<PayoutAckResult> QueueAck()
    {
        var source = new TaskCompletionSource<PayoutAckResult>(); Acks.Enqueue(source); return source;
    }
    public static void Reset() { Lists.Clear(); Acks.Clear(); ListCalls = AckCalls = 0; }
}

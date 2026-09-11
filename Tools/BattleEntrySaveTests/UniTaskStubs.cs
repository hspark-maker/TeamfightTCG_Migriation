using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class)]
    public sealed class AsyncMethodBuilderAttribute : Attribute
    {
        public AsyncMethodBuilderAttribute(Type type) { }
    }
}
namespace Cysharp.Threading.Tasks
{
    [AsyncMethodBuilder(typeof(UniTaskBuilder))]
    public struct UniTask
    {
        internal Task Inner;
        internal UniTask(Task task) { Inner = task; }
        public TaskAwaiter GetAwaiter() => (Inner ?? Task.CompletedTask).GetAwaiter();
        public static UniTask CompletedTask => new UniTask(Task.CompletedTask);
        public static UniTask WaitUntil(Func<bool> predicate, CancellationToken cancellationToken = default)
            => new UniTask(FakePlayerLoop.WaitUntil(predicate, cancellationToken));
        public UniTask AttachExternalCancellation(CancellationToken token)
            => new UniTask(FakePlayerLoop.WithCancellation(Inner ?? Task.CompletedTask, token));
        public void Forget() { }
    }
    [AsyncMethodBuilder(typeof(UniTaskBuilder<>))]
    public struct UniTask<T>
    {
        internal Task<T> Inner;
        internal UniTask(Task<T> task) { Inner = task; }
        public TaskAwaiter<T> GetAwaiter() => Inner.GetAwaiter();
    }
    public struct UniTaskBuilder
    {
        AsyncTaskMethodBuilder inner;
        public static UniTaskBuilder Create() => new UniTaskBuilder { inner = AsyncTaskMethodBuilder.Create() };
        public UniTask Task => new UniTask(inner.Task);
        public void SetResult() => inner.SetResult();
        public void SetException(Exception e) => inner.SetException(e);
        public void SetStateMachine(IAsyncStateMachine s) => inner.SetStateMachine(s);
        public void Start<S>(ref S s) where S : IAsyncStateMachine => inner.Start(ref s);
        public void AwaitOnCompleted<A, S>(ref A a, ref S s) where A : INotifyCompletion where S : IAsyncStateMachine => inner.AwaitOnCompleted(ref a, ref s);
        public void AwaitUnsafeOnCompleted<A, S>(ref A a, ref S s) where A : ICriticalNotifyCompletion where S : IAsyncStateMachine => inner.AwaitUnsafeOnCompleted(ref a, ref s);
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
    public sealed class UniTaskCompletionSource
    {
        readonly TaskCompletionSource<bool> inner = new TaskCompletionSource<bool>();
        public UniTask Task => new UniTask(inner.Task);
        public bool TrySetResult() => inner.TrySetResult(true);
    }
}
static class FakePlayerLoop
{
    sealed class Wait
    {
        internal Func<bool> Predicate;
        internal TaskCompletionSource<bool> Completion = new TaskCompletionSource<bool>();
        internal CancellationTokenRegistration Registration;
    }
    static readonly List<Wait> waits = new List<Wait>();
    internal static Task WaitUntil(Func<bool> predicate, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (predicate()) return Task.CompletedTask;
        var wait = new Wait { Predicate = predicate };
        wait.Registration = token.Register(() => wait.Completion.TrySetCanceled());
        waits.Add(wait);
        return wait.Completion.Task;
    }
    internal static async Task WithCancellation(Task task, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!token.CanBeCanceled) { await task; return; }
        var cancel = new TaskCompletionSource<bool>();
        using (token.Register(() => cancel.TrySetCanceled()))
        {
            Task winner = await Task.WhenAny(task, cancel.Task);
            await winner;
        }
    }
    internal static void Tick()
    {
        foreach (Wait wait in waits.ToArray())
        {
            if (!wait.Completion.Task.IsCompleted && !wait.Predicate()) continue;
            waits.Remove(wait);
            wait.Registration.Dispose();
            wait.Completion.TrySetResult(true);
        }
    }
    internal static void Reset()
    {
        foreach (Wait wait in waits) wait.Registration.Dispose();
        waits.Clear();
    }
}

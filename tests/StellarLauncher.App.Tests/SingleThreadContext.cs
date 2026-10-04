using System.Collections.Concurrent;

/// <summary>A single-threaded SynchronizationContext pump — the shape of a UI thread: every continuation that
/// captured it runs on its ONE thread, so blocking that thread blocks them all. <see cref="Run"/> starts a
/// delegate on it and returns a task for its result; the thread is a background thread, so a deadlocked pump
/// never keeps the test process alive.</summary>
public sealed class SingleThreadContext : SynchronizationContext, IDisposable
{
    private readonly BlockingCollection<(SendOrPostCallback Cb, object? State)> _queue = new();
    private readonly Thread _thread;

    public SingleThreadContext()
    {
        _thread = new Thread(() =>
        {
            SetSynchronizationContext(this);
            foreach (var (cb, state) in _queue.GetConsumingEnumerable()) cb(state);
        }) { IsBackground = true, Name = "test-ui-thread" };
        _thread.Start();
    }

    public int ManagedThreadId => _thread.ManagedThreadId;

    public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));
    public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException();

    /// <summary>Runs <paramref name="start"/> on the pump's thread; the returned task completes with it.</summary>
    public Task<T> Run<T>(Func<Task<T>> start)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(async _ =>
        {
            try { tcs.SetResult(await start()); }
            catch (Exception ex) { tcs.SetException(ex); }
        }, null);
        return tcs.Task;
    }

    public Task Run(Func<Task> start) => Run(async () => { await start(); return true; });

    public void Dispose() => _queue.CompleteAdding();
}

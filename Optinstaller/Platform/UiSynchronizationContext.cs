using System;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Optinstaller.Platform;

public sealed class UiSynchronizationContext : SynchronizationContext
{
    private readonly ConcurrentQueue<WorkItem> _queue = new();
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;

    public override void Post(SendOrPostCallback d, object? state)
    {
        _queue.Enqueue(new WorkItem(d, state));
    }

    public override void Send(SendOrPostCallback d, object? state)
    {
        if (Environment.CurrentManagedThreadId == _ownerThreadId)
        {
            d(state);
            return;
        }

        using var signal = new ManualResetEventSlim(false);
        var item = new WorkItem(d, state, signal);
        _queue.Enqueue(item);
        signal.Wait();

        if (item.Exception != null)
        {
            ExceptionDispatchInfo.Capture(item.Exception).Throw();
        }
    }

    public void Pump()
    {
        while (_queue.TryDequeue(out var item))
        {
            try
            {
                item.Callback(item.State);
            }
            catch (Exception ex)
            {
                item.Exception = ex;
            }
            finally
            {
                item.Signal?.Set();
            }
        }
    }

    private sealed class WorkItem
    {
        public WorkItem(SendOrPostCallback callback, object? state, ManualResetEventSlim? signal = null)
        {
            Callback = callback;
            State = state;
            Signal = signal;
        }

        public SendOrPostCallback Callback { get; }

        public object? State { get; }

        public ManualResetEventSlim? Signal { get; }

        public Exception? Exception { get; set; }
    }
}

using System.Collections.Concurrent;

namespace NAP.Core;

/// <summary>All workflow mutations, including manual work, execute on this universe's owning thread.
/// Delegates are an in-process composition API; they are never accepted by the wire protocol.</summary>
public sealed class AutomationUniverseRuntime : IAsyncDisposable
{
    private sealed record Work(Action<AutomationUniverseScope> Execute, Action<Exception> Fail);
    private readonly BlockingCollection<Work> _mailbox;
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread _thread;
    private readonly object _gate = new();
    private bool _closing;
    public UniverseContext Context { get; }
    public Task Started => _started.Task;
    public Task Completion => _stopped.Task;

    public AutomationUniverseRuntime(UniverseContext context, int capacity = 64)
    {
        if (capacity is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(capacity));
        Context = context; _mailbox = new(capacity);
        _thread = new(Run) { IsBackground = true, Name = "NAP owner " + context.Id.Value };
        _thread.Start();
    }

    public Task<T> InvokeAsync<T>(Func<AutomationUniverseScope, T> operation, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_closing) return Task.FromException<T>(new InvalidOperationException("Universe is draining."));
            if (!_mailbox.TryAdd(new(scope =>
            {
                if (cancellation.IsCancellationRequested) { result.TrySetCanceled(cancellation); return; }
                try { result.TrySetResult(operation(scope)); }
                catch (Exception error) { result.TrySetException(error); }
            }, error => result.TrySetException(error)))) return Task.FromException<T>(new AutomationException(AutomationError.Busy, "Universe mailbox is full."));
        }
        return result.Task;
    }

    private void Run()
    {
        Exception? failure = null;
        try
        {
            var queue = new AutomationQueueStore(Context.Storage);
            using var owner = queue.AcquireOwner();
            var scope = new AutomationUniverseScope(Context, queue, owner);
            _started.TrySetResult();
            foreach (var action in _mailbox.GetConsumingEnumerable()) action.Execute(scope);
        }
        catch (Exception error) { failure = error; _started.TrySetException(error); }
        finally
        {
            lock (_gate) { _closing = true; _mailbox.CompleteAdding(); }
            // Failed startup must complete queued callers, without giving them a fake owner.
            while (_mailbox.TryTake(out var abandoned)) abandoned.Fail(failure ?? new InvalidOperationException("Universe stopped."));
            if (failure is null) _stopped.TrySetResult(); else _stopped.TrySetException(failure);
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate) { _closing = true; _mailbox.CompleteAdding(); }
        await Completion.ConfigureAwait(false); // Never abandon a live owner on a timeout.
    }
}

public sealed class AutomationUniverseScope
{
    private readonly UniverseContext? _context;
    private readonly AutomationQueueStore? _queue;
    private readonly AutomationQueueOwner? _owner;
    internal AutomationUniverseScope(UniverseContext context, AutomationQueueStore queue, AutomationQueueOwner owner)
    { _context = context; _queue = queue; _owner = owner; }
    public UniverseContext Context { get { Require(); return _context!; } }
    public AutomationQueueStore Queue { get { Require(); return _queue!; } }
    public AutomationQueueOwner Owner { get { Require(); return _owner!; } }
    private void Require()
    {
        _owner!.Require(_context!.Storage.StateRoot);
    }
}

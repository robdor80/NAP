namespace NAP.Core;

public enum InboxRescanReason { Requested, WatcherNotification, WatcherOverflow }

/// <summary>Reusable admission monitor, deliberately not composed in App. One dedicated thread owns all
/// universe leases; watcher callbacks only signal reconciliation and never access the SQLite store.</summary>
public sealed class InboxAdmissionMonitor : IDisposable
{
    private readonly UniverseContext[] _contexts;
    private readonly InboxAdmissionOptions _options;
    private readonly CancellationTokenSource _stop = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly ManualResetEventSlim _started = new();
    private readonly Dictionary<UniverseId, Scope> _scopes;
    private readonly object _gate = new();
    private Thread? _thread;
    private bool _disposed;
    public int MaximumPendingObserved { get; private set; }
    private long _admittedCount;
    public long AdmittedCount => Interlocked.Read(ref _admittedCount);
    public InboxAdmissionMonitor(IEnumerable<UniverseContext> contexts, InboxAdmissionOptions? options = null)
    {
        _contexts = contexts.ToArray(); _options = options ?? new(); _options.Validate();
        LocalUniverseSettingsStore.ValidateStructure(_contexts.Select(c => c.Storage));
        _scopes = _contexts.ToDictionary(c => c.Id, c => new Scope(c));
    }
    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _thread is not null) throw new InvalidOperationException("Admission monitor can start only once.");
            _thread = new Thread(Run) { IsBackground = true, Name = "NAP Inbox admission" }; _thread.Start();
        }
        _started.Wait();
    }
    public IReadOnlyList<InboxMonitorStatus> Status()
    { lock (_gate) return _scopes.Values.Select(s => s.Status).ToArray(); }
    public void RequestRescan(UniverseId universe, InboxRescanReason reason = InboxRescanReason.Requested)
    {
        lock (_gate)
        {
            if (_disposed) return;
            var scope = _scopes[universe]; scope.Rescan = true; scope.SignalUtc = DateTimeOffset.UtcNow;
            if (reason == InboxRescanReason.WatcherOverflow) scope.Status = scope.Status with { WatcherErrors = scope.Status.WatcherErrors + 1 };
        }
        _wake.Set();
    }
    private void Run()
    {
        try
        {
            foreach (var scope in _scopes.Values)
            {
                try
                {
                    scope.Queue = new(scope.Context.Storage); scope.Owner = scope.Queue.AcquireOwner();
                    scope.Admission = new(scope.Context, scope.Queue, _options);
                    InboxAdmissionEvidence.DirectoryBoundary(scope.Context.Storage.InboxRoot);
                    // Reconcile reserved attempts even when their mutable Inbox source disappeared.
                    for (var offset = 0; ; offset += 100)
                    {
                        var items = scope.Queue.List(offset, 100);
                        foreach (var item in items.Where(i => i.State is QueueState.Observed or QueueState.WaitingStable or QueueState.Queued or QueueState.MissingSource))
                            scope.Admission.Reconcile(scope.Owner, item.Id, _stop.Token);
                        if (items.Count < 100) break;
                    }
                    var watcher = new FileSystemWatcher(scope.Context.Storage.InboxRoot) { IncludeSubdirectories = false,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite, Filter = "*", InternalBufferSize = 8192 };
                    watcher.Created += Changed; watcher.Changed += Changed; watcher.Deleted += Changed;
                    watcher.Renamed += (_, _) => RequestRescan(scope.Context.Id, InboxRescanReason.WatcherNotification);
                    watcher.Error += (_, _) => RequestRescan(scope.Context.Id, InboxRescanReason.WatcherOverflow);
                    void Changed(object sender, FileSystemEventArgs args) => RequestRescan(scope.Context.Id, InboxRescanReason.WatcherNotification);
                    scope.Watcher = watcher; watcher.EnableRaisingEvents = true;
                    SetStatus(scope, scope.Status with { State = InboxMonitorState.Monitoring });
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
                catch (Exception e) { Block(scope, e); }
            }
            _started.Set();
            while (!_stop.IsCancellationRequested)
            {
                var worked = false;
                foreach (var scope in _scopes.Values)
                {
                    if (scope.Status.State != InboxMonitorState.Monitoring) continue;
                    try
                    {
                        InboxAdmissionEvidence.DirectoryBoundary(scope.Context.Storage.InboxRoot);
                        var now = DateTimeOffset.UtcNow;
                        lock (_gate)
                        {
                            if (scope.Scan is null && (now >= scope.NextScan || scope.Rescan && now - scope.SignalUtc >= _options.Debounce))
                            {
                                scope.Scan = new InboxPackageDetector().Enumerate(scope.Context.Storage.InboxRoot).GetEnumerator();
                                scope.Rescan = false; scope.NextScan = now + _options.RescanInterval;
                                scope.Status = scope.Status with { Scans = scope.Status.Scans + 1 };
                            }
                        }
                        if (scope.Scan is not null)
                        {
                            while (scope.Pending.Count < _options.MaxPendingCandidates)
                            {
                                if (!scope.Scan.MoveNext()) { scope.Scan.Dispose(); scope.Scan = null; break; }
                                scope.Pending.Enqueue(scope.Scan.Current);
                            }
                        }
                        MaximumPendingObserved = Math.Max(MaximumPendingObserved, scope.Pending.Count);
                        if (scope.Pending.TryDequeue(out var candidate))
                        {
                            worked = true;
                            var result = scope.Admission!.Admit(scope.Owner!, candidate.FullPath, _stop.Token);
                            if (result.Code == InboxAdmissionCode.Admitted) Interlocked.Increment(ref _admittedCount);
                            SetStatus(scope, scope.Status with { LastResult = result, PendingCandidates = scope.Pending.Count });
                        }
                    }
                    catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
                    catch (Exception e) { Block(scope, e); }
                }
                if (!worked) _wake.WaitOne(50);
            }
        }
        finally
        {
            _started.Set();
            foreach (var scope in _scopes.Values)
            {
                scope.Watcher?.Dispose(); scope.Scan?.Dispose(); scope.Owner?.Dispose();
                SetStatus(scope, scope.Status with { State = scope.Status.State == InboxMonitorState.Blocked ? InboxMonitorState.Blocked : InboxMonitorState.Stopped });
            }
        }
    }
    private void SetStatus(Scope scope, InboxMonitorStatus status)
    { lock (_gate) scope.Status = status with { WatcherErrors = Math.Max(scope.Status.WatcherErrors, status.WatcherErrors) }; }
    private void Block(Scope scope, Exception error)
    {
        scope.Watcher?.Dispose(); scope.Watcher = null; scope.Scan?.Dispose(); scope.Scan = null; scope.Pending.Clear();
        var code = error is AutomationException a ? a.Code : error is ProductionStorageException ? AutomationError.UnsafePath : AutomationError.CorruptStore;
        SetStatus(scope, scope.Status with { State = InboxMonitorState.Blocked, PendingCandidates = 0, Error = code });
    }
    public void Dispose()
    {
        Thread? thread;
        lock (_gate) { if (_disposed) return; _disposed = true; thread = _thread; }
        _stop.Cancel(); _wake.Set();
        if (thread is not null && !thread.Join(TimeSpan.FromSeconds(30))) throw new TimeoutException("Admission stop remains pending; ownership has not been stolen.");
        _stop.Dispose(); _wake.Dispose(); _started.Dispose();
    }
    private sealed class Scope(UniverseContext context)
    {
        internal UniverseContext Context { get; } = context;
        internal AutomationQueueStore? Queue; internal AutomationQueueOwner? Owner; internal InboxAdmissionService? Admission;
        internal FileSystemWatcher? Watcher; internal IEnumerator<InboxPackageCandidate>? Scan;
        internal Queue<InboxPackageCandidate> Pending { get; } = new();
        internal bool Rescan = true; internal DateTimeOffset SignalUtc = DateTimeOffset.MinValue, NextScan = DateTimeOffset.MinValue;
        internal InboxMonitorStatus Status = new(context.Id, InboxMonitorState.Starting, 0, 0, 0, null, null);
    }
}

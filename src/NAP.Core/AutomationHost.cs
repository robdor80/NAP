namespace NAP.Core;

public enum AutomationHostState { Starting, Disabled, Running, Pausing, Paused, Stopping, Stopped, Degraded, Blocked }
public sealed record AutomationHostOptions
{
    public required string LaboratoryRoot { get; init; }
    public InboxAdmissionOptions Admission { get; init; } = new();
    public long MaxMemoryBytes { get; init; } = 512L * 1024 * 1024;
    public long MaxRetainedBytes { get; init; } = 4L * 1024 * 1024 * 1024;
    public int PageSize { get; init; } = 32;
}
public sealed record AutomationUniverseStatus(UniverseId UniverseId, AutomationHostState State, long Turns,
    int PendingCandidates, string? Stage, string? Diagnostic, QueueBatchId? BatchId);
public sealed record AutomationHostSnapshot(string? InstallationId, string? SessionId, AutomationHostState State,
    IReadOnlyList<AutomationUniverseStatus> Universes, string? Diagnostic);

/// <summary>Laboratory-only host composition. No default settings, service install, credentials or policy activation.
/// One controller schedules bounded work on dedicated universe owners; HTTP/readiness never own workflow leases.</summary>
public sealed partial class AutomationHost : IAsyncDisposable
{
    private sealed class Slot(UniverseContext context)
    {
        internal readonly UniverseContext Context = context;
        internal AutomationUniverseRuntime? Runtime;
        internal InboxAdmissionService? Admission;
        internal DurablePreparationService? Preparation;
        internal DurableExecutionService? Execution;
        internal WorkflowRunControl? RunControl;
        internal Task<Step>? Work;
        internal FileSystemWatcher? Watcher;
        internal IEnumerator<InboxPackageCandidate>? Scan;
        internal readonly Queue<Task<InboxReadinessObservation>> Probes = new();
        internal DateTimeOffset NextScan;
        internal int Offset;
        internal readonly Queue<QueueItem> Items = new();
        internal QueueBatchId? Batch;
        internal bool ScanRequested = true;
        internal AutomationUniverseStatus Status = new(context.Id, AutomationHostState.Starting, 0, 0, null, null, null);
    }
    private sealed record Step(string Stage, DurableAuditDispatch? Dispatch = null);
    private readonly string _root;
    private readonly AutomationHostOptions _options;
    private readonly Slot[] _slots;
    private readonly IIdentifiedAiAuditClient? _auditor;
    private readonly Thread _controller;
    private readonly object _gate = new();
    private readonly object _controlsGate = new();
    private readonly Dictionary<UniverseId, AutomationHostControl> _controls = new();
    private readonly SemaphoreSlim _heavy = new(1, 1);
    private readonly AutoResetEvent _wake = new(false);
    private readonly CancellationTokenSource _transportStop = new();
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task<AuditTransportOutcome>? _http;
    private Task? _remote;
    private Slot? _auditSlot;
    private DurableAuditDispatch? _dispatch;
    private volatile bool _stopping;
    private AutomationHostState _state = AutomationHostState.Starting;
    private string? _diagnostic, _installation, _session;
    private int _turn;
    private int _auditReserved;
    public Task Started => _started.Task;
    public Task Completion => _stopped.Task;
    public AutomationHostControlStore Controls { get; }
    public string SessionId => _session ?? throw new InvalidOperationException("Host is starting.");

    public AutomationHost(string controlRoot, IEnumerable<UniverseContext> contexts, AutomationHostOptions options,
        IIdentifiedAiAuditClient? auditor = null)
    {
        _root = Path.GetFullPath(controlRoot); _options = options; _auditor = auditor;
        var lab = Path.GetFullPath(options.LaboratoryRoot);
        if (!Path.IsPathFullyQualified(options.LaboratoryRoot) || !Path.GetFileName(Path.TrimEndingDirectorySeparator(lab)).StartsWith("nap-host-", StringComparison.Ordinal) ||
            !ProductionPaths.Within(Path.GetTempPath(), lab) || !ProductionPaths.Within(lab, _root))
            throw AutomationValidation.Invalid("This phase only supports an explicit nap-host-* laboratory below TEMP.");
        InboxAdmissionEvidence.DirectoryBoundary(lab); InboxAdmissionEvidence.DirectoryBoundary(_root);
        _slots = contexts.Select(c => new Slot(c)).ToArray();
        if (_slots.Length is < 1 or > 64 || options.PageSize is < 1 or > 1000 || options.MaxMemoryBytes < 1 || options.MaxRetainedBytes < 1 ||
            _slots.Any(s => new[] { s.Context.Storage.WorkspaceRoot, s.Context.Storage.ProductionRoot, s.Context.Storage.ArchiveRoot }.Any(r => !ProductionPaths.Within(lab, r))))
            throw AutomationValidation.Invalid("All host roots and budgets must belong to the isolated laboratory.");
        options.Admission.Validate();
        Controls = new(_root, _slots.Select(s => s.Context));
        _controller = new(Run) { IsBackground = true, Name = "NAP AutomationHost controller" }; _controller.Start();
    }

    public AutomationHostSnapshot Snapshot()
    { lock (_gate) return new(_installation, _session, _state, _slots.Select(s => s.Status).ToArray(), _diagnostic); }

    public async Task<AutomationHostControl> ActivateAsync(UniverseId universe, PolicyReference policy, bool resumeOnRestart,
        string? commandId = null, CancellationToken cancellation = default)
    {
        await Started.ConfigureAwait(false); var slot = Find(universe);
        await slot.Runtime!.Started.ConfigureAwait(false);
        var grant = await slot.Runtime.InvokeAsync(s =>
        {
            var current = s.Queue.CurrentPolicy();
            if (current.Reference != policy || !current.IsPreproductionAuthorized || current.Authorization is null)
                throw new AutomationException(AutomationError.PolicyMismatch, "Host activation requires the exact currently authorized policy.");
            return new AutomationHostGrant(universe, s.Context.Storage, AutomationHostControlStore.ProfileHash(s.Context), policy,
                current.Authorization.AuthorizationId, current.Authorization.Actor, resumeOnRestart);
        }, cancellation).ConfigureAwait(false);
        return Change(universe, AutomationControlAction.Activate, commandId, grant);
    }

    public AutomationHostControl Control(UniverseId universe, AutomationControlAction action, QueueItemId? item = null,
        string? commandId = null)
    {
        if (action == AutomationControlAction.Activate) throw AutomationValidation.Invalid("Use exact policy activation.");
        return Change(universe, action, commandId, null, item);
    }

    private AutomationHostControl Change(UniverseId universe, AutomationControlAction action, string? id,
        AutomationHostGrant? grant = null, QueueItemId? item = null)
    {
        _ = Find(universe);
        lock (_controlsGate)
        {
            if (_stopping) throw new InvalidOperationException("Host is stopping.");
            if (id is not null && Controls.Replay(id, universe, action, grant, item) is { } replay) return replay;
            var previous = Controls.Read(universe);
            var command = new AutomationControlCommand(id ?? "command_" + Guid.NewGuid().ToString("N"), universe,
                previous.Revision, action, SessionId, grant, item);
            var current = Controls.Apply(command); _controls[universe] = current; _wake.Set(); return current;
        }
    }

    private Slot Find(UniverseId universe) => _slots.SingleOrDefault(s => s.Context.Id == universe)
        ?? throw AutomationValidation.Invalid("Universe is not configured in this host.");

    private bool Allowed(Slot slot, QueueItemId? item = null)
    {
        if (_stopping) return false;
        lock (_controlsGate)
        {
            var control = _controls[slot.Context.Id];
            return !control.Paused && !control.Stopped && control.Grant is { } grant &&
                (grant.ResumeOnRestart || control.StartedSession == SessionId) &&
                (item is null || !control.CancelledItems.Contains(item.Value));
        }
    }

    private void RequireGrant(Slot slot, AutomationUniverseScope scope)
    {
        AutomationHostControl control;
        lock (_controlsGate) { control = Controls.Read(slot.Context.Id); _controls[slot.Context.Id] = control; }
        var policy = scope.Queue.CurrentPolicy();
        if (control.Grant is not { } grant || grant.Policy != policy.Reference || !policy.IsPreproductionAuthorized ||
            policy.Authorization?.AuthorizationId != grant.PolicyAuthorizationId || policy.Authorization.Actor != grant.Actor)
            throw new AutomationException(AutomationError.PolicyMismatch, "Host grant is no longer exact and authorized.");
    }

    private void Run()
    {
        Exception? fatal = null;
        ExecutionMutex? instance = null;
        try
        {
            instance = ExecutionMutex.Acquire("AutomationHost", _root);
            Controls.Initialize(); _installation = Controls.InstallationId; _session = Controls.BeginSession();
            foreach (var slot in _slots)
            {
                lock (_controlsGate) _controls[slot.Context.Id] = Controls.Read(slot.Context.Id);
                slot.Runtime = new(slot.Context, _options.Admission.MaxPendingCandidates);
                var admission = _options.Admission with { MaxRetainedBytes = Math.Min(_options.Admission.MaxRetainedBytes, _options.MaxRetainedBytes / _slots.Length / 2) };
                slot.Admission = new(slot.Context, new(slot.Context.Storage), admission);
                slot.Preparation = new(slot.Context, new(slot.Context.Storage)); slot.Execution = new(slot.Context, new(slot.Context.Storage));
                slot.RunControl = new((item, _) => !Allowed(slot, item));
                slot.Preparation.RunControl = slot.RunControl; slot.Admission.RunControl = slot.RunControl;
            }
            _started.TrySetResult();
            while (!_stopping)
            {
                HarvestAudit();
                for (var n = 0; n < _slots.Length; n++)
                {
                    var slot = _slots[(_turn + n) % _slots.Length];
                    try { Tick(slot); }
                    catch (Exception error) { Block(slot, error); }
                }
                _turn = (_turn + 1) % _slots.Length;
                lock (_gate) _state = _slots.Any(s => s.Status.State == AutomationHostState.Running) ? AutomationHostState.Running :
                    _slots.Any(s => s.Status.State is AutomationHostState.Blocked or AutomationHostState.Degraded) ? AutomationHostState.Degraded : AutomationHostState.Paused;
                _wake.WaitOne(25);
            }
        }
        catch (Exception error) { fatal = error; _started.TrySetException(error); lock (_gate) { _state = AutomationHostState.Blocked; _diagnostic = Code(error); } }
        finally
        {
            _stopping = true; _transportStop.Cancel();
            try
            {
                foreach (var slot in _slots) { slot.Watcher?.Dispose(); slot.Scan?.Dispose(); }
                foreach (var slot in _slots)
                {
                    if (slot.Work is not null) { try { slot.Work.GetAwaiter().GetResult(); } catch { /* its diagnostic is captured below; owner still drains */ } }
                    foreach (var probe in slot.Probes) { try { probe.GetAwaiter().GetResult(); } catch (OperationCanceledException) { } }
                    if (slot.Runtime is not null) { try { slot.Runtime.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch (Exception error) { fatal ??= error; } }
                    Set(slot, AutomationHostState.Stopped, "stopped");
                }
                if (_http is not null) _ = _http.GetAwaiter().GetResult();
            }
            catch (Exception error) { fatal ??= error; }
            instance?.Dispose();
            lock (_gate) { if (fatal is null) _state = AutomationHostState.Stopped; else { _state = AutomationHostState.Blocked; _diagnostic = Code(fatal); } }
            if (fatal is null) _stopped.TrySetResult(); else _stopped.TrySetException(fatal);
        }
    }

    private void Tick(Slot slot)
    {
        if (slot.Runtime!.Started.IsFaulted) { _ = slot.Runtime.Started.Exception; Block(slot, slot.Runtime.Started.Exception!.InnerException!); return; }
        if (!slot.Runtime.Started.IsCompletedSuccessfully || slot.Status.State == AutomationHostState.Blocked) return;
        if (slot.Work is { } work)
        {
            if (!work.IsCompleted) { if (!Allowed(slot)) Set(slot, AutomationHostState.Pausing, "draining"); return; }
            slot.Work = null; var result = work.GetAwaiter().GetResult();
            Set(slot, Allowed(slot) ? AutomationHostState.Running : AutomationHostState.Paused, result.Stage, increment: true);
            if (result.Dispatch is { } dispatch)
            {
                _dispatch = dispatch; _auditSlot = slot;
                _http = AutomationAuditTransport.SendAsync(dispatch, _auditor!, _transportStop.Token);
            }
        }
        if (!Allowed(slot)) { Set(slot, AutomationHostState.Paused, "paused"); return; }
        Set(slot, AutomationHostState.Running, slot.Status.Stage);
        EnsureWatcher(slot);
        if (slot.Batch is null && (slot.ScanRequested || DateTimeOffset.UtcNow >= slot.NextScan))
        {
            slot.ScanRequested = false; slot.NextScan = DateTimeOffset.UtcNow + _options.Admission.RescanInterval;
            slot.Work = slot.Runtime.InvokeAsync(s => { RequireGrant(slot, s); slot.Batch = s.Queue.CreateBatch(s.Owner).Id; return new Step("batch-open"); }); return;
        }
        if (slot.Batch is not null && slot.Scan is null && slot.Probes.Count == 0 && slot.Status.Stage == "batch-open")
            slot.Scan = new InboxPackageDetector().Enumerate(slot.Context.Storage.InboxRoot).GetEnumerator();
        while (slot.Scan is not null && slot.Probes.Count < _options.Admission.MaxPendingCandidates)
        {
            if (!slot.Scan.MoveNext()) { slot.Scan.Dispose(); slot.Scan = null; break; }
            slot.Probes.Enqueue(slot.Admission!.ProbeAsync(slot.Scan.Current.FullPath, _transportStop.Token));
        }
        if (slot.Probes.TryPeek(out var probe) && probe.IsCompleted)
        {
            slot.Probes.Dequeue(); var observation = probe.GetAwaiter().GetResult();
            slot.Work = slot.Runtime.InvokeAsync(s =>
            {
                if (!Allowed(slot)) return new Step("paused"); RequireGrant(slot, s);
                slot.Admission!.AdmitReady(s.Owner, observation, slot.Batch); return new Step("admission");
            }); return;
        }
        if (slot.Batch is { } batch && slot.Scan is null && slot.Probes.Count == 0 && slot.Status.Stage != "batch-open")
        {
            slot.Work = slot.Runtime.InvokeAsync(s => { s.Queue.CloseBatch(s.Owner, batch); return new Step("batch-closed"); }); slot.Batch = null; return;
        }
        if (!_heavy.Wait(0)) return;
        slot.Work = slot.Runtime.InvokeAsync(s =>
        {
            try { return WorkflowStep(slot, s); }
            finally { _heavy.Release(); }
        });
    }

    private Step WorkflowStep(Slot slot, AutomationUniverseScope scope)
    {
        if (!Allowed(slot)) return new("paused"); RequireGrant(slot, scope);
        if (slot.Items.Count == 0)
        {
            var page = scope.Queue.List(slot.Offset, _options.PageSize); slot.Offset += page.Count;
            if (page.Count < _options.PageSize) slot.Offset = 0;
            foreach (var candidate in page) slot.Items.Enqueue(candidate);
        }
        while (slot.Items.TryDequeue(out var queued))
        {
            var item = scope.Queue.Get(queued.Id);
            if (!Allowed(slot, item.Id) || item.State is not (QueueState.Observed or QueueState.WaitingStable or QueueState.Queued or QueueState.Running or QueueState.RetryScheduled or QueueState.MissingSource)) continue;
            if (_dispatch?.ItemId == item.Id && _dispatch.UniverseId == slot.Context.Id) continue;
            if (item.State == QueueState.RetryScheduled)
            { if (item.RetryAtUtc <= DateTimeOffset.UtcNow) scope.Queue.Transition(scope.Owner, item.Id, item.Revision, QueueState.Queued); return new("queue-retry"); }
            if (item.ActiveAttempt is null || item.State is QueueState.Observed or QueueState.WaitingStable or QueueState.MissingSource)
            { slot.Admission!.Reconcile(scope.Owner, item.Id); return new("admission-recovery"); }
            PreparationRecord? record;
            try { record = slot.Preparation!.State.Get(item.Id); } catch (AutomationException error) when (error.Code == AutomationError.Missing) { record = null; }
            var policy = scope.Queue.GetPolicy(item.Policy);
            if (policy.ResourcesTooLarge(_options.MaxMemoryBytes)) return new("resource-wait");
            if (record is null)
            {
                // Queued alone is insufficient; finish only a verifiable admission before preparation.
                slot.Admission!.Reconcile(scope.Owner, item.Id);
                var capacity = _options.MaxRetainedBytes / _slots.Length / 2;
                if (!RetainedAdmissionInventory.Fits(Path.Combine(scope.Context.Storage.StateRoot, "automation"), Math.Max(0, capacity - policy.Definition.Resources.MaxStagingBytes)))
                    return new("storage-wait");
                slot.Preparation!.Prepare(scope.Owner, item.Id); return new("preparation");
            }
            if (item.State == QueueState.Running || record.AuditState == DurableAuditState.Pass && policy.IsExecutionAuthorized)
            { var result = slot.Execution!.Execute(scope.Owner, item.Id, slot.RunControl); return new(result.Code == DurableExecutionCode.Suspended ? "suspended" : "execution"); }
            if (record.RetryAtUtc > DateTimeOffset.UtcNow || record.AuditState == DurableAuditState.Pass) continue;
            if (_auditor is null) return new("audit-unconfigured");
            if (_remote is { IsCompleted: false } || Interlocked.CompareExchange(ref _auditReserved, 1, 0) != 0) continue;
            try
            {
                var step = slot.Preparation!.BeginAudit(scope.Owner, item.Id, _auditor.ProviderId, _auditor.ModelId);
                if (step.Dispatch is null) Interlocked.Exchange(ref _auditReserved, 0);
                return new("audit", step.Dispatch);
            }
            catch { Interlocked.Exchange(ref _auditReserved, 0); throw; }
        }
        return new("idle");
    }

    private void HarvestAudit()
    {
        if (_http is null || !_http.IsCompleted || _auditSlot!.Work is not null) return;
        var outcome = _http.GetAwaiter().GetResult(); var slot = _auditSlot!; var dispatch = _dispatch!;
        _http = null; _remote = outcome.RemoteCompletion;
        slot.Work = slot.Runtime!.InvokeAsync(s =>
        {
            if (outcome.Cancelled) return new Step("audit-remote-unknown");
            if (outcome.Failure is { } failure) slot.Preparation!.FailAudit(s.Owner, dispatch, failure);
            else slot.Preparation!.CompleteAudit(s.Owner, dispatch, outcome.Report!, Allowed(slot, dispatch.ItemId));
            return new Step("audit-response");
        });
        _dispatch = null; _auditSlot = null;
        Interlocked.Exchange(ref _auditReserved, 0);
    }

    private void EnsureWatcher(Slot slot)
    {
        if (slot.Watcher is not null) return;
        InboxAdmissionEvidence.DirectoryBoundary(slot.Context.Storage.InboxRoot);
        var watcher = new FileSystemWatcher(slot.Context.Storage.InboxRoot) { IncludeSubdirectories = false, Filter = "*", NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite };
        void Signal() { slot.ScanRequested = true; _wake.Set(); }
        watcher.Created += (_, _) => Signal(); watcher.Changed += (_, _) => Signal(); watcher.Deleted += (_, _) => Signal();
        watcher.Renamed += (_, _) => Signal(); watcher.Error += (_, _) => Signal(); watcher.EnableRaisingEvents = true; slot.Watcher = watcher;
    }

    private void Set(Slot slot, AutomationHostState state, string? stage, string? diagnostic = null, bool increment = false)
    { lock (_gate) slot.Status = new(slot.Context.Id, state, slot.Status.Turns + (increment ? 1 : 0), slot.Probes.Count, stage, diagnostic, slot.Batch); }
    private void Block(Slot slot, Exception error)
    { slot.Watcher?.Dispose(); slot.Watcher = null; slot.Scan?.Dispose(); slot.Scan = null; Set(slot, AutomationHostState.Blocked, "blocked", Code(error)); }
    private static string Code(Exception error) => error is AutomationException automation ? automation.Code.ToString() : error.GetType().Name;

    public void RequestStop()
    {
        Exception? failure = null;
        lock (_controlsGate)
        {
            if (_stopping) return;
            try
            {
                if (_session is not null)
                    foreach (var slot in _slots) Change(slot.Context.Id, AutomationControlAction.Stop, null);
            }
            catch (Exception error) { failure = error; }
            finally { _stopping = true; }
        }
        lock (_gate) { _state = AutomationHostState.Stopping; if (failure is not null) _diagnostic = "stop_not_persisted:" + Code(failure); }
        _transportStop.Cancel(); _wake.Set();
    }
    public async ValueTask DisposeAsync()
    { RequestStop(); await Completion.ConfigureAwait(false); }
}

internal static class AutomationHostPolicyBudget
{
    internal static bool ResourcesTooLarge(this StoredAutomationPolicy policy, long memory) => policy.Definition.Resources.MaxMemoryBytes > memory;
}

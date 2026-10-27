using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NAP.Core;

namespace NAP.Presentation;

public sealed record GitRevocationRequest(long Revision, string Actor);
public sealed record AutomationReference(string Reference);
public sealed record AutomationPrepare(string FileName);
public sealed record AutomationExecute(string Prepared, string Audit);
public sealed record AutomationCommitSelection(IReadOnlyList<string> Results, string Subject);
public sealed record AutomationCatalogPage(CatalogFilter Filter, int Offset, int Limit);
public sealed record AutomationNormalizationPrepare(string FileName, string? TargetProfile, ImageNormalizationPolicy Policy);
public sealed record AutomationNormalizationDecision(string Reference, ImageNormalizationAuthorization Authorization);
public sealed record AutomationReadResult(string SessionId, string? Reference, JsonElement Value);

/// <summary>Typed desktop dispatch. All authoritative objects stay in this process; client projections are display-only.
/// No request can select a DLL, command, roots or arbitrary absolute path.</summary>
public sealed class AutomationDesktopBridge
{
    private sealed record Owned(UniverseId Universe, object Value, string? Parent = null);
    private readonly AutomationHost _host;
    private readonly Dictionary<UniverseId, UniverseContext> _contexts;
    private readonly ProfessionalUiService _manual, _read;
    private readonly ImageNormalizationUiService _normalization;
    private readonly ConcurrentDictionary<string, Owned> _handles = new();
    private readonly ConcurrentDictionary<string, Task<JsonElement>> _requests = new();
    private readonly string _requestDirectory;
    private readonly ConcurrentDictionary<string, (UniverseId Universe, byte[] Bytes)> _blobs = new();
    private long _blobBytes;
    private long _handleBytes;
    private int _recordedRequests;
    private readonly object _handleGate = new();
    public const long MaximumHandleBytes = 128L * 1024 * 1024;

    public AutomationDesktopBridge(AutomationHost host, IEnumerable<UniverseContext> contexts, string requestDirectory, IAiAuditClient? auditor = null,
        GitProductionService? git = null, GitProductionInspector? gitInspector = null, Func<UniverseContext, GitPublicationPolicyStore>? gitPolicy = null)
    {
        _host = host; _contexts = contexts.ToDictionary(c => c.Id);
        _manual = new(auditor, new HostedUniverseOperationDispatcher(host), git, gitInspector, gitPolicy); _read = new(auditor, new HostedUniverseOperationDispatcher(host, true), git, gitInspector, gitPolicy);
        _normalization = new(new HostedUniverseOperationDispatcher(host)); _requestDirectory = Path.GetFullPath(requestDirectory);
        var lab = Path.GetDirectoryName(host.Controls.DatabasePath)!.TrimEnd(Path.DirectorySeparatorChar);
        if (!Path.GetDirectoryName(_requestDirectory)!.Equals(Path.GetDirectoryName(lab), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
            Path.GetFileName(_requestDirectory) != "desktop-requests") throw new ArgumentException("Desktop request storage must be the explicit laboratory sibling.");
        Directory.CreateDirectory(_requestDirectory);
        if ((File.GetAttributes(_requestDirectory) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Request storage cannot be linked.");
        _recordedRequests = Directory.EnumerateFiles(_requestDirectory, "command_*.json").Take(4097).Count();
    }

    private T Get<T>(UniverseContext context, string reference) where T : class
    {
        if (!_handles.TryGetValue(reference, out var owned) || owned.Universe != context.Id || owned.Value is not T value)
            throw new AutomationException(AutomationError.InvalidClaim, "Desktop reference expired, changed type or belongs to another universe.");
        return value;
    }
    private JsonElement View(UniverseContext context, object? value, bool retain = false, string? parent = null)
    {
        string? reference = null;
        if (retain && value is not null)
        {
            // Native normalization proposals retain package/preview byte arrays absent from their JSON view.
            // Charge them exactly, plus a conservative metadata allowance. Other native plans contain
            // metadata only; four times UTF-8 bounds their strings, collection entries and object overhead.
            var metadata = JsonSerializer.SerializeToUtf8Bytes(value, value.GetType(), AutomationReadViews.Json(context)).LongLength;
            var retained = checked(4096 + metadata * 4 + (value is ImageNormalizationProposal proposal ? proposal.RetainedBufferBytes : 0));
            lock (_handleGate)
            {
                if (_handles.Count >= 128 || retained > MaximumHandleBytes - _handleBytes)
                    throw new AutomationException(AutomationError.Busy, "Desktop retained-object budget exhausted; restart the host after draining.");
                reference = "view_" + Guid.NewGuid().ToString("N"); _handles[reference] = new(context.Id, value, parent); _handleBytes += retained;
            }
        }
        var projection = value is PreparedPipelineAsset prepared ? new { prepared.JobId, prepared.Summary } : value;
        var payload = JsonSerializer.SerializeToElement(projection, projection?.GetType() ?? typeof(object), AutomationReadViews.Json(context));
        if (Encoding.UTF8.GetByteCount(payload.GetRawText()) > 256 * 1024)
        {
            var bytes = Encoding.UTF8.GetBytes(payload.GetRawText());
            if (Interlocked.Add(ref _blobBytes, bytes.Length) > 128L * 1024 * 1024)
            { Interlocked.Add(ref _blobBytes, -bytes.Length); throw new AutomationException(AutomationError.Busy, "Desktop read buffer budget exhausted."); }
            var blob = "blob_" + Guid.NewGuid().ToString("N"); _blobs[blob] = (context.Id, bytes);
            payload = AutomationProtocol.Payload(new AutomationBlob(blob, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes))));
        }
        return AutomationProtocol.Payload(new AutomationReadResult(_host.SessionId, reference, payload));
    }
    private static bool IsRead(AutomationOperation op) => op is AutomationOperation.Status or AutomationOperation.Batches or AutomationOperation.QueuePage or AutomationOperation.Events or
        AutomationOperation.Dashboard or AutomationOperation.Pipeline or AutomationOperation.Planning or AutomationOperation.Backups or AutomationOperation.InspectGit or
        AutomationOperation.GitReceipts or AutomationOperation.CatalogPage or AutomationOperation.CatalogDetail or AutomationOperation.Statistics or AutomationOperation.NormalizationRead or
        AutomationOperation.ReadBlobChunk or AutomationOperation.CurrentPolicy or AutomationOperation.ReleaseBlob or AutomationOperation.CurrentControl or AutomationOperation.Overview or AutomationOperation.WorkflowPage or AutomationOperation.WorkflowDetail or AutomationOperation.WorkflowBatches or AutomationOperation.Diagnostics or AutomationOperation.GitConfiguration or AutomationOperation.GitOutbox or AutomationOperation.GitHostAccess or AutomationOperation.GitAuthorizationPropose;

    public async Task<JsonElement> HandleAsync(AutomationRequest request, string principal)
    {
        try { return await HandleCoreAsync(request, principal).ConfigureAwait(false); }
        catch (UiStoppedException stopped) { throw new AutomationRemoteOperationException("validation_stopped", stopped.Issues.Issues); }
    }
    private Task<JsonElement> HandleCoreAsync(AutomationRequest request, string principal)
    {
        if (request.Version != 1 || !Enum.IsDefined(request.Operation) || request.RequestId.Length != 40 ||
            !request.RequestId.StartsWith("command_", StringComparison.Ordinal) || !Guid.TryParseExact(request.RequestId[8..], "N", out var parsed) || parsed == Guid.Empty)
            throw new InvalidDataException("Invalid desktop request identity/version.");
        if (request.Operation is AutomationOperation.Activate or AutomationOperation.Pause or AutomationOperation.Resume or AutomationOperation.StopUniverse or AutomationOperation.CancelItem or AutomationOperation.ResumeItem)
            return DispatchAsync(request); // these use the control ledger's durable replay, including across sessions
        if (IsRead(request.Operation)) return DispatchAsync(request);
        // Lazy avoids invoking a mutation twice when two matching requests arrive concurrently.
        return DeduplicateAsync(request, principal);
    }

    private readonly object _requestGate = new();
    private Task<JsonElement> DeduplicateAsync(AutomationRequest request, string principal)
    {
        lock (_requestGate)
        {
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { request.Operation, request.UniverseId, request.Payload, Principal = principal }, AutomationProtocol.Json))));
            var path = Path.Combine(_requestDirectory, request.RequestId + ".json");
            if (File.Exists(path))
            {
                if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidDataException("Request record exceeds its bound.");
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Request record is linked.");
                using var prior = JsonDocument.Parse(File.ReadAllBytes(path));
                if (prior.RootElement.GetProperty("SchemaVersion").GetInt32() != 1) throw new InvalidDataException("Unsupported desktop request record.");
                if (prior.RootElement.GetProperty("Fingerprint").GetString() != fingerprint) throw new InvalidDataException("Request identity changed meaning.");
                if (_requests.TryGetValue(request.RequestId, out var live)) return live;
                if (prior.RootElement.TryGetProperty("Response", out var response)) return Task.FromResult(response.Clone());
                throw new AutomationException(AutomationError.InvalidClaim, "Previous manual outcome is unknown; inspect its native evidence before another explicit operation.");
            }
            if (_recordedRequests >= 4096 || _requests.Count >= 32)
                throw new AutomationException(AutomationError.Busy, "Desktop durable/in-flight command budget exhausted.");
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(file, new { SchemaVersion = 1, Fingerprint = fingerprint }); file.Flush(true); }
            _recordedRequests++;
            var pending = ExecuteRecordedAsync(request, fingerprint, path, principal);
            _requests[request.RequestId] = pending;
            _ = pending.ContinueWith(t => { _ = t.Exception; lock (_requestGate) _requests.TryRemove(request.RequestId, out _); },
                CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            return pending;
        }
    }

    private async Task<JsonElement> ExecuteRecordedAsync(AutomationRequest request, string fingerprint, string path, string principal)
    {
        var response = await DispatchAsync(request, principal).ConfigureAwait(false);
        var temporary = path + ".response.tmp";
        using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { JsonSerializer.Serialize(file, new { SchemaVersion = 1, Fingerprint = fingerprint, Response = response }); file.Flush(true); }
        File.Move(temporary, path, true); return response;
    }

    private async Task<JsonElement> DispatchAsync(AutomationRequest request, string? principal = null)
    {
        if (request.Operation == AutomationOperation.Status) return AutomationProtocol.Payload(_host.Snapshot());
        if (request.Operation == AutomationOperation.ShutdownHost)
        { _host.RequestStop(); await _host.Completion.ConfigureAwait(false); return AutomationProtocol.Payload(_host.Snapshot()); }
        var id = new UniverseId(request.UniverseId ?? throw new InvalidDataException("Universe is required."));
        if (!_contexts.TryGetValue(id, out var c)) throw new AutomationException(AutomationError.UniverseMismatch, "Unknown desktop universe.");
        var ct = CancellationToken.None; var json = AutomationReadViews.Json(c);
        T Arg<T>() => request.Payload.Deserialize<T>(json) ?? throw new InvalidDataException("Missing operation arguments.");
        string Reference() => Arg<AutomationReference>().Reference;
        string Source(string name)
        {
            if (name != Path.GetFileName(name) || name.Contains(':') || name.Length > 240 || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Only an Inbox ZIP filename is accepted.");
            return Path.Combine(c.Storage.InboxRoot, name);
        }
        switch (request.Operation)
        {
            case AutomationOperation.Diagnostics:
                var diagnosticQuery = Arg<DiagnosticQuery>();
                return AutomationProtocol.Payload(await _host.QueryAsync(id, s => s.Queue.BrowseDiagnostics(diagnosticQuery)));
            case AutomationOperation.WorkflowBatches:
                return AutomationProtocol.Payload(await _host.QueryAsync(id, s => s.Queue.BrowseWorkflowBatches()));
            case AutomationOperation.WorkflowPage:
                var pageRequest = Arg<WorkflowPageRequest>();
                return AutomationProtocol.Payload(await _host.QueryAsync(id, s => new WorkflowDesktopReader(c, s.Queue).Browse(pageRequest)));
            case AutomationOperation.WorkflowDetail:
                var detailRequest = Arg<WorkflowDetailRequest>();
                var capabilities = await _host.ReviewCapabilitiesAsync(id, detailRequest.ItemId);
                return AutomationProtocol.Payload(await _host.QueryHeavyAsync(id, s => new WorkflowDesktopReader(c, s.Queue).Detail(detailRequest, capabilities)));
            case AutomationOperation.ReviewPropose:
                var proposal = Arg<ReviewProposalRequest>();
                await _host.RunReviewAsync(id, proposal.ItemId, false, (s, preparation) => preparation.ProposeReviewChoice(s.Owner, proposal, principal!));
                return AutomationProtocol.Payload(true);
            case AutomationOperation.ReviewApprove:
                var approval = Arg<ReviewDecisionRequest>();
                await _host.RunReviewAsync(id, approval.ItemId, false, (s, preparation) => { preparation.ApproveReview(s.Owner, approval, principal!); return true; });
                return AutomationProtocol.Payload(true);
            case AutomationOperation.ReviewContinue:
                var continuation = Arg<ReviewDecisionRequest>();
                return AutomationProtocol.Payload(await _host.RunReviewAsync(id, continuation.ItemId, true, (s, preparation) => preparation.ContinueReview(s.Owner, continuation)));

            case AutomationOperation.Overview:
                var overview = await _host.QueryAsync(id, s => s.Queue.Overview());
                return AutomationProtocol.Payload(new AutomationDesktopOverview(_host.Snapshot(), overview, _host.ReadControl(id)));
            case AutomationOperation.ReleaseBlob:
                var release = Arg<AutomationReference>().Reference;
                if (_blobs.TryGetValue(release, out var released) && released.Universe == id && _blobs.TryRemove(release, out var removed))
                    Interlocked.Add(ref _blobBytes, -removed.Bytes.Length);
                return AutomationProtocol.Payload(true);
            case AutomationOperation.CurrentPolicy: return AutomationProtocol.Payload(await _host.QueryAsync(id, s => s.Queue.CurrentPolicy()));
            case AutomationOperation.CurrentControl: return AutomationProtocol.Payload(_host.ReadControl(id));
            case AutomationOperation.ReadBlobChunk:
                var read = Arg<AutomationBlobRead>();
                if (!_blobs.TryGetValue(read.BlobId, out var blob) || blob.Universe != id || read.Offset < 0 || read.Count is < 1 or > 65536 || read.Offset > blob.Bytes.Length - read.Count)
                    throw new InvalidDataException("Invalid bounded read blob request.");
                return AutomationProtocol.Payload(blob.Bytes.AsSpan(read.Offset, read.Count).ToArray());
            case AutomationOperation.Activate:
                var a = Arg<AutomationActivation>(); return AutomationProtocol.Payload(await _host.ActivateAsync(id, a.Policy, a.ResumeOnRestart, request.RequestId));
            case AutomationOperation.Pause: case AutomationOperation.Resume: case AutomationOperation.StopUniverse: case AutomationOperation.CancelItem: case AutomationOperation.ResumeItem:
                var action = request.Operation switch { AutomationOperation.Pause => AutomationControlAction.Pause, AutomationOperation.Resume => AutomationControlAction.Resume,
                    AutomationOperation.StopUniverse => AutomationControlAction.Stop, AutomationOperation.CancelItem => AutomationControlAction.CancelItem, _ => AutomationControlAction.ResumeItem };
                var item = action is AutomationControlAction.CancelItem or AutomationControlAction.ResumeItem ? new QueueItemId(Arg<AutomationItemRequest>().ItemId) : null;
                return AutomationProtocol.Payload(_host.Control(id, action, item, request.RequestId));
            case AutomationOperation.Batches:
                var bp = Arg<AutomationPageRequest>(); return AutomationProtocol.Payload(await _host.BatchesAsync(id, bp.Offset, bp.Limit));
            case AutomationOperation.QueuePage:
                var qp = Arg<AutomationPageRequest>(); return AutomationProtocol.Payload(await _host.QueryAsync(id, s => s.Queue.List(qp.Offset, qp.Limit)));
            case AutomationOperation.Events: return AutomationProtocol.Payload(await _host.QueryAsync(id, s => s.Queue.Events(new(Arg<AutomationItemRequest>().ItemId))));
            case AutomationOperation.Dashboard: return View(c, await _read.DashboardAsync(c, ct));
            case AutomationOperation.Pipeline: return View(c, await _read.PipelineAsync(c, ct));
            case AutomationOperation.Planning: return View(c, await _read.PlanningAsync(c, ct));
            case AutomationOperation.SaveObjective: await _manual.SaveObjectiveAsync(c, Arg<CatalogObjective>(), ct); return View(c, true);
            case AutomationOperation.Backups: return View(c, await _read.BackupsAsync(c, ct));
            case AutomationOperation.Backup: return View(c, await _manual.BackupAsync(c, Arg<BackupKind>(), ct));
            case AutomationOperation.PlanRestore: return View(c, await _manual.PlanRestoreAsync(c, Arg<BackupId>(), ct), true);
            case AutomationOperation.Restore: return View(c, await _manual.RestoreAsync(c, Get<DatabaseRestorePlan>(c, Reference()), ct));
            case AutomationOperation.PlanRetention: return View(c, await _manual.PlanRetentionAsync(c, Arg<BackupRetentionPolicy>(), ct), true);
            case AutomationOperation.Retain: return View(c, await _manual.RetainAsync(c, Get<BackupRetentionPlan>(c, Reference()), ct));
            case AutomationOperation.Prepare:
                var source = Source(Arg<AutomationPrepare>().FileName); return View(c, await _manual.PrepareAsync(c, new(Path.GetFileName(source), source), ct), true);
            case AutomationOperation.PrepareNormalized:
                var normalized = await _host.QueryAsync(id, _ => new ImageNormalizationService(c).ReadReceipt(Arg<string>()));
                return View(c, await _manual.PrepareNormalizedAsync(c, normalized, ct), true);
            case AutomationOperation.Audit:
                var preparedRef = Reference(); return View(c, await _host.RunManualAuditAsync(id, () => _manual.AuditAsync(c, Get<PreparedPipelineAsset>(c, preparedRef), ct)), true, preparedRef);
            case AutomationOperation.Execute:
                var execute = Arg<AutomationExecute>();
                if (!_handles.TryGetValue(execute.Audit, out var audited) || audited.Parent != execute.Prepared) throw new AutomationException(AutomationError.InvalidClaim, "Audit belongs to another prepared manual workflow.");
                var preparedNative = Get<PreparedPipelineAsset>(c, execute.Prepared);
                await _host.QueryAsync(id, s =>
                {
                    if (s.Queue.GetReservation(preparedNative.Package.AssetKey) is { } reservation && reservation.JobId != preparedNative.JobId)
                        throw new AutomationException(AutomationError.ReservationOccupied, "Automatic workflow owns this asset reservation."); return 0;
                });
                return View(c, await _manual.ExecuteAsync(c, preparedNative, Get<AiAuditReport>(c, execute.Audit), ct), true);
            case AutomationOperation.GitConfiguration: return View(c, await _read.ReadGitConfigurationAsync(c, ct));
            case AutomationOperation.GitOutbox: return View(c, await _read.ReadGitOutboxAsync(c, ct));
            case AutomationOperation.GitHostAccess: return View(c, await _read.GitHostAccessAsync(c, ct));
            case AutomationOperation.GitAuthorizationPropose: return View(c, await _manual.ProposeGitAuthorizationAsync(c, Arg<GitAuthorizationInput>(), ct), true);
            case AutomationOperation.GitAuthorizationSave:
                await _manual.SaveGitAuthorizationAsync(c, Get<GitAuthorizationReview>(c, Reference()), ct); return View(c, true);
            case AutomationOperation.GitAuthorizationRevoke:
                var revoke = Arg<GitRevocationRequest>(); await _manual.RevokeGitAuthorizationAsync(c, revoke.Revision, revoke.Actor, ct); return View(c, true);
            case AutomationOperation.GitOutboxRetry: return View(c, await _manual.RetryGitOutboxAsync(c, Arg<GitRetryRequest>(), ct));
            case AutomationOperation.InspectGit: return View(c, await _read.InspectGitAsync(c, ct));
            case AutomationOperation.GitReceipts: return View(c, await _read.ReceiptsAsync(c, ct));
            case AutomationOperation.PlanCommit:
                var selection = Arg<AutomationCommitSelection>(); return View(c, await _manual.PlanCommitAsync(c, selection.Results.Select(r => Get<ProductionAssetResult>(c, r)).ToArray(), selection.Subject, ct), true);
            case AutomationOperation.Commit: return View(c, await _manual.CommitAsync(c, Get<GitCommitPlan>(c, Reference()), ct));
            case AutomationOperation.RecoverGit: return View(c, await _manual.RecoverAsync(c, Arg<GitOperationId>(), ct));
            case AutomationOperation.RetryCommit: return View(c, await _manual.RetryCommitAsync(c, Arg<GitOperationId>(), ct));
            case AutomationOperation.Push: return View(c, await _manual.PushAsync(c, Arg<GitOperationId>(), ct));
            case AutomationOperation.CatalogPage:
                var page = Arg<AutomationCatalogPage>(); return View(c, await _host.QueryAsync(id, _ => new CatalogExplorerReader(c).Page(page.Filter, page.Offset, page.Limit)));
            case AutomationOperation.CatalogDetail: return View(c, await _host.QueryAsync(id, _ => new CatalogExplorerReader(c).Detail(Arg<string>())));
            case AutomationOperation.Statistics: return View(c, await _host.QueryAsync(id, _ => new CatalogExplorerReader(c).Statistics(Arg<CatalogFilter>())));
            case AutomationOperation.Thumbnail:
                var thumbnail = Arg<CatalogAssetSummary>();
                return View(c, await _host.QueryHeavyAsync(id, s =>
                {
                    var actual = new CatalogExplorerReader(c).Detail(thumbnail.AssetKey.AssetId) ?? throw new InvalidDataException("Catalog asset is absent.");
                    if (thumbnail.AssetKey.UniverseId != id) throw new InvalidDataException("Thumbnail universe differs.");
                    var verified = new CatalogAssetSummary(actual.AssetKey, actual.AssetType, actual.ProductionProfile,
                        actual.ProductionRelativeDirectory + "/" + actual.AssetKey.AssetId + ".webp", actual.ProductionDigest, actual.ProductionSizeBytes);
                    var generated = new ProductionThumbnailCache(c).GetOwnedAsync(s, verified).GetAwaiter().GetResult();
                    return new AutomationThumbnailView(File.ReadAllBytes(generated.CachePath), generated.Width, generated.Height, generated.CacheHit);
                }));
            case AutomationOperation.NormalizationPrepare:
                var np = Arg<AutomationNormalizationPrepare>(); var nsource = Source(np.FileName);
                return View(c, await _normalization.PrepareAsync(c, new(np.FileName, nsource), np.TargetProfile, np.Policy, ct), true);
            case AutomationOperation.NormalizationDecide:
                var nd = Arg<AutomationNormalizationDecision>(); return View(c, await _normalization.DecideAsync(c, Get<ImageNormalizationProposal>(c, nd.Reference), nd.Authorization, ct));
            case AutomationOperation.NormalizationRead:
                return View(c, await _host.QueryAsync(id, _ => new ImageNormalizationService(c).Scan()));
            default: throw new InvalidDataException("Unsupported desktop operation.");
        }
    }
}

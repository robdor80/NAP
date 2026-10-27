using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Security.Cryptography;
using NAP.Core;

namespace NAP.Presentation;

/// <summary>WPF adapters for the authenticated host. Never fall back to a local writer after an RPC failure.</summary>
public sealed record AutomationThumbnailView(byte[] Bytes, int Width, int Height, bool CacheHit);
public sealed class RemoteDesktopService(AutomationHostClient client, bool auditAvailable) : IProfessionalUiService, IExplorerService, IImageNormalizationUiService, IGitManagementUiService, IDisposable
{
    private readonly AutomationThumbnailCache _cache = new();
    private sealed record Reference(string SessionId, string Value, UniverseId Universe);
    private readonly ConditionalWeakTable<object, Reference> _references = new();
    public bool AuditAvailable => auditAvailable;
    private string Ref(UniverseContext c, object value)
    {
        if (!_references.TryGetValue(value, out var reference) || reference.Universe != c.Id)
            throw new InvalidOperationException("This value is not a server-owned reference in the selected universe.");
        return reference.Value;
    }
    private async Task<T> Call<T>(UniverseContext c, AutomationOperation operation, object? argument, CancellationToken ct)
    {
        var json = AutomationReadViews.Json(c);
        var input = JsonSerializer.SerializeToElement(argument, argument?.GetType() ?? typeof(object), json);
        var result = await client.CallAsync(operation, c.Id.Value, input, ct).ConfigureAwait(false);
        var view = AutomationProtocol.Read<AutomationReadResult>(result);
        var payload = view.Value;
        if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("BlobId", out _))
        {
            var blob = AutomationProtocol.Read<AutomationBlob>(payload);
            if (blob.Length is < 1 or > 128 * 1024 * 1024) throw new InvalidDataException("Remote read exceeds its bound.");
            var bytes = new byte[blob.Length];
            for (int offset = 0; offset < bytes.Length; offset += 65536)
            {
                var count = Math.Min(65536, bytes.Length - offset);
                var chunk = await client.CallAsync(AutomationOperation.ReadBlobChunk, c.Id.Value, new AutomationBlobRead(blob.BlobId, offset, count), ct).ConfigureAwait(false);
                var data = chunk.Deserialize<byte[]>(AutomationProtocol.Json)!;
                if (data.Length != count) throw new InvalidDataException("Remote chunk length differs."); data.CopyTo(bytes, offset);
            }
            if (Convert.ToHexString(SHA256.HashData(bytes)) != blob.Sha256) throw new InvalidDataException("Remote read hash differs.");
            using var doc = JsonDocument.Parse(bytes); payload = doc.RootElement.Clone();
            await client.CallAsync(AutomationOperation.ReleaseBlob, c.Id.Value, new AutomationReference(blob.BlobId), ct).ConfigureAwait(false);
        }
        object? value = typeof(T) == typeof(PreparedPipelineAsset)
            ? new PreparedPipelineAsset(c, payload.GetProperty("JobId").Deserialize<JobId>(json)!, payload.GetProperty("Summary").GetString()!)
            : payload.Deserialize<T>(json);
        if (view.Reference is not null && value is not null) _references.Add(value, new(view.SessionId, view.Reference, c.Id));
        return (T)value!;
    }
    public Task<DashboardSnapshot> DashboardAsync(UniverseContext c, CancellationToken ct) => Call<DashboardSnapshot>(c, AutomationOperation.Dashboard, new { }, ct);
    public Task<PipelineSnapshot> PipelineAsync(UniverseContext c, CancellationToken ct) => Call<PipelineSnapshot>(c, AutomationOperation.Pipeline, new { }, ct);
    public Task<PreparedPipelineAsset> PrepareAsync(UniverseContext c, InboxPackageCandidate candidate, CancellationToken ct) => Call<PreparedPipelineAsset>(c, AutomationOperation.Prepare, new AutomationPrepare(candidate.FileName), ct);
    public Task<PreparedPipelineAsset> PrepareNormalizedAsync(UniverseContext c, ImageNormalizationResult candidate, CancellationToken ct) => Call<PreparedPipelineAsset>(c, AutomationOperation.PrepareNormalized, candidate.Receipt.OperationId, ct);
    public Task<AiAuditReport> AuditAsync(UniverseContext c, PreparedPipelineAsset prepared, CancellationToken ct) => Call<AiAuditReport>(c, AutomationOperation.Audit, new AutomationReference(Ref(c, prepared)), ct);
    public Task<ProductionAssetResult> ExecuteAsync(UniverseContext c, PreparedPipelineAsset prepared, AiAuditReport audit, CancellationToken ct) => Call<ProductionAssetResult>(c, AutomationOperation.Execute, new AutomationExecute(Ref(c, prepared), Ref(c, audit)), ct);
    public Task<CatalogPlanningReadModel> PlanningAsync(UniverseContext c, CancellationToken ct) => Call<CatalogPlanningReadModel>(c, AutomationOperation.Planning, new { }, ct);
    public async Task SaveObjectiveAsync(UniverseContext c, CatalogObjective objective, CancellationToken ct) => _ = await Call<bool>(c, AutomationOperation.SaveObjective, objective, ct);
    public Task<BackupHistory> BackupsAsync(UniverseContext c, CancellationToken ct) => Call<BackupHistory>(c, AutomationOperation.Backups, new { }, ct);
    public Task<BackupHistoryEntry> BackupAsync(UniverseContext c, BackupKind kind, CancellationToken ct) => Call<BackupHistoryEntry>(c, AutomationOperation.Backup, kind, ct);
    public Task<DatabaseRestorePlan> PlanRestoreAsync(UniverseContext c, BackupId id, CancellationToken ct) => Call<DatabaseRestorePlan>(c, AutomationOperation.PlanRestore, id, ct);
    public Task<DatabaseRestoreResult> RestoreAsync(UniverseContext c, DatabaseRestorePlan plan, CancellationToken ct) => Call<DatabaseRestoreResult>(c, AutomationOperation.Restore, new AutomationReference(Ref(c, plan)), ct);
    public Task<BackupRetentionPlan> PlanRetentionAsync(UniverseContext c, BackupRetentionPolicy policy, CancellationToken ct) => Call<BackupRetentionPlan>(c, AutomationOperation.PlanRetention, policy, ct);
    public Task<BackupRetentionResult> RetainAsync(UniverseContext c, BackupRetentionPlan plan, CancellationToken ct) => Call<BackupRetentionResult>(c, AutomationOperation.Retain, new AutomationReference(Ref(c, plan)), ct);
    public Task<GitConfigurationView> ReadGitConfigurationAsync(UniverseContext c, CancellationToken ct) => Call<GitConfigurationView>(c, AutomationOperation.GitConfiguration, new { }, ct);
    public Task<GitOutboxView> ReadGitOutboxAsync(UniverseContext c, CancellationToken ct) => Call<GitOutboxView>(c, AutomationOperation.GitOutbox, new { }, ct);
    public Task<GitHostAccess> GitHostAccessAsync(UniverseContext c, CancellationToken ct) => Call<GitHostAccess>(c, AutomationOperation.GitHostAccess, new { }, ct);
    public Task<GitAuthorizationReview> ProposeGitAuthorizationAsync(UniverseContext c, GitAuthorizationInput input, CancellationToken ct) => Call<GitAuthorizationReview>(c, AutomationOperation.GitAuthorizationPropose, input, ct);
    public async Task SaveGitAuthorizationAsync(UniverseContext c, GitAuthorizationReview review, CancellationToken ct) => _ = await Call<bool>(c, AutomationOperation.GitAuthorizationSave, new AutomationReference(Ref(c, review)), ct);
    public async Task RevokeGitAuthorizationAsync(UniverseContext c, long revision, string actor, CancellationToken ct) => _ = await Call<bool>(c, AutomationOperation.GitAuthorizationRevoke, new GitRevocationRequest(revision, actor), ct);
    public Task<string> RetryGitOutboxAsync(UniverseContext c, GitRetryRequest request, CancellationToken ct) => Call<string>(c, AutomationOperation.GitOutboxRetry, request, ct);
    public Task<GitRepositoryStatus> InspectGitAsync(UniverseContext c, CancellationToken ct) => Call<GitRepositoryStatus>(c, AutomationOperation.InspectGit, new { }, ct);
    public Task<IReadOnlyList<GitOperationReceipt>> ReceiptsAsync(UniverseContext c, CancellationToken ct) => Call<IReadOnlyList<GitOperationReceipt>>(c, AutomationOperation.GitReceipts, new { }, ct);
    public Task<GitCommitPlan> PlanCommitAsync(UniverseContext c, IReadOnlyList<ProductionAssetResult> results, string subject, CancellationToken ct) => Call<GitCommitPlan>(c, AutomationOperation.PlanCommit, new AutomationCommitSelection(results.Select(r => Ref(c, r)).ToArray(), subject), ct);
    public Task<GitCommitResult> CommitAsync(UniverseContext c, GitCommitPlan plan, CancellationToken ct) => Call<GitCommitResult>(c, AutomationOperation.Commit, new AutomationReference(Ref(c, plan)), ct);
    public Task<GitOperationReceipt> RecoverAsync(UniverseContext c, GitOperationId id, CancellationToken ct) => Call<GitOperationReceipt>(c, AutomationOperation.RecoverGit, id, ct);
    public Task<GitCommitResult> RetryCommitAsync(UniverseContext c, GitOperationId id, CancellationToken ct) => Call<GitCommitResult>(c, AutomationOperation.RetryCommit, id, ct);
    public Task<GitPushResult> PushAsync(UniverseContext c, GitOperationId id, CancellationToken ct) => Call<GitPushResult>(c, AutomationOperation.Push, id, ct);
    public Task BeginSessionAsync(UniverseContext c, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; }
    public Task<CatalogPage> PageAsync(UniverseContext c, CatalogFilter filter, int offset, int limit, CancellationToken ct) => Call<CatalogPage>(c, AutomationOperation.CatalogPage, new AutomationCatalogPage(filter, offset, limit), ct);
    public Task<CatalogAssetSnapshot?> DetailAsync(UniverseContext c, string id, CancellationToken ct) => Call<CatalogAssetSnapshot?>(c, AutomationOperation.CatalogDetail, id, ct);
    public Task<CatalogStatistics> StatisticsAsync(UniverseContext c, CatalogFilter filter, CancellationToken ct) => Call<CatalogStatistics>(c, AutomationOperation.Statistics, filter, ct);
    public async Task<ThumbnailResult> ThumbnailAsync(UniverseContext c, CatalogAssetSummary asset, CancellationToken ct)
    {
        var view = await Call<AutomationThumbnailView>(c, AutomationOperation.Thumbnail, asset, ct).ConfigureAwait(false);
        if (view.Bytes.Length > 2 * 1024 * 1024 || view.Width is < 1 or > 320 || view.Height is < 1 or > 320) throw new InvalidDataException("Invalid thumbnail view.");
        var path = _cache.Write(view.Bytes);
        return new(path, view.Width, view.Height, view.CacheHit);
    }
    public Task<ImageNormalizationProposal> PrepareAsync(UniverseContext c, InboxPackageCandidate candidate, string? targetProfile, ImageNormalizationPolicy policy, CancellationToken ct) =>
        Call<ImageNormalizationProposal>(c, AutomationOperation.NormalizationPrepare, new AutomationNormalizationPrepare(candidate.FileName, targetProfile, policy), ct);
    public Task<ImageNormalizationResult> DecideAsync(UniverseContext c, ImageNormalizationProposal proposal, ImageNormalizationAuthorization authorization, CancellationToken ct) =>
        Call<ImageNormalizationResult>(c, AutomationOperation.NormalizationDecide, new AutomationNormalizationDecision(Ref(c, proposal), authorization), ct);
    public Task<ImageNormalizationRecoverySnapshot> ReadAsync(UniverseContext c, CancellationToken ct) => Call<ImageNormalizationRecoverySnapshot>(c, AutomationOperation.NormalizationRead, new { }, ct);
    public void Dispose()
    {
        _cache.Dispose();
    }
}

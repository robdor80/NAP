using System.Text.Json;
using NAP.Core;

namespace NAP.Presentation;

public enum GitVisualLabScenario { Disabled, Manual, AutoCommitBatch, AutoCommitAndPushBatch, Revoked, Recovery, Empty, Error }
public sealed record GitVisualLabSnapshot(GitConfigurationView Configuration, GitOutboxView Outbox,
    GitRepositoryStatus? Repository, IReadOnlyList<GitOperationReceipt> Receipts);

/// <summary>Closed synthetic adapter. No production service, process, filesystem writer, transport or coordinator.</summary>
public sealed class GitVisualLabService : IGitManagementUiService
{
    public const string Denial = "Laboratorio F8.1: solo lectura ficticia; operaciones, autorizaciones, recuperación e inspección remota bloqueadas.";
    public UniverseContext Context { get; }
    public GitVisualLabService(GitVisualLabContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        var id = new UniverseId("f81_lab");
        Context = new(new UniverseProfile(id, "F8.1 · Universo ficticio"), new(id,
            Path.Combine(contract.Root, "fictional-workspace"), Path.Combine(contract.Root, "fictional-production"), Path.Combine(contract.Root, "fictional-archive")));
    }

    public static void DenyOperationalAction() => throw new InvalidOperationException(Denial);
    private static T Denied<T>() { DenyOperationalAction(); return default!; }
    private void Scope(UniverseContext c, CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); if (!ReferenceEquals(c, Context)) throw new InvalidDataException("Synthetic dependency scope mismatch; no fallback."); }
    public GitVisualLabSnapshot Read(GitVisualLabScenario scenario)
    {
        if (!Enum.IsDefined(scenario)) throw new InvalidDataException("Unknown fixture; no fallback.");
        if (scenario == GitVisualLabScenario.Error) throw new InvalidDataException("F8.1 · Error sintético de integridad; sin acceso a datos persistentes.");
        var mode = scenario switch {
            GitVisualLabScenario.Manual => GitPublicationMode.Manual,
            GitVisualLabScenario.AutoCommitBatch => GitPublicationMode.AutoCommitBatch,
            GitVisualLabScenario.AutoCommitAndPushBatch or GitVisualLabScenario.Recovery => GitPublicationMode.AutoCommitAndPushBatch,
            _ => GitPublicationMode.Disabled };
        var revoked = scenario == GitVisualLabScenario.Revoked;
        // Existing batches originate in an automatic grant. Manual/commit-only/revoked displays
        // retain that history under a NEW revision; they never invent publication under AllowPush=false.
        var revision = scenario is GitVisualLabScenario.Disabled or GitVisualLabScenario.Empty ? 0 :
            scenario is GitVisualLabScenario.Manual or GitVisualLabScenario.AutoCommitBatch or GitVisualLabScenario.Revoked ? 2 : 1;
        var originalGrant = "authorization_" + new string('1', 32);
        var currentGrant = revision == 2 ? "authorization_" + new string('2', 32) : originalGrant;
        var configuration = new GitConfigurationView(Context.Id, mode, revision, revision == 0 ? null : currentGrant,
            mode != GitPublicationMode.Disabled, mode is GitPublicationMode.Manual or GitPublicationMode.AutoCommitAndPushBatch,
            revoked, "FICTICIO-NO-REPOSITORIO", "f81-fiction", "origin/f81-fiction", "origin",
            new('1', 64), new('2', 64), new('3', 64), ["assets/ficticios/"], 5);
        var rows = new List<GitOutboxRow>();
        var receipts = new List<GitOperationReceipt>();
        var options = AutomationReadViews.Json(Context); // Read projections retain RequireNative protection. No IPC connection.
        GitRepositoryStatus? repository = null;
        if (revision != 0)
        {
            var status = new { UniverseId = Context.Id, RepositoryBinding = "FICTICIO-NO-REPOSITORIO", Head = new string('a', 40),
                Branch = "f81-fiction", Upstream = "origin/f81-fiction", RemoteName = "origin", RemoteBranch = "f81-fiction",
                RemoteFingerprint = new string('2', 64), RemoteTip = new string('a', 40), Synchronization = GitSynchronization.Ahead,
                Changes = Array.Empty<object>() };
            repository = JsonSerializer.SerializeToElement(status, options).Deserialize<GitRepositoryStatus>(options)!;
            for (var n = 1; n <= 24; n++)
            {
                var state = Enum.GetValues<GitOutboxState>()[(n - 1) % 4];
                var receiptState = state == GitOutboxState.Pushed ? GitReceiptState.Pushed : state == GitOutboxState.Committed ? GitReceiptState.Committed : GitReceiptState.Prepared;
                var operation = "g_" + n.ToString("x32");
                var sha = receiptState == GitReceiptState.Prepared ? null : new string('b', 40);
                var diagnostic = state == GitOutboxState.Blocked ? (n % 8 == 3 ? "git_policy_changed" : "native_evidence_unavailable") :
                    state == GitOutboxState.Committed && scenario == GitVisualLabScenario.Recovery ? "git_push_retry_3" : null;
                rows.Add(new(n.ToString("x64"), true, state, 1, 1, originalGrant, null, null, null,
                    n.ToString("x64"), operation, new string('c', 64), sha, diagnostic));
                rows.Add(new((n + 100).ToString("x64"), false, state, 1, 1, originalGrant, new JobId("job_" + n.ToString("x32")).Value,
                    new QueueItemId("queue_" + n.ToString("x32")).Value, new WorkflowAttemptId("attempt_" + n.ToString("x32")).Value, n.ToString("x64"), operation, new string('c', 64), sha, diagnostic));
                var projection = new { OperationId = new GitOperationId(operation), State = receiptState, CommitSha = sha,
                    Plan = new { Status = status, Selection = new { OwnedFiles = Array.Empty<object>(), Paths = Array.Empty<string>() }, Message = "F8.1 · resultado anterior ficticio " + n } };
                receipts.Add(JsonSerializer.SerializeToElement(projection, options).Deserialize<GitOperationReceipt>(options)!);
            }
        }
        var history = revision == 0 ? Array.Empty<GitPolicyRevision>() : new[] {
            new GitPolicyRevision(1, new string('d', 64), originalGrant, "Actor ficticio", new(2026, 10, 27, 0, 0, 0, TimeSpan.Zero), GitPublicationMode.AutoCommitAndPushBatch, false) };
        if (revision == 2) history = [..history, new(2, new string('e', 64), currentGrant, "Actor ficticio", new(2026, 10, 27, 1, 0, 0, TimeSpan.Zero), mode, revoked)];
        return new(configuration, new(Context.Id, rows.AsReadOnly(), Array.AsReadOnly(history)), repository, receipts.AsReadOnly());
    }

    public Task<GitConfigurationView> ReadGitConfigurationAsync(UniverseContext c, CancellationToken ct)
    { Scope(c, ct); return Task.FromResult(Read(GitVisualLabScenario.Manual).Configuration); }
    public Task<GitOutboxView> ReadGitOutboxAsync(UniverseContext c, CancellationToken ct)
    { Scope(c, ct); return Task.FromResult(Read(GitVisualLabScenario.Manual).Outbox); }
    public Task<GitHostAccess> GitHostAccessAsync(UniverseContext c, CancellationToken ct)
    { Scope(c, ct); return Task.FromResult(new GitHostAccess(false, Denial, false)); }
    public Task<GitAuthorizationReview> ProposeGitAuthorizationAsync(UniverseContext c, GitAuthorizationInput input, CancellationToken ct) => Denied<Task<GitAuthorizationReview>>();
    public Task SaveGitAuthorizationAsync(UniverseContext c, GitAuthorizationReview review, CancellationToken ct) => Denied<Task>();
    public Task RevokeGitAuthorizationAsync(UniverseContext c, long revision, string actor, CancellationToken ct) => Denied<Task>();
    public Task<string> RetryGitOutboxAsync(UniverseContext c, GitRetryRequest request, CancellationToken ct) => Denied<Task<string>>();
}

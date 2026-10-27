namespace NAP.Core;

/// <summary>A reviewed proposal is process-owned. It is never accepted from an IPC payload.</summary>
public sealed class GitAuthorizationProposal
{
    internal GitPublicationPolicyV1 Policy { get; }
    public string Author { get; }
    public string Committer { get; }
    internal GitAuthorizationProposal(GitPublicationPolicyV1 policy, string author, string committer)
    { Policy = policy; Author = author; Committer = committer; }
    public GitPublicationPolicyV1 ReadPolicy() => Policy with { AllowedRelativePrefixes = Array.AsReadOnly(Policy.AllowedRelativePrefixes.ToArray()) };
}

/// <summary>Presentation adapter over the existing stores. No new Git runner or persistence contract.</summary>
public sealed class GitPublicationManagementService(UniverseContext context, GitPublicationPolicyStore? policy = null,
    GitProductionInspector? inspector = null, GitProductionService? git = null)
{
    private readonly GitPublicationPolicyStore _policy = policy ?? new(context);
    private readonly GitProductionInspector _inspector = inspector ?? new();
    private readonly GitProductionService _git = git ?? new();
    public GitPublicationPolicyV1? ReadConfiguration() => _policy.Load();
    public GitRepositoryStatus InspectForSetup()
    { _ = _policy.Load(); return _inspector.Inspect(context); }

    public GitOutboxSnapshot ReadOutbox()
    {
        using var owner = AutomationManualAccess.Enter(context);
        using var lease = _policy.Lock();
        var outbox = new GitOutboxStore(context, _policy);
        var state = _policy.LoadLocked() is null ? outbox.Empty() : outbox.LoadLocked();
        var operations = _git.ListOperations(context);
        foreach (var batch in state.Batches)
        {
            var exists = operations.Contains(batch.OperationId);
            if (!exists)
            {
                if (batch.State is GitOutboxState.Committed or GitOutboxState.Pushed)
                    throw new InvalidDataException("Outbox native receipt is missing.");
                continue; // A reservation can legitimately precede Prepared.
            }
            var receipt = _git.ReadReceipt(context, batch.OperationId);
            if (GitOutboxStore.PlanHash(receipt.Plan) != batch.PlanHash ||
                batch.CommitSha is not null && receipt.CommitSha != batch.CommitSha ||
                batch.State == GitOutboxState.Pushed && receipt.State != GitReceiptState.Pushed ||
                batch.State == GitOutboxState.Committed && receipt.State == GitReceiptState.Prepared)
                throw new InvalidDataException("Outbox and native receipt disagree.");
        }
        return state;
    }

    public GitAuthorizationProposal Propose(GitPublicationMode mode, string actor, IReadOnlyList<string> prefixes,
        int maximum, bool allowCommit, bool allowPush)
    {
        ValidateInput(mode, actor, prefixes, maximum, allowCommit, allowPush);
        using var owner = AutomationManualAccess.Enter(context);
        var proposal = _policy.Propose(mode, actor, prefixes, maximum, _inspector) with
        { AllowCommit = allowCommit, AllowPush = allowPush };
        using var production = GitProductionSafety.Acquire(context);
        string Identity(bool author)
        {
            var value = _inspector.Git.Identity(context, author); GitProductionService.ValidateIdentity(value);
            return value[..(value.LastIndexOf("> ", StringComparison.Ordinal) + 1)];
        }
        var current = _inspector.InspectLocked(context, queryRemote: false);
        if (current.Branch != proposal.LocalBranch || current.Upstream != proposal.UpstreamRef ||
            current.RemoteFingerprint != proposal.RemoteFingerprint || current.ConfigurationFingerprint != proposal.ConfigFingerprint ||
            GitPublicationPolicyStore.Identity(context, _inspector.Git) != proposal.GitIdentityFingerprint)
            throw new InvalidDataException("Git bindings changed during proposal; review again.");
        return new(proposal with { AllowedRelativePrefixes = Array.AsReadOnly(proposal.AllowedRelativePrefixes.ToArray()) }, Identity(true), Identity(false));
    }

    public void Save(GitAuthorizationProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        var p = proposal.Policy;
        ValidateInput(p.Mode, p.Actor, p.AllowedRelativePrefixes, p.MaxJobsPerBatch, p.AllowCommit, p.AllowPush);
        using var owner = AutomationManualAccess.Enter(context);
        // Propose reads the current binding through the original engine; no permission is inferred.
        var fresh = Propose(p.Mode, p.Actor, p.AllowedRelativePrefixes, p.MaxJobsPerBatch, p.AllowCommit, p.AllowPush).Policy;
        if (fresh.PolicyRevision != p.PolicyRevision || fresh.UniverseId != p.UniverseId ||
            fresh.RepositoryBinding != p.RepositoryBinding || fresh.StorageFingerprint != p.StorageFingerprint ||
            fresh.CanonicalProductionRoot != p.CanonicalProductionRoot || fresh.CanonicalStateRoot != p.CanonicalStateRoot ||
            fresh.GitIdentityFingerprint != p.GitIdentityFingerprint || fresh.LocalBranch != p.LocalBranch ||
            fresh.UpstreamRef != p.UpstreamRef || fresh.RemoteName != p.RemoteName || fresh.RemoteBranch != p.RemoteBranch ||
            fresh.RemoteFingerprint != p.RemoteFingerprint || fresh.ConfigFingerprint != p.ConfigFingerprint)
            throw new InvalidDataException("Git authorization review expired; propose again.");
        _policy.Save(p, p.PolicyRevision - 1);
    }

    public void Revoke(long expectedRevision, string actor)
    {
        Actor(actor);
        using var owner = AutomationManualAccess.Enter(context);
        var prior = _policy.Load() ?? throw new InvalidDataException("No Git grant exists.");
        if (prior.PolicyRevision != expectedRevision) throw new InvalidDataException("Git revision changed; review again.");
        // Deliberately no inspector/Propose: a valid grant can be revoked while the remote is offline.
        _policy.Save(prior with { PolicyRevision = prior.PolicyRevision + 1,
            AuthorizationId = "authorization_" + Guid.NewGuid().ToString("N"), Actor = actor, AuthorizedUtc = DateTimeOffset.UtcNow,
            Mode = GitPublicationMode.Disabled, AllowCommit = false, AllowPush = false, Revoked = true }, expectedRevision);
    }

    private static void Actor(string actor)
    {
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 120 || actor != actor.Trim() || actor.Any(c => char.IsControl(c) || char.IsSurrogate(c)))
            throw new InvalidDataException("An explicit local actor is required.");
        AutomationValidation.Text(actor);
    }
    private void ValidateInput(GitPublicationMode mode, string actor, IReadOnlyList<string> prefixes, int maximum, bool commit, bool push)
    {
        Actor(actor);
        if (!Enum.IsDefined(mode) || maximum is < 1 or > 20 || prefixes is null || prefixes.Count is < 1 or > 64 ||
            prefixes.Distinct(StringComparer.Ordinal).Count() != prefixes.Count ||
            mode == GitPublicationMode.Disabled && (commit || push) ||
            push && (!commit || mode is not (GitPublicationMode.Manual or GitPublicationMode.AutoCommitAndPushBatch)))
            throw new InvalidDataException("Invalid Git mode, limits or independent permissions.");
        foreach (var prefix in prefixes)
        {
            if (prefix.Length > 512 || prefix.Any(c => char.IsControl(c) || c is '*' or '?' or '[' or ']' or ':' or '\\') ||
                Path.IsPathRooted(prefix) || prefix.Split('/').Any(s => s is "" or "." or ".." || s.Equals(".git", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Only exact relative Git prefixes are accepted.");
            ProductionPaths.CheckPath(ProductionPaths.Resolve(context.Storage.ProductionRoot, prefix), NapIssueCodes.GitRepositoryInvalid);
        }
    }
}

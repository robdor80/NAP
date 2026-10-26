using System.Security.Cryptography;

namespace NAP.Core;

/// <summary>Explicit Git authorization, independent of ZIP grants and settings v1. No automatic grant creation.</summary>
public sealed class GitPublicationPolicyStore
{
    private readonly UniverseContext _context;
    public string DirectoryPath { get; }
    private string PolicyPath => Path.Combine(DirectoryPath, "policy.json");
    private string KeyPath => Path.Combine(DirectoryPath, "integrity.key");
    internal string OutboxDirectory => Path.Combine(_context.Storage.StateRoot, "git-outbox-v1");
    public GitPublicationPolicyStore(UniverseContext context, string? directory = null)
    {
        _context = context;
        DirectoryPath = Path.GetFullPath(directory ?? Path.Combine(Path.GetDirectoryName(LocalUniverseSettingsStore.DefaultPath)!,
            "git-publication", context.Id.Value + "-" + GitProductionSafety.Binding(context)));
        if (directory is not null && !Path.IsPathFullyQualified(directory)) throw new ArgumentException("Absolute policy directory required.");
        Guard();
    }
    internal IDisposable Lock() => ExecutionMutex.Acquire("GitPublicationPolicy", DirectoryPath);
    private void Guard()
    {
        foreach (var root in new[] { _context.Storage.WorkspaceRoot, _context.Storage.ProductionRoot, _context.Storage.ArchiveRoot })
            if (ProductionPaths.Overlaps(root, DirectoryPath)) throw new InvalidDataException("Git configuration must be outside universe storage.");
        for (var p = DirectoryPath; p is not null; p = Path.GetDirectoryName(p))
            if (File.Exists(Path.Combine(p, ".git")) || Directory.Exists(Path.Combine(p, ".git"))) throw new InvalidDataException("Git configuration cannot be in a checkout.");
        GitPublicationFiles.Inventory(DirectoryPath, "policy.json", "integrity.key");
    }
    internal byte[] Key()
    {
        Guard(); var bytes = GitPublicationFiles.ReadBytes(KeyPath);
        if (bytes.Length != 32) throw new InvalidDataException("Invalid publication integrity key.");
        return bytes;
    }
    public GitPublicationPolicyV1? Load()
    {
        using var lease = Lock(); return LoadLocked();
    }
    internal GitPublicationPolicyV1? LoadLocked()
    {
        Guard();
        if (!File.Exists(PolicyPath))
        {
            if (File.Exists(KeyPath) || Directory.Exists(OutboxDirectory)) throw new InvalidDataException("Partial or removed Git configuration requires review.");
            return null; // Disabled, without remote inspection or writes.
        }
        var policy = GitPublicationFiles.Read<GitPublicationPolicyV1>(PolicyPath, Key()); Validate(policy);
        var anchor = new GitOutboxStore(_context, this).LoadLocked();
        if (anchor.PolicyRevision != policy.PolicyRevision || anchor.PolicyHash != AutomationJson.Hash(policy).Hex)
            throw new InvalidDataException("Policy revision or integrity anchor changed.");
        return policy;
    }
    /// <summary>Records an explicit reviewed grant. Expected revision is CAS; first grant uses zero.</summary>
    public void Save(GitPublicationPolicyV1 policy, long expectedRevision)
    {
        using var owner = AutomationManualAccess.Enter(_context);
        using var lease = Lock(); Guard(); Validate(policy);
        var prior = LoadLocked();
        if ((prior?.PolicyRevision ?? 0) != expectedRevision || policy.PolicyRevision != expectedRevision + 1 ||
            prior is not null && prior.AuthorizationId == policy.AuthorizationId)
            throw new InvalidDataException("A new revision and authorization ID are required.");
        GitPublicationFiles.DirectoryGuard(DirectoryPath, true);
        if (prior is null) GitPublicationFiles.WriteBytes(KeyPath, RandomNumberGenerator.GetBytes(32), false);
        var key = Key(); var outbox = new GitOutboxStore(_context, this);
        var state = prior is null ? outbox.Empty() : outbox.LoadLocked();
        // Anchor first: a crash between these finals blocks rather than resurrecting an old grant.
        var hash = AutomationJson.Hash(policy).Hex;
        outbox.SaveLocked(state with { PolicyRevision = policy.PolicyRevision, PolicyHash = hash,
            PolicyHistory = state.PolicyHistory.Append(new GitPolicyRevision(policy.PolicyRevision, hash, policy.AuthorizationId,
                policy.Actor, policy.AuthorizedUtc, policy.Mode, policy.Revoked)).ToArray(),
            Intents = state.Intents.Select(i => i.State == GitOutboxState.Pending ? i with { State = GitOutboxState.Blocked, Diagnostic = "git_policy_changed" } : i).ToArray(),
            Batches = state.Batches.Select(b => b.State == GitOutboxState.Pending ? b with { State = GitOutboxState.Blocked, Diagnostic = "git_policy_changed" } : b).ToArray() });
        GitPublicationFiles.Write(PolicyPath, policy, key, prior is not null);
        _ = LoadLocked();
    }
    /// <summary>Read-only proposal only. Caller must review, choose permissions, then explicitly Save.</summary>
    public GitPublicationPolicyV1 Propose(GitPublicationMode mode, string actor, IReadOnlyList<string> prefixes,
        int maxJobsPerBatch = 5, GitProductionInspector? inspector = null)
    {
        using var owner = AutomationManualAccess.Enter(_context);
        var previous = Load(); var i = inspector ?? new GitProductionInspector(); var status = i.Inspect(_context);
        using var production = GitProductionSafety.Acquire(_context);
        return new(1, _context.Id, GitProductionSafety.Binding(_context), AutomationJson.Hash(_context.Storage).Hex, Path.GetFullPath(_context.Storage.ProductionRoot),
            Path.GetFullPath(_context.Storage.StateRoot), mode, (previous?.PolicyRevision ?? 0) + 1,
            "authorization_" + Guid.NewGuid().ToString("N"), actor, DateTimeOffset.UtcNow,
            Identity(_context, i.Git), status.Branch, status.Upstream, status.RemoteName, status.RemoteBranch,
            status.RemoteFingerprint, status.ConfigurationFingerprint, prefixes.Order(StringComparer.Ordinal).ToArray(),
            maxJobsPerBatch, false, false, false);
    }
    internal static string Identity(UniverseContext c, GitProductionProcess git)
    {
        var config = GitProductionSafety.Configuration(c, git);
        if (!config.TryGetValue("user.name", out var name) || !config.TryGetValue("user.email", out var email))
            throw new InvalidDataException("Explicit Git identity required.");
        string StripTime(string identity) { GitProductionService.ValidateIdentity(identity); return identity[..(identity.LastIndexOf("> ", StringComparison.Ordinal) + 1)]; }
        return AutomationJson.Hash(new { Name = name.Last(), Email = email.Last(), Author = StripTime(git.Identity(c, true)), Committer = StripTime(git.Identity(c, false)) }).Hex;
    }
    internal void Require(GitPublicationPolicyV1 p, GitRepositoryStatus status, GitProductionProcess git,
        IEnumerable<string> paths, bool push = false, bool automatic = false)
    {
        var current = LoadLocked();
        if (current is null || AutomationJson.Encode(current) != AutomationJson.Encode(p) || p.Revoked ||
            p.Mode == GitPublicationMode.Disabled || !p.AllowCommit || push && (!p.AllowPush || automatic) ||
            automatic && p.Mode is not (GitPublicationMode.AutoCommitBatch or GitPublicationMode.AutoCommitAndPushBatch) ||
            status.RepositoryBinding != p.RepositoryBinding || status.UniverseId != p.UniverseId || status.Branch != p.LocalBranch ||
            status.Upstream != p.UpstreamRef || status.RemoteName != p.RemoteName || status.RemoteBranch != p.RemoteBranch ||
            status.RemoteFingerprint != p.RemoteFingerprint || status.ConfigurationFingerprint != p.ConfigFingerprint ||
            Identity(_context, git) != p.GitIdentityFingerprint || paths.Any(path => !p.AllowedRelativePrefixes.Any(prefix => path.StartsWith(prefix + "/", StringComparison.Ordinal))))
            throw new InvalidDataException("Git publication is not authorized by the exact current policy.");
    }
    private void Validate(GitPublicationPolicyV1 p)
    {
        if (p.SchemaVersion != 1 || p.UniverseId != _context.Id || p.RepositoryBinding != GitProductionSafety.Binding(_context) || p.StorageFingerprint != AutomationJson.Hash(_context.Storage).Hex ||
            p.CanonicalProductionRoot != Path.GetFullPath(_context.Storage.ProductionRoot) || p.CanonicalStateRoot != Path.GetFullPath(_context.Storage.StateRoot) ||
            !Enum.IsDefined(p.Mode) || p.PolicyRevision < 1 || p.MaxJobsPerBatch is < 1 or > 20 ||
            p.AllowPush && (!p.AllowCommit || p.Mode is not (GitPublicationMode.Manual or GitPublicationMode.AutoCommitAndPushBatch)) ||
            p.Mode == GitPublicationMode.Disabled && (p.AllowCommit || p.AllowPush) || p.AllowedRelativePrefixes is null ||
            p.AllowedRelativePrefixes.Count is < 1 or > 64 || !p.AllowedRelativePrefixes.SequenceEqual(p.AllowedRelativePrefixes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)))
            throw new InvalidDataException("Invalid Git publication policy scope or permissions.");
        AutomationValidation.Text(p.Actor); AutomationValidation.Text(p.AuthorizationId); AutomationValidation.Utc(p.AuthorizedUtc);
        foreach (var hash in new[] { p.RepositoryBinding, p.StorageFingerprint, p.GitIdentityFingerprint, p.RemoteFingerprint, p.ConfigFingerprint }) _ = new Sha256Digest(hash);
        foreach (var name in new[] { p.LocalBranch, p.UpstreamRef, p.RemoteName, p.RemoteBranch }) GitProductionSafety.RefName(name);
        if (p.LocalBranch != p.RemoteBranch) throw new InvalidDataException("Branch scope mismatch.");
        foreach (var prefix in p.AllowedRelativePrefixes)
        {
            if (prefix.Contains('\\') || prefix.Split('/').Any(s => s.Length == 0 || s is "." or ".." || s.Equals(".git", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Invalid allowed prefix.");
            ProductionPaths.Resolve(_context.Storage.ProductionRoot, prefix);
        }
    }
}

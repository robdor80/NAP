namespace NAP.Core;

public enum GitChangeKind { Added, Modified, Deleted, Renamed, Copied, Untracked, Conflicted, TypeChanged }
public enum GitSynchronization { Synchronized, Ahead, Behind, Diverged, Unknown, RemoteBranchMissing }
public enum GitReceiptState { Prepared, Committed, Pushed }
public enum GitPushOutcome { Pushed, AlreadyPushed }

/// <summary>Factual porcelain data. Paths are exact Git relative names, never human-quoted names.</summary>
public sealed record GitChangeEntry
{
    internal GitChangeEntry(string path, string? original, char index, char worktree, GitChangeKind kind, bool submodule)
    { RelativePath = path; OriginalRelativePath = original; IndexStatus = index; WorktreeStatus = worktree; ChangeKind = kind; IsSubmodule = submodule; }
    public string RelativePath { get; }
    public string? OriginalRelativePath { get; }
    public char IndexStatus { get; }
    public char WorktreeStatus { get; }
    public GitChangeKind ChangeKind { get; }
    public bool IsUntracked => ChangeKind == GitChangeKind.Untracked;
    public bool IsTracked => !IsUntracked;
    public bool IsConflict => ChangeKind == GitChangeKind.Conflicted;
    public bool IsSubmodule { get; }
}

public sealed class GitRepositoryStatus
{
    internal GitRepositoryStatus(UniverseId universe, string binding, string head, string branch, string upstream,
        string remote, string remoteBranch, string fingerprint, string? tip, GitSynchronization synchronization,
        string configuration, string porcelain, IEnumerable<GitChangeEntry> changes)
    {
        UniverseId = universe; RepositoryBinding = binding; Head = head; Branch = branch; Upstream = upstream;
        RemoteName = remote; RemoteBranch = remoteBranch; RemoteFingerprint = fingerprint; RemoteTip = tip;
        Synchronization = synchronization; ConfigurationFingerprint = configuration; Porcelain = porcelain;
        Changes = Array.AsReadOnly(changes.OrderBy(c => c.RelativePath, StringComparer.Ordinal).ToArray());
    }
    public UniverseId UniverseId { get; }
    public string RepositoryBinding { get; }
    public string Head { get; }
    public string Branch { get; }
    public string Upstream { get; }
    public string RemoteName { get; }
    public string RemoteBranch { get; }
    public string RemoteFingerprint { get; }
    public string? RemoteTip { get; }
    public GitSynchronization Synchronization { get; }
    public IReadOnlyList<GitChangeEntry> Changes { get; }
    public bool IsClean => Changes.Count == 0;
    public bool IndexIsClean => Changes.All(c => c.IsUntracked || c.IndexStatus == '.');
    internal string ConfigurationFingerprint { get; }
    internal string Porcelain { get; }
}

public sealed record GitOwnedFile
{
    internal GitOwnedFile(string relativePath, Sha256Digest digest, long size)
    { RelativePath = relativePath; Digest = digest; SizeBytes = size; }
    public string RelativePath { get; }
    public Sha256Digest Digest { get; }
    public long SizeBytes { get; }
}

/// <summary>Only the service can derive a selection, using verified production results.</summary>
public sealed class GitCommitSelection
{
    internal GitCommitSelection(IEnumerable<GitOwnedFile> owned, IEnumerable<string> changed)
    { OwnedFiles = Array.AsReadOnly(owned.OrderBy(f => f.RelativePath, StringComparer.Ordinal).ToArray()); Paths = Array.AsReadOnly(changed.Order(StringComparer.Ordinal).ToArray()); }
    public IReadOnlyList<GitOwnedFile> OwnedFiles { get; }
    public IReadOnlyList<string> Paths { get; }
}

public sealed class GitCommitPlan
{
    internal GitCommitPlan(GitRepositoryStatus status, GitCommitSelection selection, string message)
    { Status = status; Selection = selection; Message = message; }
    public GitRepositoryStatus Status { get; }
    public GitCommitSelection Selection { get; }
    public string Message { get; }
    public UniverseId UniverseId => Status.UniverseId;
}

public sealed record GitOperationId
{
    public GitOperationId(string value)
    {
        if (value is null || value.Length != 34 || !value.StartsWith("g_", StringComparison.Ordinal) ||
            value[2..].Any(c => !(c is >= '0' and <= '9' or >= 'a' and <= 'f')) || value[2..].All(c => c == '0'))
            throw new ArgumentException("Expected a Git operation identity.", nameof(value));
        Value = value;
    }
    public string Value { get; }
    internal static GitOperationId New() => new("g_" + Guid.NewGuid().ToString("N"));
    public override string ToString() => Value;
}

public sealed class GitOperationReceipt
{
    internal GitOperationReceipt(GitOperationId id, GitCommitPlan plan, GitReceiptState state,
        string? expectedTree = null, string? expectedCommit = null, string? commit = null, string? author = null, string? committer = null)
    { OperationId = id; Plan = plan; State = state; ExpectedTree = expectedTree; ExpectedCommitSha = expectedCommit; CommitSha = commit; AuthorIdentity = author; CommitterIdentity = committer; }
    public int SchemaVersion => 1;
    public GitOperationId OperationId { get; }
    public UniverseId UniverseId => Plan.UniverseId;
    public GitReceiptState State { get; }
    public string BaseHead => Plan.Status.Head;
    public string Branch => Plan.Status.Branch;
    public string RemoteName => Plan.Status.RemoteName;
    public string RemoteFingerprint => Plan.Status.RemoteFingerprint;
    public string? CommitSha { get; }
    public string? ExpectedCommitSha { get; }
    internal string? ExpectedTree { get; }
    internal string? AuthorIdentity { get; }
    internal string? CommitterIdentity { get; }
    internal GitCommitPlan Plan { get; }
}

public sealed class GitCommitResult
{
    internal GitCommitResult(GitOperationReceipt receipt)
    { OperationId = receipt.OperationId; UniverseId = receipt.UniverseId; OldHead = receipt.BaseHead; CommitSha = receipt.CommitSha!;
      Branch = receipt.Branch; CommittedPaths = receipt.Plan.Selection.Paths; Message = receipt.Plan.Message; RepositoryBinding = receipt.Plan.Status.RepositoryBinding; }
    public GitOperationId OperationId { get; }
    public UniverseId UniverseId { get; }
    public string OldHead { get; }
    public string CommitSha { get; }
    public string Branch { get; }
    public IReadOnlyList<string> CommittedPaths { get; }
    public string Message { get; }
    internal string RepositoryBinding { get; }
}

public sealed record GitPushResult(GitOperationId OperationId, string CommitSha, GitPushOutcome Outcome);

public sealed class GitProductionException : IOException
{
    internal GitProductionException(string code, string message) : base(message)
    { Issues = new NapIssueReport([new NapIssue(code, NapIssueSeverity.Error, NapIssueDisposition.Stop, message)]); }
    public NapIssueReport Issues { get; }
    internal static GitProductionException Stop(string code, string message) => new(code, message);
}

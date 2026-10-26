namespace NAP.Core;

public enum GitPublicationMode { Disabled, Manual, AutoCommitBatch, AutoCommitAndPushBatch }
public enum GitOutboxState { Pending, Committed, Blocked, Pushed }
public sealed record CompletedProductionEvidence(QueueItemId ItemId, WorkflowAttemptId AttemptId, JobId JobId,
    Sha256Digest SnapshotHash, ProductionAssetResult Production);

public sealed record GitPublicationPolicyV1(int SchemaVersion, UniverseId UniverseId, string RepositoryBinding, string StorageFingerprint,
    string CanonicalProductionRoot, string CanonicalStateRoot, GitPublicationMode Mode, long PolicyRevision,
    string AuthorizationId, string Actor, DateTimeOffset AuthorizedUtc, string GitIdentityFingerprint,
    string LocalBranch, string UpstreamRef, string RemoteName, string RemoteBranch, string RemoteFingerprint,
    string ConfigFingerprint, IReadOnlyList<string> AllowedRelativePrefixes, int MaxJobsPerBatch,
    bool AllowCommit, bool AllowPush, bool Revoked);

public sealed record GitOutboxIntent(string Id, QueueItemId ItemId, WorkflowAttemptId AttemptId, JobId JobId, QueueBatchId? SourceBatchId,
    string SnapshotHash, long PolicyRevision, string AuthorizationId, GitOutboxState State, string? Diagnostic,
    string? BatchId);
public sealed record GitOutboxBatch(string Id, IReadOnlyList<string> IntentIds, GitOperationId OperationId,
    string PlanHash, GitOutboxState State, string? CommitSha, string? Diagnostic);
public sealed record GitPolicyRevision(long Revision, string PolicyHash, string AuthorizationId, string Actor,
    DateTimeOffset AuthorizedUtc, GitPublicationMode Mode, bool Revoked);
public sealed record GitOutboxSnapshot(int SchemaVersion, UniverseId UniverseId, string RepositoryBinding,
    long PolicyRevision, string? PolicyHash, IReadOnlyList<GitPolicyRevision> PolicyHistory, long CompletionCursor, IReadOnlyList<GitOutboxIntent> Intents,
    IReadOnlyList<GitOutboxBatch> Batches);

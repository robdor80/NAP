namespace NAP.Core;

public enum PreparationCheckpoint { Intent, Preserved, CandidateIntent, CandidateReady, ValidationIntent, Validated, PlanningIntent, Planned }
public enum PreparationCode { Planned, Duplicate, NeedsReview, InvalidAdmission, InvalidPackage, GeometryPolicyRequired,
    NormalizationLimitExceeded, ResourceLimit, AssetCollision, MasterDuplicate, ExistingStorage, EvidenceChanged,
    PolicyInactive, AuditConfigurationRequired, AuditPass, AuditWarning, AuditFail, AuditRetryScheduled,
    AuditBudgetExhausted, AuditDependencyBlocked, AuditInvalidResponse, Interrupted, StorageUnavailable, Pending }
public enum DurableAuditState { NotRequested, RequestIntent, ResponseReady, Pass, Warning, Fail, RetryScheduled, Blocked }
public enum AuditCallState { Started, TransientFailure, PermanentFailure, Response }
public sealed record PreparationResult(QueueItemId ItemId, WorkflowAttemptId AttemptId, JobId JobId,
    PreparationCode Code, DateTimeOffset? RetryAtUtc = null, QueueItemId? DuplicateOf = null);
public sealed record PreparationBundle(string RelativeDirectory, Sha256Digest ReceiptHash,
    IReadOnlyList<AdmissionFile> Files);
public sealed record PreparationBundleReceipt(int SchemaVersion, string OperationId, QueueItemId ItemId,
    WorkflowAttemptId AttemptId, JobId JobId, UniverseId UniverseId, string Purpose, Sha256Digest AdmissionHash,
    IReadOnlyList<AdmissionFile> Files, NormalizationAuthority? Authority);
public sealed record PreparationPlanFacts(string DestinationRelativeDirectory, string ArchiveRelativeDirectory,
    ArchiveMasterAction ArchiveAction, IReadOnlyList<PreparationRoleFile> Files, IReadOnlyList<AiAuditIssueFact> Issues);
public sealed record PreparationRoleFile(string Role, string FileName, Sha256Digest Hash, long SizeBytes);
public sealed record PreparationSnapshot(int SchemaVersion, QueueItemId ItemId, WorkflowAttemptId AttemptId,
    JobId JobId, UniverseId UniverseId, UniverseStorageConfig Roots, PolicyReference Policy,
    Sha256Digest AdmissionHash, QueueZipIdentity OriginalZip, IReadOnlyList<AdmissionFile> OriginalFiles,
    IReadOnlyList<AdmissionFile> EffectiveFiles, EffectiveRuleSnapshot BaseProfile, EffectiveRuleSnapshot EffectiveProfile,
    string EngineIdentity, string ManifestJson, PreparationPlanFacts Plan, string? NormalizationOperation,
    NormalizationAuthority? Authority, ProfileNormalizationPolicy? Parameters, ImageNormalizationGeometry? Geometry,
    string? ExceptionalDecisionId, Sha256Digest? ExceptionalPreviewHash)
{
    [System.Text.Json.Serialization.JsonIgnore] public Sha256Digest Hash => AutomationJson.Hash(this);
}
/// <summary>No defaults grant audit capability. These complete terms require an explicit global attestation.</summary>
public sealed record PreparationOperationalGrant(string GrantId, int Version, PolicyReference Policy,
    string PolicyAuthorizationId, string Actor, DateTimeOffset AuthorizedUtc, string ProviderId, string ModelId,
    decimal MaximumCostPerRequest, IReadOnlyList<int> RetryDelaySeconds, int CircuitFailureThreshold,
    int CircuitOpenSeconds, bool AllowDuplicateMasters)
{
    [System.Text.Json.Serialization.JsonIgnore] public Sha256Digest Hash => AutomationJson.Hash(this);
}
public interface IIdentifiedAiAuditClient : IAiAuditClient
{
    string ProviderId { get; }
    string ModelId { get; }
}
public sealed record DurableAuditCall(string RequestId, int Number, DateTimeOffset StartedUtc,
    AuditCallState State, AiAuditErrorKind? Error, int? HttpStatus, DateTimeOffset? FinishedUtc);
public sealed record DurableAuditReceipt(int SchemaVersion, string RequestId, QueueItemId ItemId,
    WorkflowAttemptId AttemptId, JobId JobId, UniverseId UniverseId, Sha256Digest SnapshotHash,
    Sha256Digest RequestHash, PolicyReference Policy, Sha256Digest GrantHash, string ProviderId, string ModelId,
    DateTimeOffset RespondedUtc, AiAuditDecision Decision, string Summary, IReadOnlyList<AiAuditFinding> Findings)
{
    public AiAuditReport ValidateReport() => new(Decision, Summary, Findings);
}
public sealed record ExceptionalPreparationPreview(string DecisionId, QueueItemId ItemId, WorkflowAttemptId AttemptId,
    UniverseId UniverseId, EffectiveRuleSnapshot EffectiveRule, ProfileNormalizationPolicy Parameters,
    Sha256Digest OriginalHash, Sha256Digest CandidateHash, ImageNormalizationGeometry Geometry, PreparationBundle Bundle)
{
    [System.Text.Json.Serialization.JsonIgnore] public Sha256Digest Hash => AutomationJson.Hash(this);
}
public sealed record PreparationRecord
{
    public required QueueItemId ItemId { get; init; }
    public required WorkflowAttemptId AttemptId { get; init; }
    public required JobId JobId { get; init; }
    public required UniverseId UniverseId { get; init; }
    public required string OperationId { get; init; }
    public required Sha256Digest AdmissionHash { get; init; }
    public required EffectiveRuleSnapshot BaseProfile { get; init; }
    public required EffectiveRuleSnapshot EffectiveProfile { get; init; }
    public required PolicyReference Policy { get; init; }
    public long Revision { get; init; }
    public PreparationCheckpoint Checkpoint { get; init; }
    public PreparationCode Code { get; init; }
    public IReadOnlyList<string> DiagnosticCodes { get; init; } = [];
    public PreparationBundle? Originals { get; init; }
    public PreparationBundle? Candidate { get; init; }
    public Sha256Digest? SemanticHash { get; init; }
    public Sha256Digest? EffectiveSemanticHash { get; init; }
    public Sha256Digest? MasterHash { get; init; }
    public QueueItemId? DuplicateOf { get; init; }
    public NormalizationAuthority? Authority { get; init; }
    public ProfileNormalizationPolicy? Parameters { get; init; }
    public ImageNormalizationGeometry? Geometry { get; init; }
    public string? ExceptionalDecisionId { get; init; }
    public Sha256Digest? ExceptionalPreviewHash { get; init; }
    public PreparationSnapshot? Snapshot { get; init; }
    public DurableAuditState AuditState { get; init; }
    public string? RequestJson { get; init; }
    public Sha256Digest? RequestHash { get; init; }
    public PreparationOperationalGrant? AuditGrant { get; init; }
    public DateTimeOffset? RetryAtUtc { get; init; }
    public IReadOnlyList<DurableAuditCall> Calls { get; init; } = [];
    public PreparationBundle? AuditResponse { get; init; }
    public DurableAuditReceipt? ExpectedAuditReceipt { get; init; }
    public required DateTimeOffset UpdatedUtc { get; init; }
}

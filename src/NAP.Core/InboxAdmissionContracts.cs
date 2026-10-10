namespace NAP.Core;

public enum InboxAdmissionCode
{
    Admitted, AlreadyKnown, WaitingStable, InUse, IncompleteZip, MissingSource, SourceChanged,
    UniverseMismatch, InvalidManifest, InvalidPackage, UnsafeZip, InvalidZip, ResourceLimit,
    ProfileUnknown, AmbiguousEvidence, StorageUnavailable
}
public enum AdmissionCheckpoint { Intent, SnapshotReady, ExtractionReady, JournalIntent, Admitted, Rejected, NeedsReview }
public sealed record InboxAdmissionResult(UniverseId UniverseId, string SourcePath, InboxAdmissionCode Code,
    QueueItemId? ItemId = null, JobId? JobId = null, string? DeclaredUniverse = null);
public sealed record AdmissionFile(string RelativePath, Sha256Digest Hash, long SizeBytes);
public sealed record AdmissionReceipt(int Version, QueueItemId ItemId, WorkflowAttemptId AttemptId, JobId JobId,
    UniverseId UniverseId, QueueZipIdentity Zip, string SourcePath, string SnapshotRelativePath,
    string ExtractionRelativePath, AdmissionCheckpoint Checkpoint, DateTimeOffset Utc, Sha256Digest? PreviousHash,
    IReadOnlyList<AdmissionFile> Files, string? DeclaredUniverse, string? AssetId, InboxAdmissionCode Code);

/// <summary>Admission budgets are independent of production authorization, which remains Disabled.</summary>
public sealed record InboxAdmissionOptions
{
    public int StabilitySamples { get; init; } = 3;
    public TimeSpan SampleInterval { get; init; } = TimeSpan.FromMilliseconds(2500);
    public TimeSpan InvalidZipGrace { get; init; } = TimeSpan.FromMinutes(5);
    public long MaxZipBytes { get; init; } = 256L * 1024 * 1024;
    public StagedPackageExtractionOptions Extraction { get; init; } = new();
    public long MaxRetainedBytes { get; init; } = 2L * 1024 * 1024 * 1024;
    public long MinFreeBytes { get; init; } = 256L * 1024 * 1024;
    public int MaxPendingCandidates { get; init; } = 64;
    public TimeSpan RescanInterval { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan Debounce { get; init; } = TimeSpan.FromMilliseconds(500);
    internal void Validate()
    {
        if (StabilitySamples < 2 || StabilitySamples > 10 || SampleInterval < TimeSpan.Zero || SampleInterval > TimeSpan.FromMinutes(1) ||
            InvalidZipGrace < TimeSpan.Zero || MaxZipBytes <= 0 || MaxZipBytes > 512L * 1024 * 1024 ||
            Extraction.MaxEntries is < 1 or > 10000 || Extraction.MaxEntryBytes <= 0 || Extraction.MaxEntryBytes > 512L * 1024 * 1024 ||
            Extraction.MaxTotalBytes <= 0 || Extraction.MaxTotalBytes > 2L * 1024 * 1024 * 1024 || MaxRetainedBytes <= 0 || MinFreeBytes < 0 ||
            MaxPendingCandidates is < 1 or > 4096 || RescanInterval <= TimeSpan.Zero || Debounce < TimeSpan.Zero)
            throw AutomationValidation.Invalid("Invalid or excessive admission limits.");
    }
}
public enum InboxMonitorState { Starting, Monitoring, Blocked, Stopped }
public sealed record InboxMonitorStatus(UniverseId UniverseId, InboxMonitorState State, int PendingCandidates,
    long Scans, long WatcherErrors, InboxAdmissionResult? LastResult, AutomationError? Error);

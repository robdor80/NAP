namespace NAP.Core;

public sealed record BackupId
{
    public BackupId(string value)
    {
        if (value is null || value.Length != 34 || !value.StartsWith("b_", StringComparison.Ordinal) ||
            value[2..].Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("Expected b_ followed by 32 lowercase hexadecimal characters.", nameof(value));
        Value = value;
    }
    public string Value { get; }
    public static BackupId New() => new("b_" + Guid.NewGuid().ToString("N"));
    public override string ToString() => Value;
}

public enum BackupKind { Database, RepositorySnapshot, GitBundle }
public enum DatabaseBackupPurpose { Manual, PreRestore }
public sealed record BackupFile(string RelativePath, long Size, Sha256Digest Sha256);

/// <summary>Closed version 1 metadata. All paths are portable and relative to the artifact.</summary>
public sealed class BackupManifest
{
    internal BackupManifest(BackupId id, UniverseId universe, BackupKind kind, DateTimeOffset created,
        long size, Sha256Digest sha, int? catalogVersion = null, DatabaseBackupPurpose? purpose = null,
        IEnumerable<BackupFile>? files = null, IEnumerable<string>? directories = null, string? head = null, bool? dirty = null)
    {
        BackupId = id; UniverseId = universe; Kind = kind; CreatedUtc = created;
        Size = size; Sha256 = sha; CatalogSchemaVersion = catalogVersion; DatabasePurpose = purpose;
        Files = Array.AsReadOnly((files ?? []).ToArray()); Directories = Array.AsReadOnly((directories ?? []).ToArray());
        Head = head; Dirty = dirty;
    }
    public int SchemaVersion => 1;
    public BackupId BackupId { get; }
    public UniverseId UniverseId { get; }
    public BackupKind Kind { get; }
    public DateTimeOffset CreatedUtc { get; }
    public string Artifact => Kind switch { BackupKind.Database => "catalog.db", BackupKind.RepositorySnapshot => "tree", _ => "history.bundle" };
    public long Size { get; }
    public Sha256Digest Sha256 { get; }
    public int? CatalogSchemaVersion { get; }
    public DatabaseBackupPurpose? DatabasePurpose { get; }
    public IReadOnlyList<BackupFile> Files { get; }
    public IReadOnlyList<string> Directories { get; }
    public string? Head { get; }
    public bool? Dirty { get; }
}

public sealed class BackupHistoryEntry
{
    internal BackupHistoryEntry(BackupManifest manifest, Sha256Digest manifestSha)
    { Manifest = manifest; ManifestSha256 = manifestSha; }
    public BackupManifest Manifest { get; }
    public BackupId BackupId => Manifest.BackupId;
    public UniverseId UniverseId => Manifest.UniverseId;
    public BackupKind Kind => Manifest.Kind;
    public DateTimeOffset CreatedUtc => Manifest.CreatedUtc;
    public long Size => Manifest.Size;
    public Sha256Digest Sha256 => Manifest.Sha256;
    public bool LocallyVerified => true;
    internal Sha256Digest ManifestSha256 { get; }
}

public sealed record BackupHistory(IReadOnlyList<BackupHistoryEntry> Entries, NapIssueReport Issues);

public sealed class BackupException : IOException
{
    internal BackupException(NapIssueReport issues, IEnumerable<BackupId>? completed = null)
        : base(string.Join("; ", issues.Issues.Select(i => i.Message)))
    { Issues = issues; CompletedBackupIds = Array.AsReadOnly((completed ?? []).ToArray()); }
    public NapIssueReport Issues { get; }
    /// <summary>Explicit progress if a filesystem failure interrupted retention. Never silently partial.</summary>
    public IReadOnlyList<BackupId> CompletedBackupIds { get; }
    internal static BackupException Stop(string code, string message, IEnumerable<BackupId>? completed = null) =>
        new(new NapIssueReport([new NapIssue(code, NapIssueSeverity.Error, NapIssueDisposition.Stop, message)]), completed);
}

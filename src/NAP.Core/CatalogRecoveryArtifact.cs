namespace NAP.Core;

/// <summary>Identity of forensic evidence, deliberately incompatible with BackupId.</summary>
public sealed record CatalogRecoveryId
{
    public CatalogRecoveryId(string value)
    {
        if (value is null || value.Length != 41 || !value.StartsWith("recovery_", StringComparison.Ordinal) ||
            value[9..].Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("Expected recovery_ followed by 32 lowercase hexadecimal characters.", nameof(value));
        Value = value;
    }
    public string Value { get; }
    public static CatalogRecoveryId New() => new("recovery_" + Guid.NewGuid().ToString("N"));
    public override string ToString() => Value;
}

/// <summary>Byte-verified evidence of a corrupt active catalog. This is not a valid SQLite backup.</summary>
public sealed class CatalogRecoveryArtifact
{
    internal CatalogRecoveryArtifact(CatalogRecoveryId id, UniverseId universe, DateTimeOffset created, long size, Sha256Digest sha)
    { RecoveryId = id; UniverseId = universe; CreatedUtc = created; Size = size; Sha256 = sha; }
    public int SchemaVersion => 1;
    public CatalogRecoveryId RecoveryId { get; }
    public UniverseId UniverseId { get; }
    public DateTimeOffset CreatedUtc { get; }
    public string Reason => "active_catalog_corrupt";
    public string Artifact => "AssetCatalog.corrupt.db";
    public long Size { get; }
    public Sha256Digest Sha256 { get; }
}

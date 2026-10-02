namespace NAP.Core;

/// <summary>Absolute limits for a single ZIP extraction.</summary>
public sealed record StagedPackageExtractionOptions
{
    public int MaxEntries { get; init; } = 10_000;
    public long MaxEntryBytes { get; init; } = 512L * 1024 * 1024;
    public long MaxTotalBytes { get; init; } = 2L * 1024 * 1024 * 1024;
}

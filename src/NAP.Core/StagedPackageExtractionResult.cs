namespace NAP.Core;

/// <summary>The outcome of an extraction attempt. FinalPath is set only after publication.</summary>
public sealed record StagedPackageExtractionResult(
    StagedPackageExtractionStatus Status,
    string StagedZipPath,
    string? FinalPath = null,
    string? Reason = null);

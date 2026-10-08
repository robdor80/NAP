using System.Collections.ObjectModel;

namespace NAP.Core;

public sealed class ImageNormalizationProposal
{
    internal ImageNormalizationProposal(UniverseContext context, string source, string digest, string assetId, string assetType,
        string originalProfile, UniverseAssetRule rule, ImageNormalizationPolicy policy, ImageNormalizationPreview preview,
        Dictionary<string, byte[]>? packageFiles)
    {
        Context = context; SourcePath = source; SourceSha256 = digest; AssetKey = new(context.Id, assetId); AssetType = assetType;
        OriginalProfile = originalProfile; TargetProfile = rule.ProductionProfile; Conversion = rule.Conversion!;
        Policy = policy; Preview = preview; Files = packageFiles; OperationId = Guid.NewGuid().ToString("N");
        CandidatePngFileName = rule.PackageFiles.Single(f => f.Role == Conversion.SourceRole).ResolveFileName(assetId);
        OriginalPngFileName = packageFiles is null ? Path.GetFileName(source) : CandidatePngFileName;
        if (!ArchivePaths.SafeSegment(OriginalPngFileName)) throw ImageNormalizationException.Stop("normalization_source_invalid", "El nombre del maestro debe ser seguro y portable.");
        EvidenceSha256 = ImageNormalizationEngine.Hash(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            universe = context.Id.Value, assetId, assetType, source, digest, originalProfile, TargetProfile, Conversion, policy,
            preview.OriginalSha256, preview.CandidateSha256, preview.Geometry, preview.Method,
            files = packageFiles?.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => new { name = f.Key, sha256 = ImageNormalizationEngine.Hash(f.Value) })
        }));
    }
    internal UniverseContext Context { get; }
    internal Dictionary<string, byte[]>? Files { get; }
    public string OperationId { get; }
    public string EvidenceSha256 { get; }
    public string SourcePath { get; }
    public string SourceSha256 { get; }
    public string OriginalPngFileName { get; }
    public string CandidatePngFileName { get; }
    public UniverseAssetKey AssetKey { get; }
    public string AssetType { get; }
    public string OriginalProfile { get; }
    public string TargetProfile { get; }
    public ImageConversionRule Conversion { get; }
    public ImageNormalizationPolicy Policy { get; }
    public ImageNormalizationPreview Preview { get; }
    public bool IsPackage => Files is not null;
    public bool RequiresMetadataApproval => OriginalProfile != TargetProfile;
}

/// <summary>Caller attests an explicit human decision on this exact proposal; no default approval.</summary>
public sealed record ImageNormalizationAuthorization(string OperationId, string EvidenceSha256,
    bool Approved, bool MetadataCorrectionApproved = false);

public sealed record ImageNormalizationReceipt
{
    public required int SchemaVersion { get; init; }
    public required string OperationId { get; init; }
    public required string UniverseId { get; init; }
    public required string AssetId { get; init; }
    public required string AssetType { get; init; }
    public required bool IsPackage { get; init; }
    public required string SourcePath { get; init; }
    public required string SourceSha256 { get; init; }
    public required string OriginalPngSha256 { get; init; }
    public required string OriginalPngFileName { get; init; }
    public required string CandidatePngFileName { get; init; }
    public required string CandidatePngSha256 { get; init; }
    public required string EvidenceSha256 { get; init; }
    public required string OriginalProfile { get; init; }
    public required string TargetProfile { get; init; }
    public required ImageConversionRule Conversion { get; init; }
    public required ImageNormalizationGeometry Geometry { get; init; }
    public required ImageNormalizationPolicy Policy { get; init; }
    public required string Method { get; init; }
    public required string Decision { get; init; }
    public required bool ApprovedByUser { get; init; }
    public required IReadOnlyList<string> IncidentCodes { get; init; }
    public required bool MetadataCorrectionApproved { get; init; }
    public required DateTimeOffset DecidedAtUtc { get; init; }
    public required IReadOnlyDictionary<string, string> OutputSha256 { get; init; }
}

public sealed class ImageNormalizationResult
{
    internal ImageNormalizationResult(ImageNormalizationReceipt receipt, string directory, bool alreadyRecorded)
    { Receipt = receipt; DirectoryPath = directory; AlreadyRecorded = alreadyRecorded; }
    public ImageNormalizationReceipt Receipt { get; }
    public string DirectoryPath { get; }
    public bool AlreadyRecorded { get; }
    public string? CandidatePath => Receipt.OutputSha256.Count == 1 ? Path.Combine(DirectoryPath, "candidate", Receipt.OutputSha256.Keys.Single()) : null;
}

public sealed record ImageNormalizationRecoverySnapshot(IReadOnlyList<ImageNormalizationResult> Recorded,
    IReadOnlyList<string> IncompleteDirectories, IReadOnlyList<string> Problems);

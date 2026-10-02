using System.Text.Json.Serialization;

namespace NAP.Core;

/// <summary>
/// Represents Manifest v1 data without normalization or semantic validation.
/// Required members express root-field presence, not validity of their values.
/// </summary>
public sealed record AssetManifestV1
{
    [JsonPropertyName("schema_version")]
    public required int SchemaVersion { get; init; }

    [JsonPropertyName("asset_id")]
    public required string AssetId { get; init; }

    [JsonPropertyName("asset_type")]
    public required string AssetType { get; init; }

    [JsonPropertyName("production_profile")]
    public required string ProductionProfile { get; init; }

    [JsonPropertyName("classification")]
    public required Dictionary<string, string> Classification { get; init; }
}

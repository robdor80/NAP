using System.Text.Json.Serialization;

namespace NAP.Core;

/// <summary>Manifest v2 transport data. Runtime identity uses UniverseId and UniverseAssetKey.</summary>
public sealed record AssetManifestV2
{
    [JsonPropertyName("schema_version")]
    public required int SchemaVersion { get; init; }

    [JsonPropertyName("universe_id")]
    public required string UniverseId { get; init; }

    [JsonPropertyName("asset_id")]
    public required string AssetId { get; init; }

    [JsonPropertyName("asset_type")]
    public required string AssetType { get; init; }

    [JsonPropertyName("production_profile")]
    public required string ProductionProfile { get; init; }

    [JsonPropertyName("classification")]
    public required Dictionary<string, string> Classification { get; init; }
}

using System.Text.Json;

namespace NAP.Core;

/// <summary>Canonical v4 representation of the complete declarative context, rechecked by the existing loader.</summary>
public static class UniverseProfileSnapshotRenderer
{
    public static string Render(UniverseProfile profile)
    {
        var value = new
        {
            schema_version = 4, universe_id = profile.Id.Value, display_name = profile.DisplayName,
            classification_dimensions = profile.ClassificationDimensions,
            asset_rules = profile.AssetRules.Select(r => new
            {
                asset_type = r.AssetType, production_profile = r.ProductionProfile,
                allowed_classification = r.AllowedClassification, required_classification = r.RequiredClassification,
                package_files = r.PackageFiles.Select(f => new { role = f.Role, suffix = f.Suffix, extension = f.Extension,
                    required = f.Required, content_validator = f.ContentValidator }),
                routing = r.Routing is null ? null : new { segments = r.Routing.Segments.Select(s =>
                    s.Kind switch
                    {
                        AssetRouteSegmentKind.Literal => new Dictionary<string, object> { ["literal"] = s.Value! },
                        AssetRouteSegmentKind.Classification => new Dictionary<string, object> { ["classification"] = s.Value! },
                        AssetRouteSegmentKind.AssetId => new Dictionary<string, object> { ["asset_id"] = true },
                        _ => throw new InvalidDataException("Unknown routing segment kind.")
                    }) },
                conversion = r.Conversion is null ? null : new { kind = "png_to_webp", source_role = r.Conversion.SourceRole,
                    output_width = r.Conversion.OutputWidth, output_height = r.Conversion.OutputHeight, webp_quality = r.Conversion.WebpQuality }
            })
        };
        var json = JsonSerializer.Serialize(value);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)); UniverseProfileLoader.Load(stream);
        return json;
    }
    public static UniverseContext Context(EffectiveRuleSnapshot rule, UniverseStorageConfig roots)
    {
        rule.ValidateAndGetRule();
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(rule.ProfileJson));
        return new(UniverseProfileLoader.Load(stream), roots);
    }
}

using System.Globalization;
using System.Text.Json;

namespace NAP.Core;

/// <summary>Strict Manifest v2 contract and Naming validation. Leaves caller-owned streams open.</summary>
public static class AssetManifestV2Loader
{
    public static AssetManifestV2 Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Load(stream);
    }

    public static AssetManifestV2 Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
            throw new ArgumentException("The manifest stream must be readable.", nameof(stream));
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        RequireKind(root, JsonValueKind.Object);
        var expected = new HashSet<string>(StringComparer.Ordinal)
            { "schema_version", "universe_id", "asset_id", "asset_type", "production_profile", "classification" };
        foreach (var property in root.EnumerateObject())
        {
            if (!expected.Remove(property.Name))
                throw new JsonException("Unknown or duplicate manifest property.");
        }
        if (expected.Count != 0)
            throw new JsonException("A required manifest property is missing.");

        var version = root.GetProperty("schema_version");
        RequireKind(version, JsonValueKind.Number);
        if (!IsVersionTwo(version))
            throw new InvalidDataException("Manifest schema version must be 2.");
        var universeId = Identifier(root.GetProperty("universe_id"));
        var assetId = String(root.GetProperty("asset_id"));
        if (!AssetNamingRules.IsValidAssetId(assetId))
            throw new InvalidDataException("Manifest asset_id must follow Naming v1.");
        var assetType = Identifier(root.GetProperty("asset_type"));
        var productionProfile = Identifier(root.GetProperty("production_profile"));
        if (!AssetNamingRules.MatchesAssetType(assetId, assetType))
            throw new InvalidDataException("Manifest asset_id must match the complete asset_type prefix.");

        var classification = root.GetProperty("classification");
        RequireKind(classification, JsonValueKind.Object);
        var dimensions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in classification.EnumerateObject())
        {
            if (!AssetNamingRules.IsValidMachineIdentifier(property.Name))
                throw new InvalidDataException("Classification keys must follow Naming v1.");
            if (!dimensions.TryAdd(property.Name, Identifier(property.Value)))
                throw new JsonException("Duplicate classification property.");
        }
        return new AssetManifestV2
        {
            SchemaVersion = 2, UniverseId = universeId, AssetId = assetId,
            AssetType = assetType, ProductionProfile = productionProfile, Classification = dimensions
        };
    }

    private static bool IsVersionTwo(JsonElement element)
    {
        // Compare this fixed numeric constant exactly; decimal/double parsing can round hostile fractions to 2.
        var text = element.GetRawText().AsSpan();
        var exponentIndex = text.IndexOfAny('e', 'E');
        var exponent = 0;
        if (exponentIndex >= 0 && !int.TryParse(text[(exponentIndex + 1)..], NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out exponent))
            return false;
        var mantissa = exponentIndex >= 0 ? text[..exponentIndex] : text;
        var point = mantissa.IndexOf('.');
        var fractionDigits = point < 0 ? 0 : mantissa.Length - point - 1;
        var hasTwo = false;
        var trailingZeros = 0;
        foreach (var character in mantissa)
        {
            if (character == '.') continue;
            if (character == '0') { if (hasTwo) trailingZeros++; continue; }
            if (character == '2' && !hasTwo) { hasTwo = true; continue; }
            return false;
        }
        return hasTwo && exponent == fractionDigits - trailingZeros;
    }

    private static string Identifier(JsonElement element)
    {
        var value = String(element);
        if (!AssetNamingRules.IsValidMachineIdentifier(value))
            throw new InvalidDataException("Manifest identifiers and classification values must follow Naming v1.");
        return value;
    }

    private static string String(JsonElement element)
    {
        RequireKind(element, JsonValueKind.String);
        return element.GetString()!;
    }

    private static void RequireKind(JsonElement element, JsonValueKind kind)
    {
        if (element.ValueKind != kind)
            throw new JsonException("A manifest value has the wrong JSON type.");
    }
}

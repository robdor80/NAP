using System.Text.Json;

namespace NAP.Core;

/// <summary>Loads profile configuration v1 or v2 explicitly. Leaves caller-owned streams open.</summary>
public static class UniverseProfileLoader
{
    public static UniverseProfile Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Load(stream);
    }

    public static UniverseProfile Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
            throw new ArgumentException("The configuration stream must be readable.", nameof(stream));
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        RequireKind(root, JsonValueKind.Object);
        if (!root.TryGetProperty("schema_version", out var version))
            throw new JsonException("The universe profile schema version is missing.");
        if (version.ValueKind != JsonValueKind.Number || !version.TryGetDecimal(out var number) || number is not (1 or 2))
            throw new InvalidDataException("Unsupported universe profile schema version.");
        RequireObject(root, "schema_version", "universe_id", "display_name", "classification_dimensions", "asset_rules");

        var id = new UniverseId(String(root.GetProperty("universe_id")));
        var displayName = String(root.GetProperty("display_name"));
        var dimensions = Strings(root.GetProperty("classification_dimensions"));
        var ruleArray = root.GetProperty("asset_rules");
        RequireKind(ruleArray, JsonValueKind.Array);
        var rules = new List<UniverseAssetRule>();
        foreach (var rule in ruleArray.EnumerateArray())
        {
            IReadOnlyList<AssetPackageFileRule> packageFiles;
            switch (number)
            {
                case 1:
                    RequireObject(rule, "asset_type", "production_profile", "allowed_classification", "required_classification");
                    packageFiles = Array.Empty<AssetPackageFileRule>();
                    break;
                case 2:
                    RequireObject(rule, "asset_type", "production_profile", "allowed_classification", "required_classification", "package_files");
                    packageFiles = PackageFiles(rule.GetProperty("package_files"));
                    break;
                default:
                    throw new InvalidDataException("Unsupported universe profile schema version.");
            }
            rules.Add(new UniverseAssetRule(String(rule.GetProperty("asset_type")),
                String(rule.GetProperty("production_profile")),
                Strings(rule.GetProperty("allowed_classification")),
                Strings(rule.GetProperty("required_classification")), packageFiles));
        }
        return new UniverseProfile(id, displayName, dimensions, rules);
    }

    private static IReadOnlyList<AssetPackageFileRule> PackageFiles(JsonElement element)
    {
        RequireKind(element, JsonValueKind.Array);
        var files = new List<AssetPackageFileRule>();
        foreach (var file in element.EnumerateArray())
        {
            RequireObject(file, "role", "suffix", "extension", "required", "content_validator");
            var required = file.GetProperty("required");
            if (required.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new JsonException("A package file required flag must be boolean.");
            var validator = file.GetProperty("content_validator");
            files.Add(new AssetPackageFileRule(String(file.GetProperty("role")),
                String(file.GetProperty("suffix")), String(file.GetProperty("extension")), required.GetBoolean(),
                validator.ValueKind == JsonValueKind.Null ? null : String(validator)));
        }
        return files;
    }

    private static void RequireObject(JsonElement element, params string[] propertyNames)
    {
        RequireKind(element, JsonValueKind.Object);
        var expected = new HashSet<string>(propertyNames, StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!expected.Remove(property.Name))
                throw new JsonException("Unknown or duplicate universe profile property.");
        }
        if (expected.Count != 0)
            throw new JsonException("A required universe profile property is missing.");
    }

    private static string String(JsonElement element)
    {
        RequireKind(element, JsonValueKind.String);
        return element.GetString()!;
    }

    private static string[] Strings(JsonElement element)
    {
        RequireKind(element, JsonValueKind.Array);
        return element.EnumerateArray().Select(String).ToArray();
    }

    private static void RequireKind(JsonElement element, JsonValueKind kind)
    {
        if (element.ValueKind != kind)
            throw new JsonException("A universe profile value has the wrong JSON type.");
    }
}

using System.Text.Json;

namespace NAP.Core;

/// <summary>Loads profile configuration v1 explicitly. Leaves caller-owned streams open.</summary>
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
        RequireObject(root, "schema_version", "universe_id", "display_name", "classification_dimensions", "asset_rules");
        var version = root.GetProperty("schema_version");
        if (version.ValueKind != JsonValueKind.Number || !version.TryGetDecimal(out var number) || number != 1)
            throw new InvalidDataException("Unsupported universe profile schema version.");

        var id = new UniverseId(String(root.GetProperty("universe_id")));
        var displayName = String(root.GetProperty("display_name"));
        var dimensions = Strings(root.GetProperty("classification_dimensions"));
        var ruleArray = root.GetProperty("asset_rules");
        RequireKind(ruleArray, JsonValueKind.Array);
        var rules = new List<UniverseAssetRule>();
        foreach (var rule in ruleArray.EnumerateArray())
        {
            RequireObject(rule, "asset_type", "production_profile", "allowed_classification", "required_classification");
            rules.Add(new UniverseAssetRule(String(rule.GetProperty("asset_type")),
                String(rule.GetProperty("production_profile")),
                Strings(rule.GetProperty("allowed_classification")),
                Strings(rule.GetProperty("required_classification"))));
        }
        return new UniverseProfile(id, displayName, dimensions, rules);
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

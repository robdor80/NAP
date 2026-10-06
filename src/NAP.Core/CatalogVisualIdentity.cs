using System.Text.Json;

namespace NAP.Core;

/// <summary>Lossless scalar extraction. Arrays use index paths; null/empty containers have no trait.</summary>
public static class CatalogVisualIdentity
{
    public static IReadOnlyList<CatalogVisualTrait> Extract(ReadOnlyMemory<byte> originalJson)
    {
        try
        {
            using var document = JsonDocument.Parse(originalJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new FormatException("Visual identity must be an object.");
            var traits = new List<CatalogVisualTrait>(); Visit(document.RootElement, "");
            return Array.AsReadOnly(traits.OrderBy(t => t.Key, StringComparer.Ordinal).ToArray());
            void Visit(JsonElement value, string path)
            {
                switch (value.ValueKind)
                {
                    case JsonValueKind.Object:
                        var keys = new HashSet<string>(StringComparer.Ordinal);
                        foreach (var property in value.EnumerateObject())
                        {
                            if (!keys.Add(property.Name)) throw new FormatException("Duplicate JSON key is ambiguous.");
                            Visit(property.Value, path + "/" + property.Name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal));
                        }
                        break;
                    case JsonValueKind.Array:
                        var index = 0; foreach (var item in value.EnumerateArray()) Visit(item, path + "/" + (index++).ToString(System.Globalization.CultureInfo.InvariantCulture));
                        break;
                    case JsonValueKind.String: traits.Add(new(path, value.GetString()!, CatalogTraitType.String)); break;
                    case JsonValueKind.Number: traits.Add(new(path, value.GetRawText(), CatalogTraitType.Number)); break;
                    case JsonValueKind.True: case JsonValueKind.False: traits.Add(new(path, value.GetRawText(), CatalogTraitType.Boolean)); break;
                    case JsonValueKind.Null: break;
                    default: throw new FormatException("Unsupported JSON value.");
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        { throw CatalogException.Stop(NapIssueCodes.CatalogSourceInvalid, "Visual identity cannot be interpreted unambiguously.", inner: ex); }
    }
}

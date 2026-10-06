using System.Text;
using System.Text.Json;

namespace NAP.Core;

/// <summary>Explicit deterministic JSON serialization of allowlisted facts, not domain objects.</summary>
public sealed class AiAuditRequestJsonRenderer
{
    public string Render(AiAuditRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", 1);
            writer.WriteString("universe_id", request.AssetKey.UniverseId.Value);
            writer.WriteString("asset_id", request.AssetKey.AssetId);
            writer.WriteString("asset_type", request.AssetType);
            writer.WriteString("production_profile", request.ProductionProfile);
            writer.WriteStartArray("classification");
            foreach (var pair in request.Classification.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("key", pair.Key);
                writer.WriteString("value", pair.Value);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("input_roles");
            foreach (var role in request.InputRoles.OrderBy(role => role, StringComparer.Ordinal)) writer.WriteStringValue(role);
            writer.WriteEndArray();
            writer.WriteString("destination_relative_directory", request.DestinationRelativeDirectory);
            writer.WriteStartArray("validation_issues");
            foreach (var issue in request.ValidationIssues)
            {
                writer.WriteStartObject();
                writer.WriteString("code", issue.Code);
                writer.WriteString("severity", issue.Severity switch
                {
                    NapIssueSeverity.Info => "Info", NapIssueSeverity.Warning => "Warning", NapIssueSeverity.Error => "Error",
                    _ => throw new ArgumentOutOfRangeException(nameof(request))
                });
                writer.WriteString("disposition", issue.Disposition switch
                {
                    NapIssueDisposition.Continue => "Continue", NapIssueDisposition.Stop => "Stop",
                    _ => throw new ArgumentOutOfRangeException(nameof(request))
                });
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
        }
        return Encoding.UTF8.GetString(output.ToArray());
    }
}

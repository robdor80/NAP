using System.Text.Json;

namespace NAP.Core;

/// <summary>Strict provider-neutral report v1 parsing; never extracts, repairs or normalizes text.</summary>
public sealed class AiAuditReportJsonParser
{
    public AiAuditReport Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            ExactProperties(root, "schema_version", "decision", "summary", "findings");
            var version = root.GetProperty("schema_version");
            if (version.ValueKind != JsonValueKind.Number || version.GetRawText() != "1")
                throw Invalid();
            var decision = ReadString(root, "decision") switch
            {
                "PASS" => AiAuditDecision.Pass, "WARNING" => AiAuditDecision.Warning, "FAIL" => AiAuditDecision.Fail,
                _ => throw Invalid()
            };
            var summary = ReadString(root, "summary");
            var array = root.GetProperty("findings");
            if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > 20)
                throw Invalid();
            var findings = new List<AiAuditFinding>();
            foreach (var item in array.EnumerateArray())
            {
                ExactProperties(item, "code", "severity", "message");
                var severity = ReadString(item, "severity") switch
                {
                    "WARNING" => AiAuditFindingSeverity.Warning, "ERROR" => AiAuditFindingSeverity.Error,
                    _ => throw Invalid()
                };
                findings.Add(new AiAuditFinding(ReadString(item, "code"), severity, ReadString(item, "message")));
            }
            return new AiAuditReport(decision, summary, findings);
        }
        catch (JsonException) { throw Invalid(); }
        catch (InvalidOperationException) { throw Invalid(); }
        catch (ArgumentException) { throw Invalid(); }
    }

    private static void ExactProperties(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid();
        var remaining = new HashSet<string>(names, StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!remaining.Remove(property.Name)) throw Invalid();
        if (remaining.Count != 0) throw Invalid();
    }

    private static string ReadString(JsonElement value, string property)
    {
        var item = value.GetProperty(property);
        if (item.ValueKind != JsonValueKind.String) throw Invalid();
        return item.GetString()!;
    }

    private static InvalidDataException Invalid() => new("The AI audit report must satisfy the exact JSON v1 contract and decision invariants.");
}

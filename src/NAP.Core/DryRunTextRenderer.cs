using System.Globalization;
using System.Text;

namespace NAP.Core;

/// <summary>Returns a deterministic human preview of a plan; performs no I/O or operational validation.</summary>
public sealed class DryRunTextRenderer
{
    public string Render(ProcessingPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var lines = new List<string>
        {
            "NAP DRY RUN",
            "ProcessingPlan v1 - read-only preview",
            "",
            "ASSET",
            $"  universe_id: {plan.AssetKey.UniverseId.Value}",
            $"  asset_id: {plan.AssetKey.AssetId}",
            $"  asset_type: {plan.AssetType}",
            $"  production_profile: {plan.ProductionProfile}",
            "",
            "CLASSIFICATION"
        };
        if (plan.Classification.Count == 0) lines.Add("  (none)");
        else
            foreach (var entry in plan.Classification.OrderBy(entry => entry.Key, StringComparer.Ordinal))
                lines.Add($"  {entry.Key}: {entry.Value}");

        lines.AddRange([
            "",
            "PACKAGE",
            $"  package_root: {QuotePath(plan.PackageRoot)}",
            $"  manifest_path: {QuotePath(plan.ManifestPath)}",
            "",
            "INPUT FILES"
        ]);
        if (plan.FilesByRole.Count == 0) lines.Add("  (none)");
        else
            foreach (var entry in plan.FilesByRole.OrderBy(entry => entry.Key, StringComparer.Ordinal))
                lines.Add($"  {entry.Key}: {QuotePath(entry.Value)}");

        var destination = plan.ProductionDestination;
        lines.AddRange([
            "",
            "PRODUCTION DESTINATION",
            $"  production_root: {QuotePath(destination.RootPath)}",
            $"  relative_directory: {destination.RelativeDirectory}",
            $"  full_directory_path: {QuotePath(destination.FullDirectoryPath)}",
            "",
            "OPERATIONS",
            "  (not defined in ProcessingPlan v1)",
            "",
            "SAFETY",
            "  This renderer performs no filesystem I/O.",
            "  No operation is executed or authorized by this dry run."
        ]);
        return string.Join("\n", lines);
    }

    private static string QuotePath(string path)
    {
        var quoted = new StringBuilder("\"");
        foreach (var character in path)
        {
            switch (character)
            {
                case '\\': quoted.Append("\\\\"); break;
                case '"': quoted.Append("\\\""); break;
                case '\r': quoted.Append("\\r"); break;
                case '\n': quoted.Append("\\n"); break;
                case '\t': quoted.Append("\\t"); break;
                default:
                    if (char.IsControl(character))
                        quoted.Append("\\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
                    else quoted.Append(character);
                    break;
            }
        }
        return quoted.Append('"').ToString();
    }
}

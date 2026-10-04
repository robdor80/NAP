using System.Globalization;

namespace NAP.Core;

/// <summary>Projects planning facts into a deterministic privacy-safe summary without I/O.</summary>
public sealed class PlanLogTextRenderer
{
    public string Render(ProcessingPlan plan, NapIssueReport validationReport)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(validationReport);
        var lines = new List<string>
        {
            "NAP PLAN LOG",
            "Planning summary v1",
            "",
            "ASSET",
            $"  universe_id: {plan.AssetKey.UniverseId.Value}",
            $"  asset_id: {plan.AssetKey.AssetId}",
            $"  asset_type: {plan.AssetType}",
            $"  production_profile: {plan.ProductionProfile}",
            "",
            "DESTINATION",
            $"  relative_directory: {plan.ProductionDestination.RelativeDirectory}",
            "",
            "VALIDATION",
            $"  is_clean: {(validationReport.IsClean ? "true" : "false")}",
            $"  should_stop: {(validationReport.ShouldStop ? "true" : "false")}",
            $"  can_continue: {(validationReport.CanContinue ? "true" : "false")}",
            $"  issue_count: {validationReport.Issues.Count.ToString(CultureInfo.InvariantCulture)}",
            "",
            "ISSUES"
        };
        if (validationReport.Issues.Count == 0) lines.Add("  (none)");
        else
            for (var index = 0; index < validationReport.Issues.Count; index++)
            {
                var issue = validationReport.Issues[index];
                lines.AddRange([
                    $"  [{index.ToString(CultureInfo.InvariantCulture)}]",
                    $"    code: {issue.Code}",
                    $"    severity: {issue.Severity}",
                    $"    disposition: {issue.Disposition}"
                ]);
            }
        lines.AddRange([
            "",
            "SAFETY",
            "  Absolute paths and source file paths are intentionally omitted.",
            "  This log does not authorize execution."
        ]);
        return string.Join("\n", lines);
    }
}

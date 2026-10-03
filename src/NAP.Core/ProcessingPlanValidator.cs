namespace NAP.Core;

/// <summary>Interprets destination prefixes against one materialized snapshot; performs no filesystem I/O.</summary>
public sealed class ProcessingPlanValidator
{
    public NapIssueReport Validate(
        ProcessingPlan plan,
        ProductionRepositorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (plan.AssetKey.UniverseId != snapshot.UniverseId)
            throw new ArgumentException("The plan and repository snapshot must belong to the same universe.", nameof(snapshot));

        var windows = OperatingSystem.IsWindows();
        var comparison = windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(plan.ProductionDestination.RootPath));
        var snapshotRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(snapshot.RootPath));
        if (!string.Equals(root, snapshotRoot, comparison))
            throw new ArgumentException("The snapshot must represent the plan's production root.", nameof(snapshot));

        var entries = new Dictionary<string, ProductionRepositoryEntry>(windows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var entry in snapshot.Entries)
            if (!entries.TryAdd(entry.RelativePath, entry))
                throw new ArgumentException("Snapshot paths must be unique under platform path semantics.", nameof(snapshot));

        var segments = plan.ProductionDestination.RelativeDirectory.Split('/');
        var prefix = "";
        for (var index = 0; index < segments.Length; index++)
        {
            prefix = index == 0 ? segments[index] : prefix + "/" + segments[index];
            if (!entries.TryGetValue(prefix, out var entry)) continue;
            if (!string.Equals(entry.RelativePath, prefix, StringComparison.Ordinal))
                return Stop(NapIssueCodes.PlanDestinationCasingConflict,
                    "An existing production path differs from the canonical destination only by casing.",
                    entry, $"expected: {prefix}; observed: {entry.RelativePath}");
            if (entry.Kind == ProductionRepositoryEntryKind.File)
                return Stop(NapIssueCodes.PlanDestinationBlocked,
                    "A file blocks a required production directory path.", entry, prefix);
            if (index == segments.Length - 1)
                return Stop(NapIssueCodes.PlanDestinationExists,
                    "The planned production destination already exists.", entry, plan.ProductionDestination.RelativeDirectory);
        }
        return new NapIssueReport([]);
    }

    private static NapIssueReport Stop(string code, string message, ProductionRepositoryEntry entry, string detail) =>
        new([new NapIssue(code, NapIssueSeverity.Error, NapIssueDisposition.Stop, message, entry.FullPath, detail)]);
}

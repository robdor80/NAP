namespace NAP.Core;

/// <summary>Detects lexical root overlap between distinct universes. Does not resolve filesystem aliases.</summary>
public static class UniverseStorageIsolationValidator
{
    public static NapIssueReport Validate(IEnumerable<UniverseStorageConfig> configurations)
    {
        ArgumentNullException.ThrowIfNull(configurations);
        var configs = configurations.ToArray();
        var ids = new HashSet<UniverseId>();
        foreach (var config in configs)
        {
            if (config is null)
                throw new ArgumentException("Storage configurations cannot contain null.", nameof(configurations));
            if (!ids.Add(config.UniverseId))
                throw new ArgumentException("Only one storage configuration per universe is allowed.", nameof(configurations));
        }

        var issues = new List<NapIssue>();
        for (var first = 0; first < configs.Length; first++)
        {
            for (var second = first + 1; second < configs.Length; second++)
            {
                foreach (var firstRoot in Roots(configs[first]))
                foreach (var secondRoot in Roots(configs[second]))
                {
                    if (Overlaps(firstRoot.Path, secondRoot.Path))
                    {
                        issues.Add(new NapIssue(NapIssueCodes.UniverseStorageOverlap,
                            NapIssueSeverity.Error, NapIssueDisposition.Stop,
                            "Storage roots of different universes overlap.", firstRoot.Path,
                            $"{configs[first].UniverseId} {firstRoot.Name}: {firstRoot.Path}; " +
                            $"{configs[second].UniverseId} {secondRoot.Name}: {secondRoot.Path}"));
                    }
                }
            }
        }
        return new NapIssueReport(issues);
    }

    private static (string Name, string Path)[] Roots(UniverseStorageConfig config) =>
    [
        (nameof(config.WorkspaceRoot), config.WorkspaceRoot),
        (nameof(config.ProductionRoot), config.ProductionRoot),
        (nameof(config.ArchiveRoot), config.ArchiveRoot)
    ];

    private static bool Overlaps(string first, string second)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        first = Path.TrimEndingDirectorySeparator(first);
        second = Path.TrimEndingDirectorySeparator(second);
        return string.Equals(first, second, comparison) ||
            IsDescendant(first, second, comparison) || IsDescendant(second, first, comparison);
    }

    private static bool IsDescendant(string parent, string child, StringComparison comparison)
    {
        // Preserve volume-root separators (C:\, /) and require a whole path segment boundary.
        var prefix = Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar;
        return child.StartsWith(prefix, comparison);
    }
}

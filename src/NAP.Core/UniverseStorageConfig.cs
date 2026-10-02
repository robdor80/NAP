namespace NAP.Core;

/// <summary>Resolved roots authorized by the caller for one universe. Performs no filesystem I/O.</summary>
public sealed record UniverseStorageConfig
{
    public UniverseStorageConfig(UniverseId universeId, string workspaceRoot,
        string productionRoot, string archiveRoot)
    {
        ArgumentNullException.ThrowIfNull(universeId);
        UniverseId = universeId;
        WorkspaceRoot = ValidateRoot(workspaceRoot, nameof(workspaceRoot));
        ProductionRoot = ValidateRoot(productionRoot, nameof(productionRoot));
        ArchiveRoot = ValidateRoot(archiveRoot, nameof(archiveRoot));
    }

    public UniverseId UniverseId { get; }
    public string WorkspaceRoot { get; }
    public string ProductionRoot { get; }
    public string ArchiveRoot { get; }
    public string InboxRoot => Path.Combine(WorkspaceRoot, "inbox");
    public string StagingRoot => Path.Combine(WorkspaceRoot, "staging");
    public string StateRoot => Path.Combine(WorkspaceRoot, "state");
    public string CacheRoot => Path.Combine(WorkspaceRoot, "cache");
    public string CatalogPath => Path.Combine(StateRoot, "AssetCatalog.db");

    private static string ValidateRoot(string root, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root, parameterName);
        if (!Path.IsPathFullyQualified(root))
        {
            throw new ArgumentException("A storage root must be fully qualified.", parameterName);
        }
        return Path.GetFullPath(root);
    }
}

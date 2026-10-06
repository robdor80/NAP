namespace NAP.Core;

/// <summary>Read-only boundary for the caller-authorized local archive root. Never creates it.</summary>
public sealed class ArchiveRootValidator
{
    public NapIssueReport Validate(UniverseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var root = context.Storage.ArchiveRoot;
        try
        {
            if (!Path.IsPathFullyQualified(root) || context.Id != context.Storage.UniverseId ||
                ArchivePaths.Overlaps(root, context.Storage.ProductionRoot) || ArchivePaths.Overlaps(root, context.Storage.WorkspaceRoot))
                throw ArchiveStorageException.Stop(NapIssueCodes.ArchiveRootInvalid, "ArchiveRoot must be absolute, universe scoped and isolated from production and workspace.", root);
            var attributes = ArchivePaths.CheckPath(root, NapIssueCodes.ArchiveRootInvalid, NapIssueCodes.ArchiveRootReparse);
            if (attributes is null)
                throw ArchiveStorageException.Stop(NapIssueCodes.ArchiveRootMissing, "The configured archive root does not exist.", root);
            if ((attributes & FileAttributes.Directory) == 0)
                throw ArchiveStorageException.Stop(NapIssueCodes.ArchiveRootInvalid, "The configured archive root must be a directory.", root);
            return new NapIssueReport([]);
        }
        catch (ArchiveStorageException ex) { return ex.Issues; }
    }

    internal static void Require(UniverseContext context)
    {
        var issues = new ArchiveRootValidator().Validate(context);
        if (issues.ShouldStop) throw new ArchiveStorageException(issues);
    }
}

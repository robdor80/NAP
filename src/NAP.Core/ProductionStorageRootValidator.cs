namespace NAP.Core;

/// <summary>The write boundary is stronger than the historical read-only production repository validator.</summary>
public sealed class ProductionStorageRootValidator
{
    public NapIssueReport Validate(UniverseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var root = context.Storage.ProductionRoot;
        try
        {
            if (!Path.IsPathFullyQualified(root) || context.Id != context.Storage.UniverseId ||
                root.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
                    .Any(segment => segment.Equals(".git", StringComparison.OrdinalIgnoreCase)) ||
                ProductionPaths.Overlaps(root, context.Storage.WorkspaceRoot) || ProductionPaths.Overlaps(root, context.Storage.ArchiveRoot))
                throw ProductionStorageException.Stop(NapIssueCodes.ProductionRootInvalid, "ProductionRoot must be absolute, universe scoped, outside Git metadata and isolated from workspace and archive.", root);
            var attributes = ProductionPaths.CheckPath(root, NapIssueCodes.ProductionRootInvalid, NapIssueCodes.ProductionRootReparse);
            if (attributes is null) throw ProductionStorageException.Stop(NapIssueCodes.ProductionRootMissing, "The authorized production root does not exist.", root);
            if ((attributes & FileAttributes.Directory) == 0) throw ProductionStorageException.Stop(NapIssueCodes.ProductionRootInvalid, "ProductionRoot must be a directory.", root);
            return new NapIssueReport([]);
        }
        catch (ProductionStorageException ex) { return ex.Issues; }
    }

    internal static void Require(UniverseContext context)
    {
        var issues = new ProductionStorageRootValidator().Validate(context);
        if (issues.ShouldStop) throw new ProductionStorageException(issues);
    }
}

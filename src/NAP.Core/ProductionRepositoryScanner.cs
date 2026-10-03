namespace NAP.Core;

/// <summary>Observes filesystem structure without reading file contents, interpreting names or following reparse entries.</summary>
public sealed class ProductionRepositoryScanner
{
    public ProductionRepositoryScanResult Scan(ValidatedProductionRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        var rootIssue = RevalidateRoot(repository.RootPath);
        if (rootIssue is not null)
            return new ProductionRepositoryScanResult(new NapIssueReport([rootIssue]), null);

        var entries = new List<ProductionRepositoryEntry>();
        var reparseIssues = new List<NapIssue>();
        var pending = new Stack<(string FullPath, string RelativePath)>();
        pending.Push((repository.RootPath, ""));
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            // A discovered directory may have changed before it was visited. Never descend into a detected link.
            if (directory.RelativePath.Length > 0 &&
                (File.GetAttributes(directory.FullPath) & FileAttributes.ReparsePoint) != 0)
            {
                reparseIssues.Add(ReparseIssue(directory.FullPath, directory.RelativePath));
                continue;
            }

            foreach (var entry in new DirectoryInfo(directory.FullPath).EnumerateFileSystemInfos())
            {
                var relativePath = directory.RelativePath.Length == 0
                    ? entry.Name : directory.RelativePath + "/" + entry.Name;
                var attributes = File.GetAttributes(entry.FullName);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    reparseIssues.Add(ReparseIssue(entry.FullName, relativePath));
                    continue;
                }

                var kind = (attributes & FileAttributes.Directory) != 0
                    ? ProductionRepositoryEntryKind.Directory : ProductionRepositoryEntryKind.File;
                entries.Add(new ProductionRepositoryEntry(relativePath, entry.FullName, kind));
                if (kind == ProductionRepositoryEntryKind.Directory)
                    pending.Push((entry.FullName, relativePath));
            }
        }

        if (reparseIssues.Count > 0)
            return new ProductionRepositoryScanResult(
                new NapIssueReport(reparseIssues.OrderBy(issue => issue.Detail, StringComparer.Ordinal)), null);

        return new ProductionRepositoryScanResult(new NapIssueReport([]), new ProductionRepositorySnapshot(repository, entries));
    }

    private static NapIssue? RevalidateRoot(string root)
    {
        FileAttributes attributes;
        try { attributes = File.GetAttributes(root); }
        catch (FileNotFoundException) { return RootIssue(NapIssueCodes.ProductionRootMissing, "The production root does not exist.", root); }
        catch (DirectoryNotFoundException) { return RootIssue(NapIssueCodes.ProductionRootMissing, "The production root does not exist.", root); }
        if ((attributes & FileAttributes.Directory) == 0)
            return RootIssue(NapIssueCodes.ProductionRootInvalid, "The production root must be a directory.", root);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            return RootIssue(NapIssueCodes.ProductionRootReparse, "The production root must not be a reparse point.", root);
        return null;
    }

    private static NapIssue RootIssue(string code, string message, string root) =>
        new(code, NapIssueSeverity.Error, NapIssueDisposition.Stop, message, root);

    private static NapIssue ReparseIssue(string path, string relativePath) =>
        new(NapIssueCodes.RepositoryEntryReparse, NapIssueSeverity.Error, NapIssueDisposition.Stop,
            "A repository entry must not be a reparse point.", path, relativePath);
}

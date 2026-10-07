namespace NAP.Core;

/// <summary>Physical working tree snapshot, including ignored/untracked bytes and empty directories; only root .git is excluded.</summary>
public sealed class RepositorySnapshotService
{
    internal TimeProvider Clock { get; set; } = TimeProvider.System;
    internal Func<BackupId> NewId { get; set; } = BackupId.New;
    internal Action<string>? Observer { get; set; }
    public BackupHistoryEntry Create(UniverseContext context) => BackupStorage.Run(() =>
    {
        using var production = BackupStorage.Production(context);
        var source = context.Storage.ProductionRoot; var before = BackupTree.Capture(source, true);
        Observer?.Invoke("source_captured");
        using var archive = BackupStorage.Archive(context, true);
        var id = NewId(); var created = Clock.GetUtcNow().ToUniversalTime();
        var temp = BackupStorage.Prepare(context, BackupKind.RepositorySnapshot, id); var treeRoot = Path.Combine(temp, "tree");
        ArchivePaths.EnsureDirectory(context, treeRoot);
        foreach (var d in before.Directories) ArchivePaths.EnsureDirectory(context, BackupStorage.Resolve(treeRoot, d));
        foreach (var f in before.Files)
        {
            ProductionStorageRootValidator.Require(context);
            var input = BackupStorage.Resolve(source, f.RelativePath); var output = BackupStorage.Resolve(treeRoot, f.RelativePath);
            BackupStorage.VerifyFile(input, f.Size, f.Sha256); BackupStorage.Copy(input, output); BackupStorage.VerifyFile(output, f.Size, f.Sha256);
        }
        Observer?.Invoke("copied"); var actual = BackupTree.Capture(treeRoot, false);
        ProductionStorageRootValidator.Require(context); var after = BackupTree.Capture(source, true);
        if (before.Sha256 != after.Sha256 || before.Sha256 != actual.Sha256)
            throw BackupException.Stop(NapIssueCodes.RepositorySnapshotChanged, "Working tree changed during snapshot; no final backup was published.");
        var manifest = new BackupManifest(id, context.Id, BackupKind.RepositorySnapshot, created, before.Size, before.Sha256, files: before.Files, directories: before.Directories);
        return BackupStorage.Publish(context, temp, manifest, archive);
    });
}

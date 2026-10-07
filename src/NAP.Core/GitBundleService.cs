namespace NAP.Core;

public sealed class GitBundleService
{
    internal TimeProvider Clock { get; set; } = TimeProvider.System;
    internal Func<BackupId> NewId { get; set; } = BackupId.New;
    internal GitBackupProcess Git { get; set; } = new();
    internal Action<string>? Observer { get; set; }
    public BackupHistoryEntry Create(UniverseContext context) => BackupStorage.Run(() =>
    {
        using var production = BackupStorage.Production(context);
        var root = Path.TrimEndingDirectorySeparator(context.Storage.ProductionRoot); var metadata = Path.Combine(root, ".git");
        // Reject external configuration before even invoking read-only discovery in a standalone root.
        if (BackupStorage.Check(metadata) is { } meta && (meta & FileAttributes.Directory) != 0)
        {
            var localConfig = Path.Combine(metadata, "config"); BackupStorage.Fingerprint(localConfig);
            if (File.ReadLines(localConfig).Any(line => System.Text.RegularExpressions.Regex.IsMatch(line, @"^\s*\[\s*include", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant)))
                throw BackupException.Stop(NapIssueCodes.GitRepositoryInvalid, "External Git configuration includes require explicit review before offline backup.");
        }
        string detected;
        try { detected = Git.Root(context); }
        catch (BackupException e) when (e.Issues.Issues.Any(i => i.Code == NapIssueCodes.GitBundleFailed))
        { throw BackupException.Stop(NapIssueCodes.GitRepositoryInvalid, "ProductionRoot is not a readable local Git repository."); }
        if (!string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(detected)), root, StringComparison.Ordinal))
            throw BackupException.Stop(NapIssueCodes.GitRepositoryInvalid, "ProductionRoot must be the exact Git repository root.");
        // Standalone repositories only: linked worktrees, gitdir files and alternates could reach another root.
        if (BackupStorage.Check(metadata) is not { } a || (a & FileAttributes.Directory) == 0)
            throw BackupException.Stop(NapIssueCodes.GitRepositoryInvalid, "Linked worktrees and gitdir files require a separate backup policy.");
        var originalMetadata = BackupTree.Capture(metadata, false);
        if (originalMetadata.Files.Any(f => f.RelativePath is "objects/info/alternates" or "objects/info/http-alternates" || f.RelativePath.EndsWith(".promisor", StringComparison.Ordinal)))
            throw BackupException.Stop(NapIssueCodes.GitRepositoryInvalid, "Linked object stores and partial clones are not supported for offline backups.");
        if (!string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Git.Root(context))), root, StringComparison.Ordinal) ||
            !ArchivePaths.Same(Git.GitDirectory(context), metadata))
            throw BackupException.Stop(NapIssueCodes.GitRepositoryInvalid, "ProductionRoot must be the exact standalone Git repository root.");
        var tree = BackupTree.Capture(root, true); var head = Git.Head(context); var dirty = Git.Dirty(context); var refs = Git.Refs(context);
        using var archive = BackupStorage.Archive(context, true);
        var id = NewId(); var created = Clock.GetUtcNow().ToUniversalTime(); var temp = BackupStorage.Prepare(context, BackupKind.GitBundle, id);
        var bundle = Path.Combine(temp, "history.bundle");
        Git.Create(context, bundle); Git.Verify(context, bundle); BackupStorage.Durable(bundle);
        var f = BackupStorage.Fingerprint(bundle);
        Observer?.Invoke("bundle_verified"); ProductionStorageRootValidator.Require(context);
        if (head != Git.Head(context) || refs != Git.Refs(context) || dirty != Git.Dirty(context) || tree.Sha256 != BackupTree.Capture(root, true).Sha256 ||
            originalMetadata.Sha256 != BackupTree.Capture(metadata, false).Sha256)
            throw BackupException.Stop(NapIssueCodes.BackupChanged, "Git state or working tree changed during bundle creation.");
        BackupStorage.VerifyFile(bundle, f.Size, f.Sha);
        return BackupStorage.Publish(context, temp, new BackupManifest(id, context.Id, BackupKind.GitBundle, created, f.Size, f.Sha, head: head, dirty: dirty), archive);
    });
}

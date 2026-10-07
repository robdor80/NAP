namespace NAP.Core;

internal sealed record BackupTree(IReadOnlyList<BackupFile> Files, IReadOnlyList<string> Directories)
{
    internal long Size => Files.Sum(f => f.Size);
    internal Sha256Digest Sha256 => BackupManifestCodec.TreeHash(Files, Directories);
    internal static BackupTree Capture(string root, bool excludeGit)
    {
        BackupStorage.RequireDirectory(root); var files = new List<BackupFile>(); var directories = new List<string>();
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            BackupStorage.RequireDirectory(directory);
            foreach (var path in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
            {
                var name = Path.GetFileName(path);
                // Reject aliases even on the metadata root before excluding its contents.
                var attributes = BackupStorage.Check(path);
                if (excludeGit && ArchivePaths.Same(root, directory) && name == ".git") continue;
                if (excludeGit && ArchivePaths.Same(root, directory) && name.Equals(".git", StringComparison.OrdinalIgnoreCase))
                    throw BackupException.Stop(NapIssueCodes.BackupSourceInvalid, "Git metadata root has ambiguous casing.");
                var relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
                BackupStorage.Relative(relative); BackupStorage.Resolve(root, relative);
                if (attributes is null || (attributes & FileAttributes.Device) != 0)
                    throw BackupException.Stop(NapIssueCodes.BackupSourceInvalid, "Snapshot traversal encountered a missing or nonregular entry.");
                if ((attributes & FileAttributes.Directory) != 0) { directories.Add(relative); pending.Push(path); }
                else { var f = BackupStorage.Fingerprint(path); files.Add(new(relative, f.Size, f.Sha)); }
            }
        }
        return new(Array.AsReadOnly(files.OrderBy(f => f.RelativePath, StringComparer.Ordinal).ToArray()), Array.AsReadOnly(directories.Order(StringComparer.Ordinal).ToArray()));
    }
}

namespace NAP.Core;

/// <summary>Local read-only discovery. Invalid recognized backups STOP; unmanaged entries are reported and never adopted.</summary>
public sealed class BackupHistoryReader
{
    public BackupHistory Read(UniverseContext context) => BackupStorage.Run(() =>
    { using var lease = BackupStorage.Archive(context, false); return ReadLocked(context); });

    internal static BackupHistory ReadLocked(UniverseContext c)
    {
        BackupStorage.Context(c); var result = new List<BackupHistoryEntry>(); var issues = new List<NapIssue>();
        foreach (var kind in Enum.GetValues<BackupKind>())
        {
            var root = BackupStorage.Root(c, kind);
            ProductionPaths.CheckCasing(c.Storage.ArchiveRoot, BackupStorage.Namespace(kind));
            if (BackupStorage.Check(root) is null) continue;
            BackupStorage.RequireDirectory(root);
            foreach (var path in Directory.EnumerateFileSystemEntries(root).Order(StringComparer.Ordinal))
            {
                BackupStorage.Check(path); var name = Path.GetFileName(path); BackupId? id;
                if (kind == BackupKind.Database && name.Equals("_recovery", StringComparison.OrdinalIgnoreCase))
                {
                    if (name != "_recovery") throw BackupException.Stop(NapIssueCodes.BackupManifestInvalid, "Recovery namespace casing is ambiguous.");
                    BackupStorage.RequireDirectory(path); continue; // Forensic evidence is never an ordinary backup or retention candidate.
                }
                try { id = new BackupId(name); }
                catch (ArgumentException)
                { issues.Add(new(NapIssueCodes.BackupUnmanaged, NapIssueSeverity.Warning, NapIssueDisposition.Continue, "An unmanaged entry in a backup namespace was left untouched.")); continue; }
                result.Add(BackupStorage.Verify(c, path, kind, id));
            }
        }
        if (result.Select(e => e.BackupId).Distinct().Count() != result.Count)
            throw BackupException.Stop(NapIssueCodes.BackupManifestInvalid, "Backup identity is duplicated across namespaces.");
        return new(Array.AsReadOnly(result.OrderByDescending(e => e.CreatedUtc).ThenBy(e => e.BackupId.Value, StringComparer.Ordinal).ToArray()), new(issues));
    }
    internal static BackupHistoryEntry Find(UniverseContext c, BackupId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return ReadLocked(c).Entries.SingleOrDefault(e => e.BackupId == id)
            ?? throw BackupException.Stop(NapIssueCodes.RestoreInvalid, "The selected backup identity is not locally recognized.");
    }
    internal static Sha256Digest Inventory(UniverseContext c)
    {
        // Includes unmanaged entries and exact manifest bytes. Traversal never leaves the three namespaces.
        var files = new List<BackupFile>(); var dirs = new List<string>();
        foreach (var kind in Enum.GetValues<BackupKind>())
        {
            var root = BackupStorage.Root(c, kind); if (BackupStorage.Check(root) is null) continue;
            var tree = BackupTree.Capture(root, false); var prefix = BackupStorage.Namespace(kind);
            dirs.Add(prefix); dirs.AddRange(tree.Directories.Select(d => prefix + "/" + d));
            files.AddRange(tree.Files.Select(f => f with { RelativePath = prefix + "/" + f.RelativePath }));
        }
        return BackupManifestCodec.TreeHash(files.OrderBy(f => f.RelativePath, StringComparer.Ordinal).ToArray(), dirs.Order(StringComparer.Ordinal).ToArray());
    }
}

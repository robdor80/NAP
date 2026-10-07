using Microsoft.Data.Sqlite;

namespace NAP.Core;

internal static class BackupStorage
{
    internal static string Namespace(BackupKind kind) => kind switch
    {
        BackupKind.Database => "NAP_DATABASE_BACKUPS", BackupKind.RepositorySnapshot => "NAP_REPOSITORY_SNAPSHOTS",
        BackupKind.GitBundle => "NAP_GIT_BUNDLES", _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    internal static string Root(UniverseContext c, BackupKind kind) => Path.Combine(c.Storage.ArchiveRoot, Namespace(kind));
    internal static string DirectoryPath(UniverseContext c, BackupKind kind, BackupId id) => Path.Combine(Root(c, kind), id.Value);
    internal static void Context(UniverseContext c)
    {
        ArgumentNullException.ThrowIfNull(c); ArchiveRootValidator.Require(c);
        if (ProductionPaths.Overlaps(c.Storage.WorkspaceRoot, c.Storage.ProductionRoot))
            throw BackupException.Stop(NapIssueCodes.BackupSourceInvalid, "Universe storage roots must be isolated.");
    }
    internal static void Relative(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains('\\') || path.Split('/').Any(p => !ArchivePaths.SafeSegment(p)))
            throw BackupException.Stop(NapIssueCodes.BackupManifestInvalid, "Backup paths must be safe portable relative paths.");
    }
    internal static string Resolve(string root, string path)
    {
        Relative(path); var full = path.Split('/').Aggregate(root, Path.Combine);
        if (!ArchivePaths.Within(root, full)) throw BackupException.Stop(NapIssueCodes.BackupManifestInvalid, "Backup path escapes its artifact.");
        return full;
    }
    internal static FileAttributes? Check(string path)
    {
        var attributes = ArchivePaths.CheckPath(path, NapIssueCodes.BackupDestinationInvalid, NapIssueCodes.BackupReparse);
        if (attributes is not null) BackupFileTypes.RequireOrdinary(path);
        return attributes;
    }
    internal static void RequireDirectory(string path)
    {
        var a = Check(path); if (a is null || (a & FileAttributes.Directory) == 0)
            throw BackupException.Stop(NapIssueCodes.BackupDestinationInvalid, "A controlled backup directory is missing or invalid.");
    }
    internal static (long Size, Sha256Digest Sha) Fingerprint(string path)
    {
        var a = Check(path); if (a is null || (a & (FileAttributes.Directory | FileAttributes.Device)) != 0)
            throw BackupException.Stop(NapIssueCodes.BackupIntegrityFailed, "An expected backup file is missing or not regular.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var size = stream.Length; var hash = new Sha256Hasher().Compute(stream);
        if (stream.Length != size) throw BackupException.Stop(NapIssueCodes.BackupChanged, "A file changed during fingerprinting.");
        return (size, hash);
    }
    internal static void VerifyFile(string path, long size, Sha256Digest hash)
    { if (Fingerprint(path) != (size, hash)) throw BackupException.Stop(NapIssueCodes.BackupIntegrityFailed, "Artifact size or SHA-256 differs from its manifest."); }
    internal static void Durable(string path)
    { Check(path); using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); stream.Flush(true); }
    internal static void Copy(string source, string destination)
    {
        Check(source); Check(destination);
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        input.CopyTo(output); output.Flush(true);
    }
    internal static ArchiveLock Archive(UniverseContext context, bool writes)
    {
        Context(context); ArchiveLock lease;
        try { lease = ArchiveLock.Acquire(context); }
        catch (IOException)
        { throw BackupException.Stop(NapIssueCodes.BackupBusy, "Archive is occupied; retry explicitly."); }
        try { if (writes) lease.EnableWrites(); return lease; }
        catch { lease.Dispose(); throw; }
    }
    internal static ExecutionMutex Production(UniverseContext c)
    {
        Context(c); ProductionStorageRootValidator.Require(c);
        try { return ExecutionMutex.Acquire("Production", c.Storage.ProductionRoot); }
        catch (IOException) { throw BackupException.Stop(NapIssueCodes.BackupBusy, "Production is occupied; retry explicitly."); }
    }
    internal static string Prepare(UniverseContext c, BackupKind kind, BackupId id)
    {
        Context(c); var root = Root(c, kind);
        ProductionPaths.CheckCasing(c.Storage.ArchiveRoot, Namespace(kind)); ArchivePaths.EnsureDirectory(c, root);
        Collision(c, kind, id);
        var temp = Path.Combine(root, ".pending-" + Guid.NewGuid().ToString("N"));
        if (Check(temp) is not null) throw BackupException.Stop(NapIssueCodes.BackupCollision, "Backup temporary collision.");
        Directory.CreateDirectory(temp); RequireDirectory(temp); return temp;
    }
    internal static void Collision(UniverseContext c, BackupKind kind, BackupId id)
    {
        var root = Root(c, kind); ProductionPaths.CheckCasing(root, id.Value);
        if (Check(DirectoryPath(c, kind, id)) is not null)
            throw BackupException.Stop(NapIssueCodes.BackupCollision, "Backup identity already exists; no overwrite is permitted.");
        // IDs are unique across kinds, not only within a namespace.
        foreach (var k in Enum.GetValues<BackupKind>())
            if (Check(DirectoryPath(c, k, id)) is not null) throw BackupException.Stop(NapIssueCodes.BackupCollision, "Backup identity already exists.");
    }
    internal static BackupHistoryEntry Publish(UniverseContext c, string temp, BackupManifest manifest, ArchiveLock lease)
    {
        lease.RequireWrites(c); Context(c);
        if (!ArchivePaths.Same(Path.GetDirectoryName(temp)!, Root(c, manifest.Kind)) || !Path.GetFileName(temp).StartsWith(".pending-", StringComparison.Ordinal))
            throw BackupException.Stop(NapIssueCodes.BackupDestinationInvalid, "Backup temporary is outside its controlled namespace.");
        var bytes = BackupManifestCodec.Write(manifest); var path = Path.Combine(temp, "manifest.json");
        using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(bytes); file.Flush(true); }
        var verified = Verify(c, temp, manifest.Kind, manifest.BackupId);
        Collision(c, manifest.Kind, manifest.BackupId); RequireDirectory(temp);
        Directory.Move(temp, DirectoryPath(c, manifest.Kind, manifest.BackupId)); return verified;
    }
    internal static BackupHistoryEntry Verify(UniverseContext c, string directory, BackupKind kind, BackupId id)
    {
        RequireDirectory(directory); var manifestPath = Path.Combine(directory, "manifest.json"); var fingerprint = Fingerprint(manifestPath);
        if (fingerprint.Size > 16 * 1024 * 1024) throw BackupException.Stop(NapIssueCodes.BackupManifestInvalid, "Backup manifest exceeds the supported size limit.");
        var bytes = File.ReadAllBytes(manifestPath);
        using (var stream = new MemoryStream(bytes))
            if (new Sha256Hasher().Compute(stream) != fingerprint.Sha) throw BackupException.Stop(NapIssueCodes.BackupChanged, "Manifest changed while reading.");
        var m = BackupManifestCodec.Read(bytes, c);
        if (m.BackupId != id || m.Kind != kind) throw BackupException.Stop(NapIssueCodes.BackupManifestInvalid, "Manifest identity does not match its controlled directory.");
        var entries = Directory.EnumerateFileSystemEntries(directory).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
        if (!entries.SequenceEqual(new[] { "manifest.json", m.Artifact }.Order(StringComparer.Ordinal)))
            throw BackupException.Stop(NapIssueCodes.BackupIntegrityFailed, "Backup contains missing or unmanaged entries.");
        var artifact = Path.Combine(directory, m.Artifact);
        if (kind == BackupKind.RepositorySnapshot)
        {
            var tree = BackupTree.Capture(artifact, excludeGit: false);
            if (tree.Sha256 != m.Sha256 || tree.Size != m.Size || !tree.Files.SequenceEqual(m.Files) || !tree.Directories.SequenceEqual(m.Directories))
                throw BackupException.Stop(NapIssueCodes.BackupIntegrityFailed, "Snapshot tree differs from its manifest.");
        }
        else
        {
            VerifyFile(artifact, m.Size, m.Sha256);
            if (kind == BackupKind.Database) ValidateDatabase(c, artifact);
            else GitBundleIntegrity.Verify(artifact, m.Head!);
        }
        VerifyFile(manifestPath, fingerprint.Size, fingerprint.Sha);
        return new(m, fingerprint.Sha);
    }
    internal static void ValidateDatabase(UniverseContext c, string path)
    {
        Fingerprint(path);
        foreach (var suffix in new[] { "-journal", "-wal", "-shm" })
            if (Check(path + suffix) is not null) throw BackupException.Stop(NapIssueCodes.BackupSourceInvalid, "SQLite sidecars require explicit recovery before backup or restore.");
        using var db = CatalogBoundary.Connect(path, SqliteOpenMode.ReadOnly);
        CatalogSchema.Validate(db, c.Id); CatalogAssetData.ValidateLogical(db, c);
        if (!string.Equals((string?)CatalogSql.Scalar(db, null, "PRAGMA journal_mode"), "delete", StringComparison.OrdinalIgnoreCase))
            throw BackupException.Stop(NapIssueCodes.BackupSourceInvalid, "Catalog backups require DELETE journal mode.");
    }
    internal static string Binding(UniverseContext c)
    {
        using var bytes = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(c.Id.Value + "\n" + c.Storage.WorkspaceRoot + "\n" + c.Storage.ProductionRoot + "\n" + c.Storage.ArchiveRoot));
        return new Sha256Hasher().Compute(bytes).Hex;
    }
    internal static T Run<T>(Func<T> action)
    {
        try { return action(); }
        catch (BackupException) { throw; }
        catch (CatalogException e)
        {
            var code = e.Issues.Issues[0].Code;
            throw BackupException.Stop(code switch { NapIssueCodes.CatalogBusy => NapIssueCodes.BackupBusy,
                NapIssueCodes.CatalogWrongUniverse => NapIssueCodes.BackupWrongUniverse, _ => code }, "Catalog validation or coordination rejected the backup operation.");
        }
        catch (ArchiveStorageException e)
        { throw BackupException.Stop(e.Issues.Issues[0].Code, "Archive validation rejected the backup operation."); }
        catch (ProductionStorageException e)
        { throw BackupException.Stop(e.Issues.Issues[0].Code, "Production path validation rejected the backup operation."); }
        catch (SqliteException e)
        { throw BackupException.Stop(e.SqliteErrorCode is 5 or 6 ? NapIssueCodes.BackupBusy : NapIssueCodes.BackupIntegrityFailed, "SQLite rejected the backup operation."); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or OverflowException)
        { throw BackupException.Stop(NapIssueCodes.BackupIoFailed, "A controlled backup operation failed; review local artifacts before retrying."); }
    }
}

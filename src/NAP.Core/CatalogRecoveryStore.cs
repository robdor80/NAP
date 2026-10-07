using System.Globalization;
using System.Text.Json;

namespace NAP.Core;

/// <summary>Forensic preservation only, under existing Archive/Catalog leases. No SQLite validation or ordinary retention.</summary>
internal sealed class CatalogRecoveryStore
{
    internal Func<CatalogRecoveryId> NewId { get; set; } = CatalogRecoveryId.New;
    internal Action<string>? Observer { get; set; }
    internal static string Root(UniverseContext c) => Path.Combine(BackupStorage.Root(c, BackupKind.Database), "_recovery");
    internal static string DirectoryPath(UniverseContext c, CatalogRecoveryId id) => Path.Combine(Root(c), id.Value);
    internal CatalogRecoveryArtifact Preserve(UniverseContext c, ArchiveLock archive, ExecutionMutex catalog, (long Size, Sha256Digest Sha) active)
    {
        archive.RequireWrites(c); catalog.Require("Catalog", c.Storage.StateRoot, "AssetCatalog.db");
        CatalogBoundary.Paths(c); BackupStorage.Context(c); BackupStorage.VerifyFile(c.Storage.CatalogPath, active.Size, active.Sha);
        var root = Root(c); var parent = BackupStorage.Root(c, BackupKind.Database);
        ProductionPaths.CheckCasing(parent, "_recovery"); ArchivePaths.EnsureDirectory(c, root);
        var id = NewId(); Collision(c, id);
        var temp = Path.Combine(root, ".pending-recovery-" + Guid.NewGuid().ToString("N"));
        if (BackupStorage.Check(temp) is not null) throw BackupException.Stop(NapIssueCodes.RecoveryPreservationFailed, "Recovery temporary already exists.");
        ArchivePaths.EnsureDirectory(c, temp);
        var evidence = new CatalogRecoveryArtifact(id, c.Id, DateTimeOffset.UtcNow, active.Size, active.Sha);
        var copy = Path.Combine(temp, evidence.Artifact);
        BackupStorage.Copy(c.Storage.CatalogPath, copy);
        Observer?.Invoke("copied");
        VerifyBytes(c.Storage.CatalogPath, copy, evidence);
        var bytes = Write(evidence);
        using (var manifest = new FileStream(Path.Combine(temp, "recovery_manifest.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { manifest.Write(bytes); manifest.Flush(true); }
        Verify(c, temp, evidence); Observer?.Invoke("verified");
        Verify(c, temp, evidence); VerifyBytes(c.Storage.CatalogPath, copy, evidence);
        Collision(c, id); BackupStorage.Context(c); BackupStorage.RequireDirectory(root); BackupStorage.RequireDirectory(temp);
        Directory.Move(temp, DirectoryPath(c, id));
        // Preservation is only usable after the final unit and source bytes have both been reverified.
        Verify(c, DirectoryPath(c, id), evidence);
        VerifyBytes(c.Storage.CatalogPath, Path.Combine(DirectoryPath(c, id), evidence.Artifact), evidence);
        return evidence;
    }
    internal static void Verify(UniverseContext c, string directory, CatalogRecoveryArtifact expected)
    {
        BackupStorage.RequireDirectory(directory);
        if (expected.UniverseId != c.Id) throw BackupException.Stop(NapIssueCodes.BackupWrongUniverse, "Recovery evidence belongs to another universe.");
        var manifest = Path.Combine(directory, "recovery_manifest.json"); var fingerprint = BackupStorage.Fingerprint(manifest);
        if (fingerprint.Size > 4096) throw Invalid();
        var bytes = File.ReadAllBytes(manifest);
        try
        {
            using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 }); var e = doc.RootElement;
            var names = new[] { "schema_version", "recovery_id", "universe_id", "created_utc", "reason", "artifact", "sha256", "size" };
            var actual = e.EnumerateObject().Select(p => p.Name).ToArray();
            if (actual.Length != names.Length || actual.Distinct(StringComparer.Ordinal).Count() != actual.Length ||
                !actual.Order(StringComparer.Ordinal).SequenceEqual(names.Order(StringComparer.Ordinal))) throw Invalid();
            if (e.GetProperty("universe_id").GetString() != c.Id.Value) throw BackupException.Stop(NapIssueCodes.BackupWrongUniverse, "Recovery manifest belongs to another universe.");
            if (e.GetProperty("schema_version").GetInt32() != 1 || e.GetProperty("recovery_id").GetString() != expected.RecoveryId.Value ||
                e.GetProperty("created_utc").GetString() != expected.CreatedUtc.ToString("O", CultureInfo.InvariantCulture) ||
                e.GetProperty("reason").GetString() != expected.Reason || e.GetProperty("artifact").GetString() != expected.Artifact ||
                e.GetProperty("sha256").GetString() != expected.Sha256.Hex || e.GetProperty("size").GetInt64() != expected.Size) throw Invalid();
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        { throw Invalid(); }
        if (!Directory.EnumerateFileSystemEntries(directory).Select(Path.GetFileName).Order(StringComparer.Ordinal)
            .SequenceEqual(new[] { expected.Artifact, "recovery_manifest.json" }.Order(StringComparer.Ordinal))) throw Invalid();
        BackupStorage.VerifyFile(Path.Combine(directory, expected.Artifact), expected.Size, expected.Sha256);
        BackupStorage.VerifyFile(manifest, fingerprint.Size, fingerprint.Sha);
    }
    private static byte[] Write(CatalogRecoveryArtifact e)
    {
        using var bytes = new MemoryStream();
        using (var w = new Utf8JsonWriter(bytes))
        {
            w.WriteStartObject(); w.WriteNumber("schema_version", 1); w.WriteString("recovery_id", e.RecoveryId.Value);
            w.WriteString("universe_id", e.UniverseId.Value); w.WriteString("created_utc", e.CreatedUtc.ToString("O", CultureInfo.InvariantCulture));
            w.WriteString("reason", e.Reason); w.WriteString("artifact", e.Artifact); w.WriteString("sha256", e.Sha256.Hex); w.WriteNumber("size", e.Size); w.WriteEndObject();
        }
        return bytes.ToArray();
    }
    private static void Collision(UniverseContext c, CatalogRecoveryId id)
    {
        BackupStorage.RequireDirectory(Root(c)); ProductionPaths.CheckCasing(Root(c), id.Value);
        if (BackupStorage.Check(DirectoryPath(c, id)) is not null) throw BackupException.Stop(NapIssueCodes.RecoveryPreservationFailed, "Recovery identity already exists; no overwrite is permitted.");
    }
    private static void VerifyBytes(string source, string copy, CatalogRecoveryArtifact e)
    {
        BackupStorage.VerifyFile(source, e.Size, e.Sha256); BackupStorage.VerifyFile(copy, e.Size, e.Sha256);
        using var a = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var b = new FileStream(copy, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (a.Length != e.Size || b.Length != e.Size) throw Invalid();
        var left = new byte[65536]; var right = new byte[65536]; long remaining = e.Size;
        while (remaining > 0)
        {
            var count = (int)Math.Min(remaining, left.Length); a.ReadExactly(left.AsSpan(0, count)); b.ReadExactly(right.AsSpan(0, count));
            if (!left.AsSpan(0, count).SequenceEqual(right.AsSpan(0, count))) throw Invalid(); remaining -= count;
        }
        if (a.Length != e.Size || b.Length != e.Size) throw Invalid();
    }
    private static BackupException Invalid() => BackupException.Stop(NapIssueCodes.RecoveryPreservationFailed, "Forensic recovery evidence failed its closed manifest or exact byte verification.");
}

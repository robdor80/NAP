using System.Globalization;
using System.Text.Json;

namespace NAP.Core;

internal static class BackupManifestCodec
{
    internal static byte[] Write(BackupManifest m)
    {
        using var bytes = new MemoryStream();
        using (var w = new Utf8JsonWriter(bytes))
        {
            w.WriteStartObject(); w.WriteNumber("schema_version", 1); w.WriteString("backup_id", m.BackupId.Value);
            w.WriteString("universe_id", m.UniverseId.Value); w.WriteString("backup_kind", m.Kind.ToString());
            w.WriteString("created_utc", m.CreatedUtc.ToString("O", CultureInfo.InvariantCulture));
            w.WriteString("artifact", m.Artifact); w.WriteString("sha256", m.Sha256.Hex); w.WriteNumber("size", m.Size);
            if (m.Kind == BackupKind.Database)
            { w.WriteNumber("catalog_schema_version", m.CatalogSchemaVersion!.Value); w.WriteString("purpose", m.DatabasePurpose.ToString()); }
            if (m.Kind == BackupKind.RepositorySnapshot)
            {
                w.WriteNumber("file_count", m.Files.Count); w.WritePropertyName("files"); WriteFiles(w, m.Files);
                w.WritePropertyName("directories"); WriteDirectories(w, m.Directories);
            }
            if (m.Kind == BackupKind.GitBundle)
            {
                w.WriteString("head", m.Head); w.WriteBoolean("dirty", m.Dirty!.Value);
                w.WriteString("contents", "local_git_objects_and_refs_only");
            }
            w.WriteEndObject();
        }
        return bytes.ToArray();
    }
    internal static Sha256Digest TreeHash(IReadOnlyList<BackupFile> files, IReadOnlyList<string> directories)
    {
        using var bytes = new MemoryStream();
        using (var w = new Utf8JsonWriter(bytes))
        { w.WriteStartObject(); w.WritePropertyName("files"); WriteFiles(w, files); w.WritePropertyName("directories"); WriteDirectories(w, directories); w.WriteEndObject(); }
        bytes.Position = 0; return new Sha256Hasher().Compute(bytes);
    }
    private static void WriteFiles(Utf8JsonWriter w, IReadOnlyList<BackupFile> files)
    {
        w.WriteStartArray(); foreach (var f in files)
        { w.WriteStartObject(); w.WriteString("relative_path", f.RelativePath); w.WriteNumber("size", f.Size); w.WriteString("sha256", f.Sha256.Hex); w.WriteEndObject(); }
        w.WriteEndArray();
    }
    private static void WriteDirectories(Utf8JsonWriter w, IReadOnlyList<string> directories)
    { w.WriteStartArray(); foreach (var d in directories) w.WriteStringValue(d); w.WriteEndArray(); }

    internal static BackupManifest Read(byte[] bytes, UniverseContext context)
    {
        try
        {
            using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 }); var e = doc.RootElement;
            var kind = EnumValue<BackupKind>(e.GetProperty("backup_kind").GetString()!);
            var common = new[] { "schema_version", "backup_id", "universe_id", "backup_kind", "created_utc", "artifact", "sha256", "size" };
            Fields(e, common.Concat(kind switch
            {
                BackupKind.Database => new[] { "catalog_schema_version", "purpose" },
                BackupKind.RepositorySnapshot => new[] { "file_count", "files", "directories" },
                _ => new[] { "head", "dirty", "contents" }
            }).ToArray());
            if (e.GetProperty("schema_version").GetInt32() != 1) throw Invalid();
            var universe = new UniverseId(e.GetProperty("universe_id").GetString()!);
            if (universe != context.Id) throw BackupException.Stop(NapIssueCodes.BackupWrongUniverse, "Backup metadata belongs to another universe.");
            var id = new BackupId(e.GetProperty("backup_id").GetString()!);
            var date = e.GetProperty("created_utc").GetString()!;
            if (!DateTimeOffset.TryParseExact(date, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var created) || created.Offset != TimeSpan.Zero) throw Invalid();
            var size = e.GetProperty("size").GetInt64(); if (size < 0) throw Invalid();
            var hash = new Sha256Digest(e.GetProperty("sha256").GetString()!);
            int? version = null; DatabaseBackupPurpose? purpose = null; string? head = null; bool? dirty = null;
            var files = new List<BackupFile>(); var dirs = new List<string>();
            if (kind == BackupKind.Database)
            {
                version = e.GetProperty("catalog_schema_version").GetInt32(); if (version != CatalogSchema.Version) throw Invalid();
                purpose = EnumValue<DatabaseBackupPurpose>(e.GetProperty("purpose").GetString()!); if (size == 0) throw Invalid();
            }
            if (kind == BackupKind.RepositorySnapshot)
            {
                foreach (var f in e.GetProperty("files").EnumerateArray())
                {
                    Fields(f, "relative_path", "size", "sha256"); var path = f.GetProperty("relative_path").GetString()!;
                    BackupStorage.Relative(path); var length = f.GetProperty("size").GetInt64(); if (length < 0) throw Invalid();
                    files.Add(new(path, length, new Sha256Digest(f.GetProperty("sha256").GetString()!)));
                }
                foreach (var d in e.GetProperty("directories").EnumerateArray())
                { var path = d.GetString()!; BackupStorage.Relative(path); dirs.Add(path); }
                Ordered(files.Select(f => f.RelativePath)); Ordered(dirs);
                if (files.Select(f => f.RelativePath).Intersect(dirs, StringComparer.Ordinal).Any() ||
                    e.GetProperty("file_count").GetInt32() != files.Count || checked(files.Sum(f => f.Size)) != size || TreeHash(files, dirs) != hash) throw Invalid();
                foreach (var path in files.Select(f => f.RelativePath).Concat(dirs))
                { var slash = path.LastIndexOf('/'); if (slash >= 0 && !dirs.Contains(path[..slash], StringComparer.Ordinal)) throw Invalid(); }
            }
            if (kind == BackupKind.GitBundle)
            {
                head = e.GetProperty("head").GetString() ?? throw Invalid();
                if ((head.Length != 40 && head.Length != 64) || head.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')) || size == 0) throw Invalid();
                dirty = e.GetProperty("dirty").GetBoolean();
                if (e.GetProperty("contents").GetString() != "local_git_objects_and_refs_only") throw Invalid();
            }
            var manifest = new BackupManifest(id, universe, kind, created, size, hash, version, purpose, files, dirs, head, dirty);
            if (e.GetProperty("artifact").GetString() != manifest.Artifact) throw Invalid();
            return manifest;
        }
        catch (BackupException) { throw; }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        { throw Invalid(); }
    }
    private static T EnumValue<T>(string value) where T : struct, Enum =>
        Enum.GetNames<T>().Contains(value, StringComparer.Ordinal) ? Enum.Parse<T>(value) : throw Invalid();
    private static void Fields(JsonElement e, params string[] names)
    {
        var actual = e.EnumerateObject().Select(p => p.Name).ToArray();
        if (actual.Length != names.Length || actual.Distinct(StringComparer.Ordinal).Count() != actual.Length ||
            !actual.Order(StringComparer.Ordinal).SequenceEqual(names.Order(StringComparer.Ordinal))) throw Invalid();
    }
    private static void Ordered(IEnumerable<string> paths)
    { var a = paths.ToArray(); if (!a.SequenceEqual(a.Order(StringComparer.Ordinal)) || a.Distinct(StringComparer.Ordinal).Count() != a.Length) throw Invalid(); }
    private static BackupException Invalid() => BackupException.Stop(NapIssueCodes.BackupManifestInvalid, "Backup manifest is not a closed, supported version 1 document.");
}

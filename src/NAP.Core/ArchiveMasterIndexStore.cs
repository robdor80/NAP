using System.Text.Json;

namespace NAP.Core;

/// <summary>Strict local JSON persistence. Publication is internal and requires the executor's live archive lock.</summary>
public sealed class ArchiveMasterIndexStore
{
    private readonly UniverseContext _context;

    public ArchiveMasterIndexStore(UniverseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public string IndexPath => Path.Combine(_context.Storage.ArchiveRoot, "_nap", "master_index.json");

    public ArchiveMasterIndex Load()
    {
        ArchiveRootValidator.Require(_context);
        if (!ArchivePaths.FileExists(IndexPath, NapIssueCodes.ArchiveIndexInvalid)) return new ArchiveMasterIndex(_context.Id, []);
        try
        {
            using var input = new FileStream(IndexPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var document = JsonDocument.Parse(input);
            var root = document.RootElement;
            RequireProperties(root, "schema_version", "universe_id", "entries");
            var version = root.GetProperty("schema_version");
            if (version.ValueKind != JsonValueKind.Number || version.GetRawText() != "1") throw new FormatException("Expected schema_version integer 1.");
            var universe = new UniverseId(Text(root, "universe_id"));
            if (universe != _context.Id) throw new FormatException("The archive index belongs to a different universe.");
            var array = root.GetProperty("entries");
            if (array.ValueKind != JsonValueKind.Array) throw new FormatException("Expected an entries array.");
            var entries = new List<ArchiveMasterIndexEntry>();
            foreach (var entry in array.EnumerateArray())
            {
                RequireProperties(entry, "asset_id", "asset_type", "master_sha256", "master_size_bytes", "relative_directory", "verified");
                var size = entry.GetProperty("master_size_bytes");
                if (size.ValueKind != JsonValueKind.Number || !size.TryGetInt64(out var bytes) || bytes <= 0 ||
                    size.GetRawText().Any(c => c is < '0' or > '9')) throw new FormatException("Expected a positive integer master size.");
                if (entry.GetProperty("verified").ValueKind != JsonValueKind.True) throw new FormatException("An archive entry must be verified.");
                entries.Add(new ArchiveMasterIndexEntry(Text(entry, "asset_id"), Text(entry, "asset_type"),
                    new Sha256Digest(Text(entry, "master_sha256")), bytes, Text(entry, "relative_directory"), true));
            }
            return new ArchiveMasterIndex(universe, entries);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException or InvalidOperationException)
        {
            throw ArchiveStorageException.Stop(NapIssueCodes.ArchiveIndexInvalid, "The archive index is invalid; explicit repair is required.", IndexPath, ex);
        }
    }

    internal void Publish(ArchiveMasterIndex index, ArchiveLock archiveLock)
    {
        ArgumentNullException.ThrowIfNull(index);
        archiveLock.RequireWrites(_context);
        if (index.UniverseId != _context.Id) throw new ArgumentException("The archive index must belong to the current universe.", nameof(index));
        ArchiveRootValidator.Require(_context);
        ArchivePaths.FileExists(IndexPath, NapIssueCodes.ArchiveIndexInvalid);
        var directory = Path.GetDirectoryName(IndexPath)!;
        ArchivePaths.EnsureDirectory(_context, directory);
        var temp = Path.Combine(directory, $"master_index.{Guid.NewGuid():N}.tmp");
        using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStartObject();
                writer.WriteNumber("schema_version", 1);
                writer.WriteString("universe_id", index.UniverseId.Value);
                writer.WriteStartArray("entries");
                foreach (var entry in index.Entries)
                {
                    writer.WriteStartObject();
                    writer.WriteString("asset_id", entry.AssetId);
                    writer.WriteString("asset_type", entry.AssetType);
                    writer.WriteString("master_sha256", entry.MasterSha256.Hex);
                    writer.WriteNumber("master_size_bytes", entry.MasterSizeBytes);
                    writer.WriteString("relative_directory", entry.RelativeDirectory);
                    writer.WriteBoolean("verified", entry.Verified);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
                writer.Flush();
            }
            output.WriteByte((byte)'\n');
            output.Flush(flushToDisk: true);
        }
        archiveLock.RequireWrites(_context);
        ArchiveRootValidator.Require(_context);
        ArchivePaths.FileExists(temp, NapIssueCodes.ArchiveIndexInvalid);
        ArchivePaths.FileExists(IndexPath, NapIssueCodes.ArchiveIndexInvalid);
        File.Move(temp, IndexPath, overwrite: true);
    }

    private static string Text(JsonElement element, string name)
    {
        var value = element.GetProperty(name);
        if (value.ValueKind != JsonValueKind.String) throw new FormatException($"Expected {name} to be a string.");
        return value.GetString()!;
    }

    private static void RequireProperties(JsonElement element, params string[] expected)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new FormatException("Expected a JSON object.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!expected.Contains(property.Name, StringComparer.Ordinal) || !names.Add(property.Name))
                throw new FormatException("Unexpected or duplicate JSON property.");
        if (names.Count != expected.Length) throw new FormatException("Missing JSON property.");
    }
}

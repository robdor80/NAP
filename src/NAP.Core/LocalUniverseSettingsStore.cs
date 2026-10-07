using System.Text.Json;

namespace NAP.Core;

/// <summary>Local roots only. Closed versioned format, atomic publication, no secret fields.</summary>
public sealed class LocalUniverseSettingsStore
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NAP", "settings.json");
    public string SettingsPath { get; }
    public LocalUniverseSettingsStore(string settingsPath)
    {
        if (!Path.IsPathFullyQualified(settingsPath)) throw new ArgumentException("Settings path must be absolute.", nameof(settingsPath));
        SettingsPath = Path.GetFullPath(settingsPath);
    }
    public IReadOnlyList<UniverseStorageConfig> Load()
    {
        Guard();
        if (!File.Exists(SettingsPath)) return Array.AsReadOnly(Array.Empty<UniverseStorageConfig>());
        using var stream = new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 1024 * 1024) throw new InvalidDataException("Settings exceed their bounded format.");
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement; Fields(root, "schema_version", "universes");
        if (root.GetProperty("schema_version").GetInt32() != 1) throw new InvalidDataException("Unsupported local settings version.");
        var rows = new List<UniverseStorageConfig>();
        foreach (var row in root.GetProperty("universes").EnumerateArray())
        {
            Fields(row, "universe_id", "workspace_root", "production_root", "archive_root");
            rows.Add(new(new(row.GetProperty("universe_id").GetString()!), row.GetProperty("workspace_root").GetString()!,
                row.GetProperty("production_root").GetString()!, row.GetProperty("archive_root").GetString()!));
        }
        ValidateStructure(rows); return rows.AsReadOnly();
    }
    /// <summary>Validate all structure, and availability of the explicit universe plus every new/changed entry.
    /// Unchanged remembered siblings may be offline. Omit the ID only for a bulk configuration update.</summary>
    public void Save(IEnumerable<UniverseStorageConfig> configurations, UniverseId? configuredUniverse = null)
    {
        var rows = configurations.ToArray(); ValidateStructure(rows); Guard();
        foreach (var row in rows)
            if (new[] { row.WorkspaceRoot, row.ProductionRoot, row.ArchiveRoot }.Any(root => ProductionPaths.Within(root, SettingsPath)))
                throw new InvalidDataException("Local settings must remain outside all universe storage roots.");
        var parent = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(parent); Guard();
        using var lease = ExecutionMutex.Acquire("LocalSettings", parent, Path.GetFileName(SettingsPath));
        // Never silently overwrite an unreadable/unknown configuration with a new one.
        var prior = Load();
        if (configuredUniverse is not null && rows.All(r => r.UniverseId != configuredUniverse))
            throw new ArgumentException("The configured universe must be included in the saved settings.", nameof(configuredUniverse));
        // Every new/changed universe is physically checked. The explicitly saved universe is
        // checked even when unchanged; remembered, unchanged siblings may be disconnected.
        foreach (var row in rows.Where(r => r.UniverseId == configuredUniverse || !prior.Contains(r))) ValidateAvailable(row);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { schema_version = 1, universes = rows.OrderBy(r => r.UniverseId.Value, StringComparer.Ordinal)
            .Select(r => new { universe_id = r.UniverseId.Value, workspace_root = r.WorkspaceRoot, production_root = r.ProductionRoot, archive_root = r.ArchiveRoot }) }, new JsonSerializerOptions { WriteIndented = true });
        if (bytes.Length > 1024 * 1024) throw new InvalidDataException("Settings exceed their bounded format.");
        var temp = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { output.Write(bytes); output.Flush(true); }
            Guard(); File.Move(temp, SettingsPath, overwrite: true);
        }
        finally
        {
            ProductionPaths.CheckPath(temp, NapIssueCodes.CatalogInvalid, NapIssueCodes.CatalogInvalid);
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
    public static void Validate(IEnumerable<UniverseStorageConfig> configurations, bool requireAvailable = true)
    {
        var rows = configurations.ToArray(); ValidateStructure(rows);
        if (requireAvailable) foreach (var row in rows) ValidateAvailable(row);
    }
    public static void ValidateStructure(IEnumerable<UniverseStorageConfig> configurations)
    {
        var rows = configurations.ToArray();
        var isolation = UniverseStorageIsolationValidator.Validate(rows);
        if (isolation.ShouldStop) throw new InvalidDataException("Universe storage roots overlap.");
        foreach (var row in rows)
        {
            if (ProductionPaths.Overlaps(row.WorkspaceRoot, row.ProductionRoot) || ProductionPaths.Overlaps(row.WorkspaceRoot, row.ArchiveRoot) ||
                ProductionPaths.Overlaps(row.ProductionRoot, row.ArchiveRoot))
                throw ProductionStorageException.Stop(NapIssueCodes.ProductionRootInvalid, "Roots of a universe must remain isolated.");
        }
    }
    public static void ValidateAvailable(UniverseStorageConfig row)
    {
        ValidateStructure([row]);
        var context = new UniverseContext(new UniverseProfile(row.UniverseId, row.UniverseId.Value), row);
        var production = new ProductionStorageRootValidator().Validate(context);
        if (production.ShouldStop) throw new ProductionStorageException(production);
        var archive = new ArchiveRootValidator().Validate(context);
        if (archive.ShouldStop) throw new ArchiveStorageException(archive);
        var workspace = ProductionPaths.CheckPath(row.WorkspaceRoot, NapIssueCodes.CatalogInvalid, NapIssueCodes.CatalogInvalid);
        if (workspace is null || (workspace & FileAttributes.Directory) == 0) throw new InvalidDataException("WorkspaceRoot must be an existing controlled directory.");
        if (row.WorkspaceRoot.StartsWith("\\\\", StringComparison.Ordinal) || new DriveInfo(row.WorkspaceRoot).DriveType == DriveType.Network)
            throw new InvalidDataException("WorkspaceRoot must be local.");
    }
    private void Guard()
    {
        ProductionPaths.CheckPath(SettingsPath, NapIssueCodes.CatalogInvalid, NapIssueCodes.CatalogInvalid);
        for (var directory = Path.GetDirectoryName(SettingsPath); directory is not null; directory = Path.GetDirectoryName(directory))
            if (Directory.Exists(Path.Combine(directory, ".git")) || File.Exists(Path.Combine(directory, ".git")))
                throw new InvalidDataException("Local settings cannot be stored in a Git checkout.");
        if (Directory.Exists(SettingsPath)) throw new InvalidDataException("Settings must be a regular file.");
    }
    private static void Fields(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Settings object required.");
        var found = element.EnumerateObject().Select(p => p.Name).ToArray();
        if (found.Length != names.Length || found.Distinct(StringComparer.Ordinal).Count() != names.Length || names.Any(n => !found.Contains(n, StringComparer.Ordinal)))
            throw new InvalidDataException("Unknown, duplicated or missing local settings fields.");
    }
}

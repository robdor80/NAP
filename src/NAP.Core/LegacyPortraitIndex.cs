using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace NAP.Core;

/// <summary>
/// Historical approved portraits are indexed in a separate versioned SQLite file.
/// AssetCatalog.db v1 retains its strict master/manifest and audited publication contract.
/// </summary>
public sealed class LegacyPortraitIndex(UniverseContext context)
{
    private const int Version = 1;
    private static readonly string[] FacetedTraits =
    [
        "/age/life_stage/value", "/age/apparent_age/value", "/eyes/color/value",
        "/hair/color/value", "/body/build/value", "/facial_hair/type/value"
    ];
    public string DatabasePath => Path.Combine(context.Storage.StateRoot, "LegacyPortraits.db");

    public int Import(string assetsRoot)
    {
        if (context.Id.Value != "nimroel") throw new InvalidOperationException("Solo se admite el legado de Nimroel.");
        var inventory = LegacyPortraitReader.Scan(assetsRoot);
        if (inventory.Issues.Count > 0)
            throw new InvalidDataException("Incidencias de inventario: " + string.Join(" | ", inventory.Issues.Take(5)));
        if (inventory.Portraits.Count == 0) throw new InvalidDataException("No se encontraron retratos históricos.");
        if (inventory.Portraits.Select(p => p.AssetId).Distinct(StringComparer.Ordinal).Count() != inventory.Portraits.Count)
            throw new InvalidDataException("Identidades históricas duplicadas.");
        var catalog = new AssetCatalog(context);
        foreach (var item in inventory.Portraits)
            if (catalog.Get(item.AssetId) is not null)
                throw new InvalidDataException("Un retrato histórico ya existe en el catálogo nativo: " + item.AssetId);

        CatalogBoundary.Paths(context, createState: true);
        var target = DatabasePath;
        var temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var db = Connect(temp, SqliteOpenMode.ReadWriteCreate))
            {
                using var transaction = db.BeginTransaction();
                Execute(db, transaction, "CREATE TABLE meta (singleton INTEGER PRIMARY KEY CHECK(singleton=1), schema_version INTEGER NOT NULL, universe_id TEXT NOT NULL, assets_root TEXT NOT NULL)");
                Execute(db, transaction, "CREATE TABLE portraits (asset_id TEXT COLLATE BINARY PRIMARY KEY, culture TEXT NOT NULL, location TEXT NOT NULL, profession TEXT NOT NULL, sex TEXT NOT NULL, relative_directory TEXT NOT NULL UNIQUE, sha256 TEXT NOT NULL, size_bytes INTEGER NOT NULL CHECK(size_bytes>0), width INTEGER NOT NULL, height INTEGER NOT NULL, traits_json TEXT NOT NULL)");
                Execute(db, transaction, "INSERT INTO meta VALUES(1,$v,$u,$root)", "$v", Version, "$u", context.Id.Value, "$root", Path.GetFullPath(assetsRoot));
                foreach (var p in inventory.Portraits)
                {
                    var traits = CatalogVisualIdentity.Extract(File.ReadAllBytes(p.VisualIdentityPath));
                    var json = JsonSerializer.Serialize(traits);
                    Execute(db, transaction,
                        "INSERT INTO portraits VALUES($id,$culture,$location,$profession,$sex,$relative,$sha,$size,$w,$h,$traits)",
                        "$id", p.AssetId, "$culture", p.Culture.ToLowerInvariant(), "$location", p.Location.ToLowerInvariant(),
                        "$profession", p.Profession, "$sex", p.Sex, "$relative", p.RelativeDirectory,
                        "$sha", p.WebpSha256, "$size", p.WebpBytes, "$w", p.Width, "$h", p.Height, "$traits", json);
                }
                transaction.Commit();
                using var check = db.CreateCommand();
                check.CommandText = "PRAGMA integrity_check";
                if (!string.Equals(check.ExecuteScalar()?.ToString(), "ok", StringComparison.Ordinal))
                    throw new InvalidDataException("Falló la comprobación del índice histórico.");
            }
            File.Move(temp, target, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        return inventory.Portraits.Count;
    }

    public IReadOnlyList<LegacyPortrait> Query(CatalogFilter? filter = null)
    {
        if (!File.Exists(DatabasePath)) return [];
        filter ??= new CatalogFilter();
        var result = new List<LegacyPortrait>();
        using var db = OpenRead();
        var root = (string)Scalar(db, "SELECT assets_root FROM meta WHERE singleton=1")!;
        using var command = db.CreateCommand();
        command.CommandText = "SELECT asset_id,culture,location,profession,sex,relative_directory,sha256,size_bytes,width,height,traits_json FROM portraits ORDER BY asset_id COLLATE BINARY";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetString(0);
            var culture = reader.GetString(1);
            var location = reader.GetString(2);
            var role = reader.GetString(3);
            var sex = reader.GetString(4);
            var relative = reader.GetString(5);
            var traits = JsonSerializer.Deserialize<CatalogVisualTrait[]>(reader.GetString(10)) ?? [];
            if (!Matches(filter, id, culture, location, role, sex, traits)) continue;
            var directory = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!ProductionPaths.Within(root, directory))
                throw new InvalidDataException("Una ruta histórica sale de la carpeta Assets autorizada.");
            string FileName(string suffix) => Path.Combine(directory, id + suffix);
            result.Add(new LegacyPortrait(id, culture, location, role, sex, relative,
                FileName(".webp"), FileName("_prompt.md"), FileName("_info.md"),
                FileName("_visual_identity.json"), reader.GetString(6), reader.GetInt64(7),
                reader.GetInt32(8), reader.GetInt32(9)));
        }
        return result.AsReadOnly();
    }

    /// <summary>Historical SQLite contributes only indexed classification and scalar traits.
    /// No WebP, PNG, physical path or document is read or sent to an AI service.</summary>
    public IReadOnlyList<CatalogDiversityEntry> DiversityEntries(CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        CatalogBoundary.Paths(context);
        if (!CatalogBoundary.Regular(DatabasePath)) return [];
        var entries = new List<CatalogDiversityEntry>();
        using var db = OpenRead();
        using var command = db.CreateCommand();
        command.CommandText = "SELECT asset_id,culture,location,profession,sex,traits_json FROM portraits ORDER BY asset_id COLLATE BINARY";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            cancellation.ThrowIfCancellationRequested();
            if (entries.Count >= 50_000) throw new InvalidDataException("Índice histórico demasiado grande para la auditoría.");
            var traits = JsonSerializer.Deserialize<CatalogVisualTrait[]>(reader.GetString(5))
                ?? throw new InvalidDataException("Metadatos históricos no disponibles.");
            entries.Add(new CatalogDiversityEntry(reader.GetString(0), "portrait", "portrait_npc",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["culture"] = reader.GetString(1), ["location"] = reader.GetString(2),
                    ["role"] = reader.GetString(3), ["sex"] = reader.GetString(4)
                }, Array.AsReadOnly(traits), true));
        }
        return entries.AsReadOnly();
    }

    public LegacyPortrait? Get(string assetId) =>
        Query(new CatalogFilter(assetId: assetId)).FirstOrDefault();

    public CatalogStatistics Statistics(CatalogFilter filter)
    {
        var entries = Query(filter);
        static IReadOnlyList<CatalogDistribution> Group(IEnumerable<(string Dim, string Value, CatalogTraitType? Type)> values) =>
            Array.AsReadOnly(values.GroupBy(x => x).Select(g => new CatalogDistribution(g.Key.Dim, g.Key.Value, g.LongCount(), g.Key.Type))
                .OrderBy(x => x.Dimension, StringComparer.Ordinal).ThenBy(x => x.Value, StringComparer.Ordinal).ToArray());
        return new CatalogStatistics(entries.Count,
            Group(entries.Select(_ => ("asset_type", "portrait", (CatalogTraitType?)null))),
            Group(entries.Select(_ => ("production_profile", "portrait_npc", (CatalogTraitType?)null))),
            Group(entries.SelectMany(p => new (string, string, CatalogTraitType?)[]
            {
                ("culture", p.Culture, null), ("location", p.Location, null),
                ("role", p.Profession, null), ("sex", p.Sex, null)
            })),
            Group(entries.SelectMany(p => GetTraits(p).Where(t => FacetedTraits.Contains(t.Key, StringComparer.Ordinal))
                .Select(t => (t.Key, t.Value, (CatalogTraitType?)t.Type)))));
    }

    private IReadOnlyList<CatalogVisualTrait> GetTraits(LegacyPortrait portrait)
    {
        using var db = OpenRead();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT traits_json FROM portraits WHERE asset_id=$id";
        cmd.Parameters.AddWithValue("$id", portrait.AssetId);
        return JsonSerializer.Deserialize<CatalogVisualTrait[]>((string)cmd.ExecuteScalar()!) ?? [];
    }

    private static bool Matches(CatalogFilter f, string id, string culture, string location, string role, string sex,
        IReadOnlyList<CatalogVisualTrait> traits)
    {
        if (f.AssetId is not null && f.AssetId != id) return false;
        if (f.AssetType is not null && f.AssetType != "portrait") return false;
        if (f.ProductionProfile is not null && f.ProductionProfile != "portrait_npc") return false;
        var classes = new Dictionary<string, string>(StringComparer.Ordinal)
        { ["culture"] = culture, ["location"] = location, ["role"] = role, ["sex"] = sex };
        return f.Classification.All(pair => classes.TryGetValue(pair.Key, out var value) && value == pair.Value) &&
            f.Traits.All(t => traits.Any(x => x == t));
    }

    public void Verify(LegacyPortrait p)
    {
        if (!File.Exists(p.WebpPath) || new FileInfo(p.WebpPath).Length != p.WebpBytes)
            throw new InvalidDataException("El WebP histórico ha desaparecido o ha cambiado.");
        using var input = File.OpenRead(p.WebpPath);
        var digest = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
        if (digest != p.WebpSha256) throw new InvalidDataException("El WebP histórico ha cambiado desde la importación.");
    }

    private SqliteConnection OpenRead()
    {
        var db = Connect(DatabasePath, SqliteOpenMode.ReadOnly);
        try
        {
            if (Convert.ToInt64(Scalar(db, "SELECT schema_version FROM meta WHERE singleton=1")) != Version ||
                (string?)Scalar(db, "SELECT universe_id FROM meta WHERE singleton=1") != context.Id.Value)
                throw new InvalidDataException("Índice histórico incompatible con este universo.");
            return db;
        }
        catch { db.Dispose(); throw; }
    }

    private static SqliteConnection Connect(string path, SqliteOpenMode mode)
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = path, Mode = mode, Cache = SqliteCacheMode.Private, Pooling = false }.ToString());
        db.Open();
        return db;
    }
    private static object? Scalar(SqliteConnection db, string sql)
    {
        using var cmd = db.CreateCommand(); cmd.CommandText = sql; return cmd.ExecuteScalar();
    }
    private static void Execute(SqliteConnection db, SqliteTransaction transaction, string sql, params object[] args)
    {
        using var cmd = db.CreateCommand(); cmd.Transaction = transaction; cmd.CommandText = sql;
        for (int i = 0; i < args.Length; i += 2) cmd.Parameters.AddWithValue((string)args[i], args[i+1]);
        cmd.ExecuteNonQuery();
    }
}

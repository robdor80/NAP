using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using SixLabors.ImageSharp;

namespace NAP.Core;

/// <summary>
/// Independently scoped, locally verified provenance for pre-NAP masters.
/// Never registers a native audited publication and never writes an image.
/// Separate from LegacyPortraits.db so re-importing WebP cannot erase provenance.
/// </summary>
public sealed record LegacyMasterEvidence(
    string AssetId, string PngSha256, string? HistoricalSha256, string EvidenceKind,
    string RelativeSourcePath, long SizeBytes, int Width, int Height, string? GitCommit)
{
    public bool RemoteCloudVerified => false;
    public string StatusLabel => EvidenceKind switch
    {
        "historic_sha_match" => "PNG local: coincide con SHA histórico",
        "git_20260926_match" => "PNG local: coincidencia documentada con Git (26/09/2026)",
        _ => "PNG local: huella nueva, sin SHA histórico"
    };
    public string CloudStatus => "TeraBox remoto: no verificado";
}

public sealed class LegacyMasterEvidenceIndex(UniverseContext context)
{
    private const int Version = 1;
    private const string GitReference = "66c2de7dae0625dda21b6073da688e28e1ccbcb8";
    private static readonly Regex ShaPattern = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    public string DatabasePath => Path.Combine(context.Storage.StateRoot, "LegacyMasterEvidence.db");

    /// <summary>Requires explicit user-chosen manifest and directory of downloaded PNG files.</summary>
    public int Import(string evidencePath, string downloadedRoot)
    {
        if (context.Id.Value != "nimroel") throw new InvalidOperationException("Solo Nimroel admite este expediente.");
        var originals = Path.GetFullPath(downloadedRoot);
        if (!Directory.Exists(originals)) throw new DirectoryNotFoundException("No existe la carpeta de PNG descargados.");
        if ((File.GetAttributes(originals) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("La carpeta de origen no puede ser un enlace.");
        var portraits = new LegacyPortraitIndex(context).Query().ToDictionary(p => p.AssetId, StringComparer.Ordinal);
        if (portraits.Count != 39) throw new InvalidDataException("Es necesario disponer de exactamente 39 retratos históricos indexados.");

        using var manifest = JsonDocument.Parse(File.ReadAllBytes(evidencePath));
        var root = manifest.RootElement;
        if (root.GetProperty("schema").GetString() != "nimroel_historical_master_reconciliation" ||
            root.GetProperty("schema_version").GetString() != "1.0" ||
            root.GetProperty("universe").GetString() != "nimroel")
            throw new InvalidDataException("El manifiesto de conciliación no corresponde a Nimroel v1.");
        var sourceEntries = root.GetProperty("entries").EnumerateArray().ToArray();
        if (sourceEntries.Length != 39) throw new InvalidDataException("El expediente no contiene 39 entradas.");
        var files = Directory.EnumerateFiles(originals, "*.png", new EnumerationOptions
        {
            RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = false, ReturnSpecialDirectories = false
        }).Take(5001).ToArray();
        if (files.Length > 5000) throw new InvalidDataException("Origen demasiado extenso para una conciliación explícita.");
        var byName = files.GroupBy(Path.GetFileName, StringComparer.Ordinal).ToDictionary(g => g.Key!, g => g.ToArray(), StringComparer.Ordinal);
        var rows = new List<LegacyMasterEvidence>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in sourceEntries)
        {
            var id = entry.GetProperty("asset_id").GetString() ?? "";
            if (!portraits.ContainsKey(id) || !seen.Add(id))
                throw new InvalidDataException("ID inesperado o repetido: " + id);
            var name = entry.GetProperty("png_file").GetString();
            if (name != id + ".png" || !byName.TryGetValue(name, out var matches) || matches.Length != 1)
                throw new InvalidDataException("PNG no localizado de forma inequívoca: " + id);
            var path = Path.GetFullPath(matches[0]);
            if (!ProductionPaths.Within(originals, path)) throw new InvalidDataException("Ruta PNG fuera del origen.");
            for (string? d = Path.GetDirectoryName(path); d is not null && ProductionPaths.Within(originals, d); d = Path.GetDirectoryName(d))
                if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Carpeta enlazada no admitida.");
            if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                throw new InvalidDataException("PNG enlazado o no regular.");
            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > 64L * 1024 * 1024)
                throw new InvalidDataException("PNG de tamaño inválido: " + id);
            using var input = File.OpenRead(path);
            Span<byte> pngHeader = stackalloc byte[8];
            input.ReadExactly(pngHeader);
            if (!pngHeader.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
                throw new InvalidDataException("Cabecera PNG inválida: " + id);
            input.Position = 0;
            var digest = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
            var expected = entry.GetProperty("sha256_copia_local").GetString() ?? "";
            if (!ShaPattern.IsMatch(expected) || digest != expected)
                throw new InvalidDataException("SHA-256 de PNG distinto del manifiesto: " + id);
            using var image = Image.Load(path);
            var oldElement = entry.GetProperty("sha256_registro_septiembre_2026");
            var old = oldElement.ValueKind == JsonValueKind.Null ? null : oldElement.GetString();
            if (old is not null && !ShaPattern.IsMatch(old)) throw new InvalidDataException("SHA histórico mal formado.");
            var status = entry.GetProperty("estado_conciliacion").GetString();
            string kind; string? commit = null;
            switch (status)
            {
                case "coincide_sha_registro_historico" when old == digest:
                    kind = "historic_sha_match"; break;
                case "identico_a_png_git_2026_09_26" when
                    id == "portrait_treskal_farmer_female_001" && old is not null && old != digest:
                    kind = "git_20260926_match"; commit = GitReference; break;
                case "png_valido_sha_nuevo_sin_referencia_previa" when old is null:
                    kind = "new_local_fingerprint"; break;
                default: throw new InvalidDataException("Estado o comparación histórica incoherente: " + id);
            }
            if (entry.GetProperty("cloud_api_verificada").GetBoolean())
                throw new InvalidDataException("No se admite afirmar verificación remota sin prueba independiente.");
            rows.Add(new LegacyMasterEvidence(id, digest, old, kind,
                Path.GetRelativePath(originals, path).Replace('\\', '/'), info.Length, image.Width, image.Height, commit));
        }
        if (seen.Count != portraits.Count) throw new InvalidDataException("Faltan retratos históricos.");
        if (rows.Count(x => x.EvidenceKind == "historic_sha_match") != 14 ||
            rows.Count(x => x.EvidenceKind == "git_20260926_match") != 1 ||
            rows.Count(x => x.EvidenceKind == "new_local_fingerprint") != 24)
            throw new InvalidDataException("La distribución de evidencias no coincide con el acta aprobada.");

        CatalogBoundary.Paths(context, createState: true);
        if (File.Exists(DatabasePath))
        {
            var old = ReadAll().OrderBy(x => x.AssetId, StringComparer.Ordinal).ToArray();
            if (old.Length == rows.Count && old.Zip(rows.OrderBy(x => x.AssetId, StringComparer.Ordinal))
                .All(pair => pair.First == pair.Second)) return rows.Count; // idempotent, never silently overwrites evidence
            throw new InvalidDataException("Ya existe otro expediente de maestros. Revisión explícita obligatoria.");
        }
        var tmp = DatabasePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var db = Connect(tmp, SqliteOpenMode.ReadWriteCreate))
            {
                using var tx = db.BeginTransaction();
                Exec(db, tx, "CREATE TABLE meta (singleton INTEGER PRIMARY KEY CHECK(singleton=1), version INTEGER NOT NULL, universe_id TEXT NOT NULL, source_root TEXT NOT NULL, cloud_verified INTEGER NOT NULL CHECK(cloud_verified=0))");
                Exec(db, tx, "CREATE TABLE masters (asset_id TEXT COLLATE BINARY PRIMARY KEY, png_sha256 TEXT NOT NULL, historical_sha256 TEXT, evidence_kind TEXT NOT NULL, source_relative_path TEXT NOT NULL, size_bytes INTEGER NOT NULL, width INTEGER NOT NULL, height INTEGER NOT NULL, git_commit TEXT)");
                Exec(db, tx, "INSERT INTO meta VALUES (1,1,'nimroel',$root,0)", ("$root", originals));
                foreach (var row in rows)
                    Exec(db, tx, "INSERT INTO masters VALUES($id,$sha,$old,$kind,$rel,$size,$w,$h,$git)",
                        ("$id", row.AssetId), ("$sha", row.PngSha256), ("$old", (object?)row.HistoricalSha256 ?? DBNull.Value),
                        ("$kind", row.EvidenceKind), ("$rel", row.RelativeSourcePath), ("$size", row.SizeBytes),
                        ("$w", row.Width), ("$h", row.Height), ("$git", (object?)row.GitCommit ?? DBNull.Value));
                tx.Commit();
                using var check = db.CreateCommand(); check.CommandText = "PRAGMA integrity_check";
                if (check.ExecuteScalar()?.ToString() != "ok") throw new InvalidDataException("Integridad SQLite fallida.");
            }
            File.Move(tmp, DatabasePath); // do not replace an existing evidence database
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
        return rows.Count;
    }

    public LegacyMasterEvidence? Get(string assetId) => ReadAll().FirstOrDefault(x => x.AssetId == assetId);
    public IReadOnlyList<LegacyMasterEvidence> ReadAll()
    {
        if (!File.Exists(DatabasePath)) return [];
        var rows = new List<LegacyMasterEvidence>();
        using var db = Connect(DatabasePath, SqliteOpenMode.ReadOnly);
        using (var check = db.CreateCommand())
        {
            check.CommandText = "SELECT version,universe_id,cloud_verified FROM meta WHERE singleton=1";
            using var cursor = check.ExecuteReader();
            if (!cursor.Read() || cursor.GetInt32(0) != Version || cursor.GetString(1) != context.Id.Value || cursor.GetInt32(2) != 0)
                throw new InvalidDataException("Índice de evidencias incompatible.");
        }
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT asset_id,png_sha256,historical_sha256,evidence_kind,source_relative_path,size_bytes,width,height,git_commit FROM masters ORDER BY asset_id";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            rows.Add(new(reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.GetInt64(5), reader.GetInt32(6),
                reader.GetInt32(7), reader.IsDBNull(8) ? null : reader.GetString(8)));
        return rows.AsReadOnly();
    }
    private static SqliteConnection Connect(string file, SqliteOpenMode mode)
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = file, Mode = mode, Cache = SqliteCacheMode.Private, Pooling = false }.ToString());
        db.Open(); return db;
    }
    private static void Exec(SqliteConnection db, SqliteTransaction tx, string sql, params (string Name, object Value)[] values)
    {
        using var cmd = db.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = sql;
        foreach (var (name, value) in values) cmd.Parameters.AddWithValue(name, value);
        cmd.ExecuteNonQuery();
    }
}

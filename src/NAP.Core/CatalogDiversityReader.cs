using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace NAP.Core;

/// <summary>Read-only, universe-scoped projection. Uses existing SQLite catalog validation and file safety.</summary>
public sealed class CatalogDiversityReader(UniverseContext context)
{
    public CatalogDiversityReport Read(CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellation.ThrowIfCancellationRequested();
        var native = Array.Empty<CatalogDiversityEntry>();
        var history = Array.Empty<CatalogDiversityEntry>();
        // A missing catalog is not created just to answer a question.
        if (File.Exists(context.Storage.CatalogPath))
            native = new CatalogExplorerReader(context).DiversityEntries(cancellation).ToArray();
        if (context.Id.Value == "nimroel")
        {
            var index = new LegacyPortraitIndex(context);
            if (File.Exists(index.DatabasePath))
                history = index.DiversityEntries(cancellation).ToArray();
        }
        cancellation.ThrowIfCancellationRequested();
        return CatalogDiversityAnalyzer.Analyze(context.Id, native.Concat(history));
    }
}

/// <summary>Internal bounded index projection; never returns stored prompts or per-asset documents.</summary>
internal static class CatalogDiversitySql
{
    internal static IReadOnlyList<CatalogDiversityEntry> Select(SqliteConnection connection, UniverseId universe, CancellationToken cancellation)
    {
        var rows = new Dictionary<string, (string Type, string Profile, Dictionary<string, string> Classes,
            List<CatalogVisualTrait> Traits)>(StringComparer.Ordinal);
        using (var cmd = CatalogSql.Command(connection, null,
            "SELECT asset_id,asset_type,production_profile FROM assets WHERE universe_id=$u ORDER BY asset_id COLLATE BINARY", "$u", universe.Value))
        using (var reader = cmd.ExecuteReader())
            while (reader.Read())
            {
                cancellation.ThrowIfCancellationRequested();
                if (rows.Count >= 50_000) throw new InvalidDataException("Catálogo demasiado grande para la auditoría de diversidad.");
                rows.Add(reader.GetString(0), (reader.GetString(1), reader.GetString(2), new Dictionary<string, string>(StringComparer.Ordinal), new List<CatalogVisualTrait>()));
            }

        using (var cmd = CatalogSql.Command(connection, null,
            "SELECT asset_id,dimension,value FROM classifications WHERE universe_id=$u ORDER BY asset_id,dimension COLLATE BINARY", "$u", universe.Value))
        using (var reader = cmd.ExecuteReader())
            while (reader.Read())
            {
                cancellation.ThrowIfCancellationRequested();
                var id = reader.GetString(0);
                if (!rows.TryGetValue(id, out var row)) throw new InvalidDataException("Clasificación sin asset.");
                row.Classes.Add(reader.GetString(1), reader.GetString(2));
            }

        using (var cmd = CatalogSql.Command(connection, null,
            "SELECT asset_id,trait_key,trait_value,scalar_type FROM visual_traits WHERE universe_id=$u ORDER BY asset_id,trait_key COLLATE BINARY", "$u", universe.Value))
        using (var reader = cmd.ExecuteReader())
            while (reader.Read())
            {
                cancellation.ThrowIfCancellationRequested();
                var id = reader.GetString(0);
                if (!rows.TryGetValue(id, out var row)) throw new InvalidDataException("Rasgo sin asset.");
                row.Traits.Add(new(reader.GetString(1), reader.GetString(2), Enum.Parse<CatalogTraitType>(reader.GetString(3))));
            }

        return Array.AsReadOnly(rows.Select(kv => new CatalogDiversityEntry(kv.Key, kv.Value.Type, kv.Value.Profile,
            kv.Value.Classes, kv.Value.Traits.AsReadOnly(), false)).OrderBy(a => a.AssetId, StringComparer.Ordinal).ToArray());
    }
}

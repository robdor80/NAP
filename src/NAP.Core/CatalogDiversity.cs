using System.Collections.ObjectModel;
using System.Text.Json;

namespace NAP.Core;

/// <summary>Aggregate facts from verified catalog rows only; never a visual assessment or a population census.</summary>
public sealed record CatalogDiversityCount(string Id, string Dimension, string Value, long Count, long ClassifiedAssets)
{
    public string Display => Dimension + " · " + Value + ": " + Count.ToString(System.Globalization.CultureInfo.InvariantCulture) +
        " / " + ClassifiedAssets.ToString(System.Globalization.CultureInfo.InvariantCulture) + " clasificados";
}

/// <summary>Cross-tabulated combinations, counted per asset (not inferred from independent margins).</summary>
public sealed record CatalogDiversityCross(string Id, string FirstDimension, string FirstValue,
    string SecondDimension, string SecondValue, long Count)
{
    public string Display => FirstDimension + "=" + FirstValue + " · " + SecondDimension + "=" + SecondValue + ": " +
        Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Purely relative coverage gap, not a staffing quota or a judgment that assets are excessive.</summary>
public sealed record CatalogDiversityOpportunity(string Dimension, string Value, long Count, string ComparedTo,
    long ComparedCount, string Explanation);

public sealed class CatalogDiversitySnapshot
{
    public CatalogDiversitySnapshot(UniverseId universeId, long totalAssets,
        IEnumerable<CatalogDiversityCount> counts, IEnumerable<CatalogDiversityCross> crosses,
        IEnumerable<CatalogDiversityOpportunity> opportunities)
    {
        ArgumentNullException.ThrowIfNull(universeId);
        if (totalAssets < 0) throw new ArgumentOutOfRangeException(nameof(totalAssets));
        ArgumentNullException.ThrowIfNull(counts);
        ArgumentNullException.ThrowIfNull(crosses);
        ArgumentNullException.ThrowIfNull(opportunities);
        UniverseId = universeId; TotalAssets = totalAssets;
        Counts = Array.AsReadOnly(counts.ToArray());
        Crosses = Array.AsReadOnly(crosses.ToArray());
        Opportunities = Array.AsReadOnly(opportunities.ToArray());
        if (Counts.Select(c => c.Id).Concat(Crosses.Select(c => c.Id)).Distinct(StringComparer.Ordinal).Count() != Counts.Count + Crosses.Count)
            throw new ArgumentException("Duplicate evidence ID.");
        Evidence = new ReadOnlyDictionary<string, string>(
            Counts.Select(c => new KeyValuePair<string, string>(c.Id, c.Display))
                .Concat(Crosses.Select(c => new KeyValuePair<string, string>(c.Id, c.Display)))
                .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal));
    }

    public UniverseId UniverseId { get; }
    public long TotalAssets { get; }
    public IReadOnlyList<CatalogDiversityCount> Counts { get; }
    public IReadOnlyList<CatalogDiversityCross> Crosses { get; }
    public IReadOnlyList<CatalogDiversityOpportunity> Opportunities { get; }
    public IReadOnlyDictionary<string, string> Evidence { get; }

    /// <summary>Bounded aggregate JSON; never includes per-asset IDs, documents, paths, SQL or master bytes.</summary>
    public string ToJson()
    {
        var json = JsonSerializer.Serialize(new
        {
            schema_version = 1,
            universe_id = UniverseId.Value,
            total_verified_assets = TotalAssets,
            counts = Counts.Select(c => new { id = c.Id, dimension = c.Dimension, value = c.Value,
                count = c.Count, assets_with_dimension = c.ClassifiedAssets }),
            crosses = Crosses.Select(c => new { id = c.Id, first_dimension = c.FirstDimension, first_value = c.FirstValue,
                second_dimension = c.SecondDimension, second_value = c.SecondValue, count = c.Count }),
            relative_opportunities = Opportunities.Select(o => new { dimension = o.Dimension, value = o.Value,
                count = o.Count, compared_to = o.ComparedTo, comparison_count = o.ComparedCount }),
            limitations = new[] { "Only registered verified assets are counted.",
                "Unrecorded categories and missing JSON fields cannot be interpreted as zero.",
                "Differences are relative visual-asset variety, not required NPC population or production quotas." }
        });
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 512 * 1024)
            throw new InvalidOperationException("Aggregate catalog report exceeds the bounded AI evidence payload; narrow the catalog before querying.");
        return json;
    }
}

public sealed record CatalogInsightAnswer(string Answer, IReadOnlyList<string> Evidence)
{
    public string EvidenceText => Evidence.Count == 0 ? "Sin referencias cuantitativas seleccionadas." :
        string.Join(Environment.NewLine, Evidence);
}

public interface ICatalogInsightClient
{
    Task<CatalogInsightAnswer> AskAsync(CatalogDiversitySnapshot snapshot, string question,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Read-only scoped SQLite aggregation. Does not open master images, deserialize companion documents,
/// modify the catalog, issue arbitrary user SQL or invoke AI.
/// </summary>
public sealed class CatalogDiversityReader(UniverseContext context)
{
    public const int MaxAssets = 50000;
    public CatalogDiversitySnapshot Read(CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        return new AssetCatalog(context).Read(connection =>
        {
            var assets = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            using (var command = CatalogSql.Command(connection, null,
                "SELECT asset_id,asset_type,production_profile FROM assets WHERE universe_id=$u ORDER BY asset_id COLLATE BINARY",
                "$u", context.Id.Value))
            using (var reader = command.ExecuteReader())
                while (reader.Read())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (assets.Count >= MaxAssets) throw new InvalidOperationException("The catalog exceeds the bounded diversity read limit; no partial audit was produced.");
                    var id = reader.GetString(0);
                    assets.Add(id, new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["asset_type"] = reader.GetString(1),
                        ["production_profile"] = reader.GetString(2)
                    });
                }

            using (var command = CatalogSql.Command(connection, null,
                "SELECT asset_id,dimension,value FROM classifications WHERE universe_id=$u ORDER BY asset_id,dimension",
                "$u", context.Id.Value))
            using (var reader = command.ExecuteReader())
                while (reader.Read())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!assets.TryGetValue(reader.GetString(0), out var fields))
                        throw new InvalidDataException("Orphaned catalog classification.");
                    fields.Add("classification:" + reader.GetString(1), reader.GetString(2));
                }

            using (var command = CatalogSql.Command(connection, null,
                "SELECT asset_id,trait_key,trait_value FROM visual_traits WHERE universe_id=$u AND scalar_type='String' ORDER BY asset_id,trait_key",
                "$u", context.Id.Value))
            using (var reader = command.ExecuteReader())
                while (reader.Read())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!assets.TryGetValue(reader.GetString(0), out var fields))
                        throw new InvalidDataException("Orphaned catalog visual trait.");
                    fields.Add("visual:" + reader.GetString(1), reader.GetString(2));
                }

            return Aggregate(context.Id, assets.Values);
        });
    }

    /// <summary>Pure deterministic aggregation used by SQLite and synthetic tests alike.</summary>
    public static CatalogDiversitySnapshot Aggregate(UniverseId universeId,
        IEnumerable<IReadOnlyDictionary<string, string>> assets)
    {
        ArgumentNullException.ThrowIfNull(universeId);
        ArgumentNullException.ThrowIfNull(assets);
        var grouped = new Dictionary<(string Dimension, string Value), long>();
        var coverage = new Dictionary<string, long>(StringComparer.Ordinal);
        var cross = new Dictionary<(string A, string AV, string B, string BV), long>();
        long total = 0;
        foreach (var source in assets)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (++total > MaxAssets) throw new InvalidOperationException("Diversity aggregation exceeds the bounded asset limit.");
            var facts = source.Where(p => !string.IsNullOrWhiteSpace(p.Key) && !string.IsNullOrWhiteSpace(p.Value))
                .OrderBy(p => p.Key, StringComparer.Ordinal).ToArray();
            foreach (var (dimension, value) in facts)
            {
                var key = (dimension, value);
                grouped[key] = grouped.GetValueOrDefault(key) + 1;
                coverage[dimension] = coverage.GetValueOrDefault(dimension) + 1;
            }

            // Cross only meaningful categorical dimensions; no products of unrelated JSON free-text fields.
            var axes = facts.Where(f => CrossAxis(f.Key)).Take(24).ToArray();
            for (var i = 0; i < axes.Length; i++)
            for (var j = i + 1; j < axes.Length; j++)
            {
                var a = axes[i]; var b = axes[j];
                var key = (a.Key, a.Value, b.Key, b.Value);
                cross[key] = cross.GetValueOrDefault(key) + 1;
            }
            if (grouped.Count > 12000 || cross.Count > 12000)
                throw new InvalidOperationException("Catalog diversity has too many categories to report safely; no incomplete report was produced.");
        }

        var counts = grouped.OrderBy(g => g.Key.Dimension, StringComparer.Ordinal)
            .ThenBy(g => g.Key.Value, StringComparer.Ordinal)
            .Select((g, i) => new CatalogDiversityCount("d" + (i + 1).ToString("D5", System.Globalization.CultureInfo.InvariantCulture),
                g.Key.Dimension, g.Key.Value, g.Value, coverage[g.Key.Dimension])).ToArray();
        var crosses = cross.OrderBy(g => g.Key.A, StringComparer.Ordinal).ThenBy(g => g.Key.AV, StringComparer.Ordinal)
            .ThenBy(g => g.Key.B, StringComparer.Ordinal).ThenBy(g => g.Key.BV, StringComparer.Ordinal)
            .Select((g, i) => new CatalogDiversityCross("c" + (i + 1).ToString("D5", System.Globalization.CultureInfo.InvariantCulture),
                g.Key.A, g.Key.AV, g.Key.B, g.Key.BV, g.Value)).ToArray();
        var opportunities = new List<CatalogDiversityOpportunity>();
        foreach (var dimension in counts.GroupBy(c => c.Dimension, StringComparer.Ordinal))
        {
            var candidates = dimension.OrderByDescending(c => c.Count).ThenBy(c => c.Value, StringComparer.Ordinal).ToArray();
            if (candidates.Length < 2 || candidates[0].Count < 8) continue;
            foreach (var less in candidates.Skip(1).Where(c => c.Count * 4 <= candidates[0].Count))
                opportunities.Add(new(less.Dimension, less.Value, less.Count, candidates[0].Value,
                    candidates[0].Count, "Representación comparativamente baja entre las categorías registradas; no implica una cuota."));
        }
        return new(universeId, total, counts, crosses,
            opportunities.OrderBy(o => o.Dimension, StringComparer.Ordinal).ThenBy(o => o.Count)
                .ThenBy(o => o.Value, StringComparer.Ordinal).Take(200).ToArray());
    }

    private static bool CrossAxis(string key)
    {
        if (key is "asset_type" or "production_profile") return true;
        var name = key[(key.LastIndexOfAny([':', '/']) + 1)..];
        return name is "culture" or "realm" or "region" or "location" or "role" or "profession"
            or "occupation" or "job" or "sex" or "gender" or "age_group" or "age_range"
            or "apparent_age" or "life_stage" or "season" or "weather" or "time_of_day"
            or "environment" or "building_type" or "scene_type";
    }
}

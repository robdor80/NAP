using System.Collections.ObjectModel;
using System.Text.Json;

namespace NAP.Core;

/// <summary>A single indexed asset; no binary, prompt, document, path, or original JSON is exported.</summary>
public sealed record CatalogDiversityEntry(string AssetId, string AssetType, string ProductionProfile,
    IReadOnlyDictionary<string, string> Classification, IReadOnlyList<CatalogVisualTrait> Traits, bool Historical);

/// <summary>Counts originate exclusively from rows read from the universe-scoped catalog(s).</summary>
public sealed record CatalogDiversityEvidence(string Id, string Dimension, string Value, long Count, long Population)
{
    public string Display => $"{Dimension}: {Value} — {Count:N0} de {Population:N0}";
}

public sealed record CatalogDiversityGap(string Dimension, string Value, long Count,
    string ReferenceValue, long ReferenceCount)
{
    public string Display => $"{Dimension}: {Value} ({Count:N0}), frente a {ReferenceValue} ({ReferenceCount:N0}).";
}

public sealed class CatalogDiversityReport
{
    internal CatalogDiversityReport(UniverseId universe, long native, long historical,
        IEnumerable<CatalogDiversityEvidence> evidence, IEnumerable<CatalogDiversityGap> gaps,
        IEnumerable<string> warnings)
    {
        UniverseId = universe;
        NativeAssets = native; HistoricalAssets = historical;
        Evidence = Array.AsReadOnly(evidence.ToArray());
        Gaps = Array.AsReadOnly(gaps.ToArray());
        Warnings = Array.AsReadOnly(warnings.ToArray());
    }
    public UniverseId UniverseId { get; }
    public long NativeAssets { get; }
    public long HistoricalAssets { get; }
    public long TotalAssets => NativeAssets + HistoricalAssets;
    public IReadOnlyList<CatalogDiversityEvidence> Evidence { get; }
    public IReadOnlyList<CatalogDiversityGap> Gaps { get; }
    public IReadOnlyList<string> Warnings { get; }
    /// <summary>Deterministic, bounded aggregation, not a dump of private SQLite rows.</summary>
    public string ToJson() => JsonSerializer.Serialize(new
    {
        schema_version = 1, universe_id = UniverseId.Value, native_assets = NativeAssets,
        historical_assets = HistoricalAssets, total_assets = TotalAssets,
        evidence = Evidence.Select(e => new { id = e.Id, dimension = e.Dimension, value = e.Value, count = e.Count, population = e.Population }),
        observed_gaps = Gaps.Select(g => new { dimension = g.Dimension, value = g.Value, count = g.Count, comparison_value = g.ReferenceValue, comparison_count = g.ReferenceCount }),
        limitations = Warnings
    });
}

public sealed record CatalogDiversityObservation(string Comment, IReadOnlyList<CatalogDiversityEvidence> Evidence)
{
    public string EvidenceLabel => string.Join(" · ", Evidence.Select(e => e.Display));
}
public sealed record CatalogDiversityAnswer(string Summary, IReadOnlyList<CatalogDiversityObservation> Observations);

/// <summary>No SQL, file mutations, model inference, arbitrary target quotas, or inferred zero-population categories.</summary>
public static class CatalogDiversityAnalyzer
{
    private static readonly (string Left, string Right)[] Crosses =
    [
        ("profesion", "sexo"), ("profesion", "edad"), ("sexo", "edad"),
        ("cultura", "profesion"), ("localidad", "profesion"), ("cultura", "sexo"),
        ("localidad", "sexo"), ("tipo", "perfil")
    ];
    private static readonly string[] Standard = ["tipo", "perfil", "profesion", "sexo", "edad", "cultura", "reino", "region", "localidad"];
    // Explicitly approved display-only values. Arbitrary JSON strings can contain paths,
    // credentials or personal data and must never be forwarded to an AI service.
    private static readonly HashSet<string> PublicTraits = new(StringComparer.Ordinal)
    {
        "/age/life_stage/value", "/age/apparent_age/value", "/eyes/color/value",
        "/hair/color/value", "/body/build/value", "/facial_hair/type/value",
        "/clothing/style/value", "/profession", "/occupation", "/role",
        "/sex", "/gender", "/age_group", "/age/life_stage", "/life_stage",
        "/scene/time_of_day", "/scene/weather", "/scene/season", "/scene/lighting",
        "/environment/weather", "/environment/season", "/environment/biome"
    };
    private const string Unknown = "(sin dato)";
    private const int MaxAssets = 50_000, MaxEvidence = 3_000;
    private const int MaxJsonCharacters = 350_000;

    public static CatalogDiversityReport Analyze(UniverseId universe, IEnumerable<CatalogDiversityEntry> input)
    {
        ArgumentNullException.ThrowIfNull(universe);
        ArgumentNullException.ThrowIfNull(input);
        var entries = input.ToArray();
        if (entries.Length > MaxAssets) throw new InvalidDataException("El catálogo excede el límite seguro de la auditoría; consulta un subconjunto.");
        if (entries.Any(e => e is null || e.Classification is null || e.Traits is null ||
            e.AssetType is null || e.ProductionProfile is null || e.AssetId is null))
            throw new ArgumentException("Invalid catalog diversity entry.", nameof(input));

        var nativeEntries = entries.Where(e => !e.Historical).ToArray();
        var legacyEntries = entries.Where(e => e.Historical).ToArray();
        if (nativeEntries.Select(e => e.AssetId).Distinct(StringComparer.Ordinal).Count() != nativeEntries.Length ||
            legacyEntries.Select(e => e.AssetId).Distinct(StringComparer.Ordinal).Count() != legacyEntries.Length)
            throw new InvalidDataException("Identidades duplicadas en una misma fuente de catálogo.");
        var nativeIds = new HashSet<string>(nativeEntries.Select(e => e.AssetId), StringComparer.Ordinal);
        // A native verified asset supersedes its own historical index projection if both are present.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var assets = entries.Where(e => (!e.Historical || !nativeIds.Contains(e.AssetId)) &&
            seen.Add(e.AssetId)).OrderBy(e => e.AssetId, StringComparer.Ordinal).ToArray();
        var facets = assets.Select(Facets).ToArray();
        var rows = new List<(string Dimension, string Value, long Count, long Population)>();
        var gaps = new List<CatalogDiversityGap>();
        foreach (var dimension in Standard)
        {
            var values = facets.Select(f => f[dimension]).ToArray();
            Add(dimension, values, assets.Length);
            if (dimension is "profesion" or "sexo" or "edad" or "cultura" or "localidad" or "perfil")
            {
                var available = values.Where(v => v != Unknown).GroupBy(v => v, StringComparer.Ordinal)
                    .Select(g => (Value: g.Key, Count: g.LongCount())).OrderByDescending(x => x.Count).ThenBy(x => x.Value, StringComparer.Ordinal).ToArray();
                if (available.Length > 1 && available[0].Count >= 6)
                    foreach (var small in available.Skip(1).Where(x => x.Count * 4 <= available[0].Count))
                        gaps.Add(new(dimension, small.Value, small.Count, available[0].Value, available[0].Count));
            }
        }

        // All scalar visual-identity keys are represented as their literal JSON Pointer,
        // never guessed to be a profession/age/sex when their schema differs.
        foreach (var dimension in assets.SelectMany(a => a.Traits.Where(t => PublicTraits.Contains(t.Key) && SafeTraitValue(t.Value)).Select(t => "rasgo:" + t.Key))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var key = dimension["rasgo:".Length..];
            Add(dimension, assets.Select(a =>
                a.Traits.FirstOrDefault(t => t.Key == key && SafeTraitValue(t.Value)) is { } t ? t.Value : Unknown).ToArray(), assets.Length);
        }

        foreach (var (left, right) in Crosses)
        {
            var pairs = facets.Where(f => f[left] != Unknown && f[right] != Unknown)
                .Select(f => (Left: f[left], Right: f[right])).ToArray();
            var total = pairs.LongLength;
            // Report all observed combinations and, for sex/age within professions, zeros
            // only among values actually observed globally. No imaginary professions.
            var valuesLeft = pairs.Select(p => p.Left).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var valuesRight = pairs.Select(p => p.Right).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (valuesLeft.Length * (long)valuesRight.Length > MaxEvidence)
                throw new InvalidDataException("Demasiadas combinaciones de categorías para una auditoría íntegra.");
            foreach (var l in valuesLeft)
            foreach (var v in valuesRight)
            {
                var count = pairs.LongCount(p => p.Left == l && p.Right == v);
                if (count != 0 || left == "profesion" && right is "sexo" or "edad")
                    rows.Add(($"{left} × {right}", $"{l} | {v}", count, total));
            }
        }

        if (rows.Count > MaxEvidence) throw new InvalidDataException("Auditoría demasiado extensa para enviar todos los recuentos sin truncamiento.");
        var ordered = rows.OrderBy(r => r.Dimension, StringComparer.Ordinal).ThenBy(r => r.Value, StringComparer.Ordinal).ToArray();
        var evidence = ordered.Select((r, i) => new CatalogDiversityEvidence("e" + (i + 1).ToString("D4", System.Globalization.CultureInfo.InvariantCulture),
            r.Dimension, r.Value, r.Count, r.Population)).ToArray();
        var warnings = new List<string>();
        if (assets.Length == 0) warnings.Add("No hay assets indexados en este universo; no se puede evaluar la diversidad.");
        foreach (var dim in new[] { "profesion", "sexo", "edad" })
        {
            var missing = facets.LongCount(f => f[dim] == Unknown);
            if (missing > 0) warnings.Add($"{missing} de {assets.Length} assets sin metadato de {dim}; no puede deducirse de la imagen.");
        }
        if (assets.Any(a => a.Historical))
            warnings.Add("Incluye retratos del índice histórico, diferenciados de publicaciones verificadas nuevas.");
        warnings.Add("No se infieren categorías jamás registradas ni se imponen cuotas demográficas. Una diferencia de representación es una sugerencia artística, no un exceso.");
        var report = new CatalogDiversityReport(universe, assets.LongCount(a => !a.Historical), assets.LongCount(a => a.Historical),
            evidence, gaps.OrderBy(g => g.Dimension, StringComparer.Ordinal).ThenBy(g => g.Count).ToArray(), warnings);
        if (report.ToJson().Length > MaxJsonCharacters)
            throw new InvalidDataException("El JSON agregado excede el límite seguro; no se envía un informe parcial a la IA.");
        return report;

        void Add(string dimension, IEnumerable<string> values, long population)
        {
            foreach (var group in values.GroupBy(v => v, StringComparer.Ordinal))
                rows.Add((dimension, group.Key, group.LongCount(), population));
        }
    }

    private static bool SafeTraitValue(string? value)
    {
        // No URLs, Windows/UNIX paths, email addresses, delimiters, control chars, tokens or freeform narratives.
        return value is { Length: > 0 and <= 80 } &&
            !value.Contains("..", StringComparison.Ordinal) &&
            value.All(c => char.IsLetterOrDigit(c) || c is ' ' or '_' or '-' or '.');
    }

    private static Dictionary<string, string> Facets(CatalogDiversityEntry asset)
    {
        string? Class(string key) => asset.Classification.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
        string? Trait(params string[] paths) => paths.Select(p => asset.Traits.FirstOrDefault(t => t.Key == p && t.Type == CatalogTraitType.String && SafeTraitValue(t.Value))?.Value)
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tipo"] = asset.AssetType,
            ["perfil"] = asset.ProductionProfile,
            ["profesion"] = Class("role") ?? Trait("/profession", "/occupation", "/role") ?? Unknown,
            ["sexo"] = Class("sex") ?? Trait("/sex", "/gender") ?? Unknown,
            ["edad"] = Trait("/age/life_stage/value", "/age_group", "/age/life_stage", "/life_stage") ?? Unknown,
            ["cultura"] = Class("culture") ?? Unknown,
            ["reino"] = Class("realm") ?? Unknown,
            ["region"] = Class("region") ?? Unknown,
            ["localidad"] = Class("location") ?? Unknown
        };
    }
}

public interface ICatalogDiversityAiClient
{
    Task<CatalogDiversityAnswer> AskAsync(CatalogDiversityReport report, string question, CancellationToken cancellation = default);
}

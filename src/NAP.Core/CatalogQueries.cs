using Microsoft.Data.Sqlite;

namespace NAP.Core;

internal static class CatalogQueries
{
    internal static IReadOnlyList<CatalogAssetSnapshot> Select(SqliteConnection connection, UniverseId universe, CatalogFilter filter)
    {
        var (where, values) = Predicate(universe, filter);
        var ids = new List<string>();
        using (var command = CatalogSql.Command(connection, null, "SELECT a.asset_id FROM assets a WHERE " + where, values))
        using (var reader = command.ExecuteReader()) while (reader.Read()) ids.Add(reader.GetString(0));
        return Array.AsReadOnly(ids.Order(StringComparer.Ordinal).Select(id => CatalogAssetData.Read(connection, null, universe, id)!).ToArray());
    }

    internal static (string Sql, object?[] Values) Predicate(UniverseId universe, CatalogFilter filter)
    {
        var predicates = new List<string> { "a.universe_id=$u" }; var parameters = new List<object?> { "$u", universe.Value };
        void Equal(string column, string? value)
        { if (value is null) return; var name = "$p" + parameters.Count; predicates.Add(column + "=" + name); parameters.Add(name); parameters.Add(value); }
        Equal("a.asset_id", filter.AssetId); Equal("a.asset_type", filter.AssetType); Equal("a.production_profile", filter.ProductionProfile);
        foreach (var (dimension, value) in filter.Classification.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var key = "$p" + parameters.Count; var val = key + "v";
            predicates.Add("EXISTS(SELECT 1 FROM classifications c WHERE c.universe_id=a.universe_id AND c.asset_id=a.asset_id AND c.dimension=" + key + " AND c.value=" + val + ")");
            parameters.AddRange([key, dimension, val, value]);
        }
        foreach (var trait in filter.Traits)
        {
            var key = "$p" + parameters.Count; var val = key + "v"; var type = key + "t";
            predicates.Add("EXISTS(SELECT 1 FROM visual_traits t WHERE t.universe_id=a.universe_id AND t.asset_id=a.asset_id AND t.trait_key=" + key + " AND t.trait_value=" + val + " AND t.scalar_type=" + type + ")");
            parameters.AddRange([key, trait.Key, val, trait.Value, type, trait.Type.ToString()]);
        }
        // Only internal, fixed column names and generated parameter names are concatenated. All caller values are bound.
        return (string.Join(" AND ", predicates), parameters.ToArray());
    }
    internal static CatalogStatistics Statistics(IReadOnlyList<CatalogAssetSnapshot> assets)
    {
        IReadOnlyList<CatalogDistribution> Group(IEnumerable<(string Key, string Value, CatalogTraitType? Type)> values) => Array.AsReadOnly(values.GroupBy(v => v)
            .Select(g => new CatalogDistribution(g.Key.Key, g.Key.Value, g.LongCount(), g.Key.Type))
            .OrderBy(d => d.Dimension, StringComparer.Ordinal).ThenBy(d => d.Value, StringComparer.Ordinal).ThenBy(d => d.TraitType).ToArray());
        return new(assets.Count, Group(assets.Select(a => ("asset_type", a.AssetType, (CatalogTraitType?)null))),
            Group(assets.Select(a => ("production_profile", a.ProductionProfile, (CatalogTraitType?)null))),
            Group(assets.SelectMany(a => a.Classification.Select(p => (p.Key, p.Value, (CatalogTraitType?)null)))),
            Group(assets.SelectMany(a => a.Traits.Select(t => (t.Key, t.Value, (CatalogTraitType?)t.Type)))));
    }
}

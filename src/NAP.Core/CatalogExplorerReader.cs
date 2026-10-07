using Microsoft.Data.Sqlite;

namespace NAP.Core;

/// <summary>Small index projection, not publication/recovery evidence. No documents or master bytes.</summary>
public sealed record CatalogAssetSummary(UniverseAssetKey AssetKey, string AssetType, string ProductionProfile,
    string ProductionRelativePath, Sha256Digest ProductionDigest, long ProductionSizeBytes);
public sealed record CatalogPage(long TotalCount, int Offset, IReadOnlyList<CatalogAssetSummary> Items);

/// <summary>Read-only UI queries over schema v1. Existing full integrity/publication APIs remain unchanged.</summary>
public sealed class CatalogExplorerReader(UniverseContext context)
{
    public CatalogAssetSnapshot? Detail(string assetId, CancellationToken cancellation = default) => Read(c =>
    {
        ArgumentNullException.ThrowIfNull(assetId);
        var asset = CatalogAssetData.Read(c, null, context.Id, assetId);
        if (asset is not null) CatalogAssetData.ValidateAsset(asset, context);
        return asset;
    }, cancellation);
    public CatalogPage Page(CatalogFilter? filter = null, int offset = 0, int limit = 60, CancellationToken cancellation = default)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (limit is < 1 or > 240) throw new ArgumentOutOfRangeException(nameof(limit));
        return Read(c =>
        {
            var (where, values) = CatalogQueries.Predicate(context.Id, filter ?? new());
            var total = Convert.ToInt64(CatalogSql.Scalar(c, null, "SELECT COUNT(*) FROM assets a WHERE " + where, values));
            var items = new List<CatalogAssetSummary>();
            using var command = CatalogSql.Command(c, null,
                "SELECT a.asset_id,a.asset_type,a.production_profile,a.production_sha256,a.production_size,a.production_directory,f.relative_path,f.sha256,f.size,f.verified " +
                "FROM assets a LEFT JOIN files f ON f.universe_id=a.universe_id AND f.asset_id=a.asset_id AND f.location='Production' AND f.kind='generated_webp' WHERE " +
                where + " ORDER BY a.asset_id COLLATE BINARY LIMIT $limit OFFSET $offset", [.. values, "$limit", limit, "$offset", offset]);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                cancellation.ThrowIfCancellationRequested();
                var id = reader.GetString(0); var type = reader.GetString(1); var profile = reader.GetString(2);
                if (reader.IsDBNull(6)) throw CatalogException.Stop(NapIssueCodes.CatalogIntegrityFailed, "A cataloged production output is absent.");
                var path = reader.GetString(6); var digest = new Sha256Digest(reader.GetString(3)); var size = reader.GetInt64(4);
                ProductionPaths.Resolve(context.Storage.ProductionRoot, path);
                if (!AssetNamingRules.MatchesAssetType(id, type) || !AssetNamingRules.IsValidMachineIdentifier(profile) || size <= 0 ||
                    reader.GetString(7) != digest.Hex || reader.GetInt64(8) != size || reader.GetInt64(9) != 1 ||
                    path != reader.GetString(5) + "/" + id + ".webp")
                    throw CatalogException.Stop(NapIssueCodes.CatalogIntegrityFailed, "Inconsistent production index projection.");
                items.Add(new(new(context.Id, id), type, profile, path, digest, size));
            }
            if (items.Select(i => i.AssetKey).Distinct().Count() != items.Count)
                throw CatalogException.Stop(NapIssueCodes.CatalogIntegrityFailed, "Ambiguous production output.");
            return new CatalogPage(total, offset, items.AsReadOnly());
        }, cancellation);
    }

    public CatalogStatistics Statistics(CatalogFilter? filter = null, CancellationToken cancellation = default) =>
        Read(c => Statistics(c, context.Id, filter ?? new(), cancellation), cancellation);

    public CatalogPlanningReadModel Planning(CancellationToken cancellation = default) => Read(c =>
    {
        var objectives = CatalogOperations.Objectives(c, context.Id).Select(o =>
        {
            var facts = Statistics(c, context.Id, o.Filter, cancellation);
            return new CatalogObjectiveProgress(o, new(facts.TotalAssets, o.TargetCount), facts);
        }).ToArray();
        var lookup = objectives.ToDictionary(o => o.Objective.Id, StringComparer.Ordinal);
        var campaigns = CatalogOperations.Campaigns(c, context.Id).Select(campaign => new CatalogCampaignProgress(campaign,
            Array.AsReadOnly(campaign.ObjectiveIds.Select(id => lookup[id]).ToArray()))).ToArray();
        return new CatalogPlanningReadModel(context.Id, Array.AsReadOnly(objectives), Array.AsReadOnly(campaigns));
    }, cancellation);

    internal static CatalogStatistics Statistics(SqliteConnection c, UniverseId universe, CatalogFilter filter, CancellationToken cancellation)
    {
        var (where, values) = CatalogQueries.Predicate(universe, filter);
        IReadOnlyList<CatalogDistribution> Group(string sql, bool typed = false)
        {
            var rows = new List<CatalogDistribution>();
            using var command = CatalogSql.Command(c, null, sql, values);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                cancellation.ThrowIfCancellationRequested();
                rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetInt64(2), typed ? Enum.Parse<CatalogTraitType>(reader.GetString(3)) : null));
            }
            return Array.AsReadOnly(rows.OrderBy(d => d.Dimension, StringComparer.Ordinal).ThenBy(d => d.Value, StringComparer.Ordinal).ThenBy(d => d.TraitType).ToArray());
        }
        var total = Convert.ToInt64(CatalogSql.Scalar(c, null, "SELECT COUNT(*) FROM assets a WHERE " + where, values));
        return new(total,
            Group("SELECT 'asset_type',a.asset_type,COUNT(*) FROM assets a WHERE " + where + " GROUP BY a.asset_type"),
            Group("SELECT 'production_profile',a.production_profile,COUNT(*) FROM assets a WHERE " + where + " GROUP BY a.production_profile"),
            Group("SELECT c.dimension,c.value,COUNT(*) FROM assets a JOIN classifications c ON c.universe_id=a.universe_id AND c.asset_id=a.asset_id WHERE " + where + " GROUP BY c.dimension,c.value"),
            Group("SELECT t.trait_key,t.trait_value,COUNT(*),t.scalar_type FROM assets a JOIN visual_traits t ON t.universe_id=a.universe_id AND t.asset_id=a.asset_id WHERE " + where + " GROUP BY t.trait_key,t.trait_value,t.scalar_type", true));
    }

    private T Read<T>(Func<SqliteConnection, T> query, CancellationToken cancellation) => AssetCatalog.Run(() =>
    {
        cancellation.ThrowIfCancellationRequested();
        using var lease = CatalogBoundary.Acquire(context);
        CatalogBoundary.Paths(context);
        if (!CatalogBoundary.Regular(context.Storage.CatalogPath)) throw CatalogException.Stop(NapIssueCodes.CatalogMissing, "The catalog does not exist.");
        using var connection = CatalogBoundary.Connect(context.Storage.CatalogPath, SqliteOpenMode.ReadOnly);
        CatalogSchema.Validate(connection, context.Id);
        if (!string.Equals((string?)CatalogSql.Scalar(connection, null, "PRAGMA journal_mode"), "delete", StringComparison.OrdinalIgnoreCase))
            throw CatalogException.Stop(NapIssueCodes.CatalogInvalid, "Schema v1 requires DELETE journal mode.");
        cancellation.ThrowIfCancellationRequested();
        return query(connection);
    });
}

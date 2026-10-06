using Microsoft.Data.Sqlite;

namespace NAP.Core;

public sealed record CatalogRebuildResult(int AssetCount, bool OperationalDataPreserved, bool PriorCatalogUnreadable);

/// <summary>Explicit replacement after complete preflight and temporary DB integrity checks. No asset writes or historical backups.</summary>
public sealed class CatalogRebuilder
{
    private readonly UniverseContext _context;
    public CatalogRebuilder(UniverseContext context) { ArgumentNullException.ThrowIfNull(context); _context = context; }
    public CatalogRebuildResult Rebuild() => AssetCatalog.Run(() =>
    {
        using var sources = CatalogSourceLease.Acquire(_context);
        var plan = new CatalogImporter(_context).Discover();
        if (plan.Issues.ShouldStop) throw new CatalogException(plan.Issues);
        using var lease = CatalogBoundary.Acquire(_context); CatalogBoundary.Paths(_context, createState: true);
        var exists = CatalogBoundary.Regular(_context.Storage.CatalogPath);
        CatalogPlanningReadModel? operational = null; var unreadable = false;
        IReadOnlyList<CatalogAssetSnapshot> previousAssets = [];
        if (exists)
        {
            try
            {
                using var previous = CatalogBoundary.Open(_context, false);
                operational = CatalogOperations.Planning(previous, _context.Id);
                previousAssets = CatalogQueries.Select(previous, _context.Id, new CatalogFilter());
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode is 11 or 26) { unreadable = true; }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 1)
            { operational = ReadOperationalFromDamagedCatalog(); unreadable = operational is null; }
            catch (CatalogException ex) when (ex.Issues.Issues.All(i => i.Code is NapIssueCodes.CatalogCorrupt or NapIssueCodes.CatalogIntegrityFailed or NapIssueCodes.CatalogInvalid))
            {
                // A readable schema v1 must preserve operational data even when derived tables are damaged.
                operational = ReadOperationalFromDamagedCatalog();
                unreadable = operational is null;
            }
        }
        var rebuilt = plan.Assets.Select(asset =>
        {
            var previous = previousAssets.SingleOrDefault(a => a.AssetKey == asset.AssetKey);
            return previous is not null && CatalogAssetData.Equivalent(previous, asset) ? CatalogAssetData.WithAudit(asset, previous.AuditState) : asset;
        }).ToArray();
        new AssetCatalog(_context).CreateAndPublish(rebuilt, lease, overwrite: exists, operational);
        return new CatalogRebuildResult(plan.Assets.Count, operational is not null, unreadable);
    });

    private CatalogPlanningReadModel? ReadOperationalFromDamagedCatalog()
    {
        try
        {
            using var connection = CatalogBoundary.Connect(_context.Storage.CatalogPath, SqliteOpenMode.ReadOnly);
            if (Convert.ToInt64(CatalogSql.Scalar(connection, null, "SELECT COUNT(*) FROM sqlite_master WHERE name NOT LIKE 'sqlite_%'")) == 0) return null;
            if ((string?)CatalogSql.Scalar(connection, null, "SELECT sql FROM sqlite_master WHERE name='catalog_metadata'") != CatalogSchema.Objects["catalog_metadata"] ||
                Convert.ToInt64(CatalogSql.Scalar(connection, null, "SELECT COUNT(*) FROM catalog_metadata")) != 1)
                throw CatalogException.Stop(NapIssueCodes.CatalogRebuildFailed, "Readable catalog metadata is not authoritative; operational preservation requires review.");
            using (var command = CatalogSql.Command(connection, null, "SELECT schema_version,universe_id FROM catalog_metadata WHERE singleton=1"))
            using (var reader = command.ExecuteReader())
            {
                if (!reader.Read()) throw CatalogException.Stop(NapIssueCodes.CatalogRebuildFailed, "Readable catalog has no authoritative metadata; operational preservation requires review.");
                if (reader.GetInt64(0) != CatalogSchema.Version) throw CatalogException.Stop(NapIssueCodes.CatalogSchemaUnsupported, "Rebuild cannot replace an unknown schema.");
                if (reader.GetString(1) != _context.Id.Value) throw CatalogException.Stop(NapIssueCodes.CatalogWrongUniverse, "Rebuild cannot replace another universe's catalog.");
            }
            foreach (var name in new[] { "objectives", "objective_classifications", "objective_traits", "campaigns", "campaign_objectives" })
            {
                if ((string?)CatalogSql.Scalar(connection, null, "SELECT sql FROM sqlite_master WHERE name=$n", "$n", name) != CatalogSchema.Objects[name])
                    throw CatalogException.Stop(NapIssueCodes.CatalogRebuildFailed, "Readable operational tables are incomplete; do not discard user-entered data.");
            }
            var objectives = CatalogOperations.Objectives(connection, _context.Id);
            var campaigns = CatalogOperations.Campaigns(connection, _context.Id);
            using (var command = CatalogSql.Command(connection, null, "PRAGMA foreign_key_check"))
            using (var reader = command.ExecuteReader()) while (reader.Read())
                if (reader.GetString(0) is "objectives" or "objective_classifications" or "objective_traits" or "campaigns" or "campaign_objectives")
                    throw CatalogException.Stop(NapIssueCodes.CatalogRebuildFailed, "Operational foreign keys are inconsistent; user-entered data requires review.");
            // Recompute coverage only in the replacement; source derived tables may be corrupt.
            return new(_context.Id, Array.AsReadOnly(objectives.Select(o => new CatalogObjectiveProgress(o, new(0, o.TargetCount), new(0, [], [], [], []))).ToArray()),
                Array.AsReadOnly(campaigns.Select(c => new CatalogCampaignProgress(c, [])).ToArray()));
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 11 or 26) { return null; }
        catch (SqliteException ex) { throw CatalogException.Stop(NapIssueCodes.CatalogRebuildFailed, "A readable damaged catalog cannot preserve operational data safely.", inner: ex); }
    }
}

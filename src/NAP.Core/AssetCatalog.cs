using Microsoft.Data.Sqlite;

namespace NAP.Core;

/// <summary>Universe-scoped operational index. Public entry points accept no DB path, connection or SQL.</summary>
public sealed class AssetCatalog
{
    private readonly UniverseContext _context;
    public AssetCatalog(UniverseContext context) { ArgumentNullException.ThrowIfNull(context); _context = context; }
    public string CatalogPath => _context.Storage.CatalogPath;
    public UniverseId UniverseId => _context.Id;
    public void Initialize() => Run(() =>
    {
        using var lease = CatalogBoundary.Acquire(_context);
        CatalogBoundary.Paths(_context, createState: true);
        if (CatalogBoundary.Regular(CatalogPath)) { using var existing = CatalogBoundary.Open(_context, false); return 0; }
        CreateAndPublish([], lease, overwrite: false); return 0;
    });
    public void CheckIntegrity() => Read(connection => { CatalogSchema.Integrity(connection); return 0; });

    public bool RegisterVerified(ValidatedAssetPackage package, ProcessingPlan processing, ArchiveMasterResult archive, ProductionAssetResult production, AiAuditReport? auditReport = null) => Run(() =>
    {
        if (auditReport is not null) ProductionAssetExecutor.RequirePass(auditReport);
        using var sources = CatalogSourceLease.Acquire(_context);
        var snapshot = new CatalogAssetReader(_context).FromVerified(package, processing, archive, production);
        if (auditReport is not null) snapshot = CatalogAssetData.WithAudit(snapshot, "PASS");
        return RegisterSnapshots([snapshot]) == 1;
    });

    internal int RegisterSnapshots(IReadOnlyList<CatalogAssetSnapshot> assets) => Run(() =>
    {
        using var lease = CatalogBoundary.Acquire(_context); CatalogBoundary.Paths(_context, createState: true);
        if (assets.Any(a => a.AssetKey.UniverseId != _context.Id)) throw CatalogException.Stop(NapIssueCodes.CatalogWrongUniverse, "Snapshots belong to another universe.");
        Revalidate(assets);
        if (!CatalogBoundary.Regular(CatalogPath)) { CreateAndPublish(assets, lease, overwrite: false); return assets.Count; }
        using var connection = CatalogBoundary.Open(_context, true);
        using var transaction = connection.BeginTransaction(); var inserted = 0;
        foreach (var asset in assets) if (CatalogAssetData.InsertOrVerify(connection, transaction, asset)) inserted++;
        Revalidate(assets); transaction.Commit(); return inserted;
    });

    public CatalogAssetSnapshot? Get(string assetId)
    { ArgumentNullException.ThrowIfNull(assetId); return Read(c => CatalogAssetData.Read(c, null, _context.Id, assetId)); }
    public IReadOnlyList<CatalogAssetSnapshot> Query(CatalogFilter? filter = null) => Read(c => CatalogQueries.Select(c, _context.Id, filter ?? new()));
    public CatalogStatistics Statistics(CatalogFilter? filter = null) => Read(c => CatalogQueries.Statistics(CatalogQueries.Select(c, _context.Id, filter ?? new())));
    public CatalogCoverage Coverage(CatalogFilter filter, long targetCount)
    { ArgumentNullException.ThrowIfNull(filter); if (targetCount <= 0) throw new ArgumentOutOfRangeException(nameof(targetCount)); return new(Query(filter).Count, targetCount); }

    public void SaveObjective(CatalogObjective objective)
    { ArgumentNullException.ThrowIfNull(objective); RequireUniverse(objective.UniverseId); Write(c => { CatalogOperations.SaveObjective(c, objective); return 0; }); }
    public void SaveCampaign(CatalogCampaign campaign)
    { ArgumentNullException.ThrowIfNull(campaign); RequireUniverse(campaign.UniverseId); Write(c => { CatalogOperations.SaveCampaign(c, campaign); return 0; }); }
    public CatalogPlanningReadModel Planning() => Read(c => CatalogOperations.Planning(c, _context.Id));

    internal T Read<T>(Func<SqliteConnection, T> operation) => Run(() =>
    { using var lease = CatalogBoundary.Acquire(_context); using var connection = CatalogBoundary.Open(_context, false); return operation(connection); });
    private T Write<T>(Func<SqliteConnection, T> operation) => Run(() =>
    { using var lease = CatalogBoundary.Acquire(_context); using var connection = CatalogBoundary.Open(_context, true); return operation(connection); });
    private void RequireUniverse(UniverseId universe)
    { if (universe != _context.Id) throw CatalogException.Stop(NapIssueCodes.CatalogWrongUniverse, "Operational data belongs to another universe."); }
    private void Revalidate(IReadOnlyList<CatalogAssetSnapshot> assets)
    {
        var reader = new CatalogAssetReader(_context);
        foreach (var asset in assets) if (!CatalogAssetData.Equivalent(asset, reader.Capture(asset.ProductionRelativeDirectory)))
            throw CatalogException.Stop(NapIssueCodes.CatalogSourceInvalid, "Physical sources changed after their frozen preflight.");
    }
    internal void CreateAndPublish(IReadOnlyList<CatalogAssetSnapshot> assets, ExecutionMutex lease, bool overwrite,
        CatalogPlanningReadModel? operational = null)
    {
        var temp = CatalogBoundary.Temporary(_context);
        using (var connection = CatalogBoundary.Connect(temp, SqliteOpenMode.ReadWrite))
        {
            CatalogSql.Execute(connection, null, "PRAGMA journal_mode=DELETE"); CatalogSchema.Create(connection, _context.Id);
            using (var transaction = connection.BeginTransaction())
            {
                foreach (var asset in assets) CatalogAssetData.InsertOrVerify(connection, transaction, asset);
                transaction.Commit();
            }
            if (operational is not null)
            {
                foreach (var objective in operational.Objectives) CatalogOperations.SaveObjective(connection, objective.Objective);
                foreach (var campaign in operational.Campaigns) CatalogOperations.SaveCampaign(connection, campaign.Campaign);
            }
            CatalogSchema.Validate(connection, _context.Id); CatalogAssetData.ValidateLogical(connection, _context); Revalidate(assets);
        }
        CatalogBoundary.Publish(_context, temp, overwrite, lease);
    }
    internal static T Run<T>(Func<T> operation)
    {
        try { return operation(); }
        catch (SqliteException ex) { throw CatalogSql.Error(ex); }
        catch (Exception ex) when (ex is ProductionStorageException or FormatException or ArgumentException or InvalidOperationException)
        { throw CatalogException.Stop(NapIssueCodes.CatalogInvalid, "Catalog invariants or controlled paths are invalid.", inner: ex); }
    }
}

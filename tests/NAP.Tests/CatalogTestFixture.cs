using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

internal sealed class CatalogTestFixture : IDisposable
{
    internal ProductionTestFixture Production { get; }
    internal UniverseContext Context { get; }
    internal AssetCatalog Catalog => new(Context);
    internal string Root => Production.Root;
    internal CatalogTestFixture(bool nimroel = false, string universe = "future_world", bool genericClassification = false)
    {
        Production = new(nimroel, universe); Context = Production.Context;
        if (genericClassification)
        {
            var rule = Context.Profile.AssetRules.Single();
            Context = new UniverseContext(new UniverseProfile(Context.Id, "Generic future universe", ["material", "planet"],
                [new UniverseAssetRule(rule.AssetType, rule.ProductionProfile, ["material", "planet"], [], rule.PackageFiles, rule.Routing, rule.Conversion),
                 new UniverseAssetRule("banner", "painted_banner", ["material", "planet"], [], rule.PackageFiles, rule.Routing, rule.Conversion)]), Context.Storage);
        }
    }
    internal (JobId Job, ValidatedAssetPackage Package, ProcessingPlan Plan, ArchiveMasterResult Archive, ProductionAssetResult Production) Publish(string id = "emblem_example_001",
        string identity = "{\"eye_color\":\"green-grey\",\"nested\":{\"future_trait\":true}}", bool automatic = false,
        Dictionary<string, string>? classification = null, string? assetType = null, string? productionProfile = null)
    {
        var source = Production.Package; var directory = Directory.CreateDirectory(Path.Combine(Root, "packages", id)).FullName;
        foreach (var rule in source.AssetRule.PackageFiles)
        {
            var target = Path.Combine(directory, rule.ResolveFileName(id));
            File.Copy(source.FilesByRole[rule.Role], target);
            if (target.EndsWith("_visual_identity.json", StringComparison.Ordinal)) File.WriteAllText(target, identity);
        }
        File.WriteAllText(Path.Combine(directory, id + "_manifest.json"), JsonSerializer.Serialize(source.Manifest with
            { AssetId = id, Classification = classification ?? source.Manifest.Classification, AssetType = assetType ?? source.Manifest.AssetType, ProductionProfile = productionProfile ?? source.Manifest.ProductionProfile }));
        var package = new PackageSemanticValidator().Validate(directory, Context).Package!; Assert.NotNull(package);
        var repository = new ProductionRepositoryValidator().Validate(Context).Repository!;
        var plan = new ProcessingPlanBuilder().Build(package, repository, new ProductionDestinationResolver().Resolve(package, repository));
        var archivePlan = new ArchiveMasterPlanner(Context).Plan(package, plan); var job = Production.Job();
        if (automatic)
        {
            var result = new AssetExecutionCoordinator(Context).Execute(job, package, plan, archivePlan, ProductionTestFixture.Pass, ProductionTestFixture.Budget);
            return (job, package, plan, result.Archive, result.Production);
        }
        new JobStateStore(Context).Transition(job, JobState.Audited);
        var archive = new ArchiveMasterExecutor(Context).Execute(archivePlan, ProductionTestFixture.Pass);
        var productionPlan = new ProductionAssetPlanner(Context).Plan(package, plan, archive, ProductionTestFixture.Budget, job);
        var production = new ProductionAssetExecutor(Context).Execute(productionPlan, ProductionTestFixture.Pass);
        return (job, package, plan, archive, production);
    }
    internal SqliteConnection Raw(bool foreignKeys = true)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Catalog.CatalogPath, Mode = SqliteOpenMode.ReadWrite,
            Pooling = false, ForeignKeys = foreignKeys, DefaultTimeout = 1 }.ToString()); connection.Open(); return connection;
    }
    internal static void Execute(SqliteConnection c, string sql, params (string Name, object Value)[] values)
    { using var command = c.CreateCommand(); command.CommandText = sql; foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value); command.ExecuteNonQuery(); }
    internal static object? Scalar(SqliteConnection c, string sql)
    { using var command = c.CreateCommand(); command.CommandText = sql; return command.ExecuteScalar(); }
    internal static void Stop(CatalogException error, string code) => Assert.Contains(error.Issues.Issues, i => i.Code == code && i.StopsProcessing && i.Severity == NapIssueSeverity.Error);
    internal static object? Invoke(object? target, string type, string method, params object[] args) => ProductionTestFixture.Invoke(target, type, method, args);
    internal static string Logical(CatalogAssetSnapshot asset) => JsonSerializer.Serialize(new { asset.AssetKey, asset.AssetType, asset.ProductionProfile,
        asset.Lifecycle, asset.ArchiveRelativeDirectory, asset.ProductionRelativeDirectory, asset.MasterDigest, asset.MasterSizeBytes,
        asset.ProductionDigest, asset.ProductionSizeBytes, Classification = asset.Classification.OrderBy(p => p.Key, StringComparer.Ordinal), asset.Files, asset.Traits,
        Documents = asset.Documents.Select(d => new { d.Role, Bytes = Convert.ToBase64String(d.ToArray()) }) });
    internal Dictionary<string, string> Sources() => ArchiveTestFixture.Snapshot(Context.Storage.ProductionRoot).Select(p => new KeyValuePair<string, string>("production/" + p.Key, p.Value))
        .Concat(ArchiveTestFixture.Snapshot(Context.Storage.ArchiveRoot).Select(p => new KeyValuePair<string, string>("archive/" + p.Key, p.Value))).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
    internal void AssertSources(Dictionary<string, string> expected) => Assert.Equal(expected.OrderBy(p => p.Key, StringComparer.Ordinal), Sources().OrderBy(p => p.Key, StringComparer.Ordinal));
    public void Dispose() => Production.Dispose();
}

using Microsoft.Data.Sqlite;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class CatalogBoundaryTests
{
    [Fact]
    public void BoundaryUsesOnlyScopedCatalogPathAndExplicitSqlitePolicy()
    {
        using var f = new CatalogTestFixture(); Assert.Equal(f.Context.Storage.CatalogPath, f.Catalog.CatalogPath);
        f.Catalog.Initialize(); f.Catalog.CheckIntegrity(); Assert.True(File.Exists(f.Catalog.CatalogPath));
        using var c = (SqliteConnection)CatalogTestFixture.Invoke(null, "CatalogBoundary", "Open", f.Context, false)!;
        Assert.Equal(1L, CatalogTestFixture.Scalar(c, "PRAGMA foreign_keys")); Assert.Equal(2L, CatalogTestFixture.Scalar(c, "PRAGMA synchronous"));
        Assert.Equal("delete", CatalogTestFixture.Scalar(c, "PRAGMA journal_mode")); Assert.False(new SqliteConnectionStringBuilder(c.ConnectionString).Pooling);
        Assert.Equal(11L, CatalogTestFixture.Scalar(c, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'"));
        Assert.Equal(5L, CatalogTestFixture.Scalar(c, "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name LIKE 'ix_%'"));
        Assert.Equal(1L, CatalogTestFixture.Scalar(c, "SELECT schema_version FROM catalog_metadata"));
        Assert.Equal(f.Context.Id.Value, CatalogTestFixture.Scalar(c, "SELECT universe_id FROM catalog_metadata"));
        Assert.DoesNotContain(typeof(AssetCatalog).GetConstructors().SelectMany(c => c.GetParameters()), p => p.ParameterType == typeof(string));
        Assert.DoesNotContain(typeof(AssetCatalog).GetMethods(), m => m.GetParameters().Any(p => p.ParameterType == typeof(SqliteConnection)));
        Assert.DoesNotContain(typeof(AssetCatalog).GetMethods(), m => m.Name == "Query" && m.GetParameters().Any(p => p.ParameterType == typeof(string)));
    }
    [Fact]
    public void MissingCatalogReadDoesNotCreateOrRepairAnything()
    {
        using var f = new CatalogTestFixture(); var before = ArchiveTestFixture.Snapshot(f.Root);
        CatalogTestFixture.Stop(Assert.Throws<CatalogException>(() => f.Catalog.Query()), NapIssueCodes.CatalogMissing);
        ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }
    [Theory]
    [InlineData("bytes")] [InlineData("header")] [InlineData("truncated")] [InlineData("empty")]
    public void CorruptCatalogIsRejectedWithoutRepair(string kind)
    {
        using var f = new CatalogTestFixture(); f.Catalog.Initialize();
        var valid = File.ReadAllBytes(f.Catalog.CatalogPath);
        File.WriteAllBytes(f.Catalog.CatalogPath, kind switch { "bytes" => Enumerable.Range(1, 256).Select(i => (byte)i).ToArray(), "header" => new byte[4096], "truncated" => valid[..64], _ => [] });
        var before = ArchiveTestFixture.Snapshot(f.Root);
        var error = Assert.Throws<CatalogException>(() => f.Catalog.CheckIntegrity()); Assert.True(error.Issues.ShouldStop);
        ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }
    [Theory]
    [InlineData("metadata_version", NapIssueCodes.CatalogSchemaUnsupported)] [InlineData("pragma_version", NapIssueCodes.CatalogSchemaUnsupported)]
    [InlineData("universe", NapIssueCodes.CatalogWrongUniverse)]
    public void UnknownVersionsAndOtherUniverseAreRejected(string mutation, string code)
    {
        using var f = new CatalogTestFixture(); f.Catalog.Initialize();
        using (var c = f.Raw()) CatalogTestFixture.Execute(c, mutation switch { "metadata_version" => "UPDATE catalog_metadata SET schema_version=2", "pragma_version" => "PRAGMA user_version=2", _ => "UPDATE catalog_metadata SET universe_id='other_world'" });
        var before = ArchiveTestFixture.Snapshot(f.Root); CatalogTestFixture.Stop(Assert.Throws<CatalogException>(() => f.Catalog.Initialize()), code);
        CatalogTestFixture.Stop(Assert.Throws<CatalogException>(() => new CatalogRebuilder(f.Context).Rebuild()), code); ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }
    [Theory]
    [InlineData("assets")] [InlineData("classifications")] [InlineData("files")] [InlineData("documents")] [InlineData("visual_traits")]
    [InlineData("objectives")] [InlineData("objective_classifications")] [InlineData("objective_traits")] [InlineData("campaigns")] [InlineData("campaign_objectives")]
    [InlineData("ix_assets_type_profile")] [InlineData("ix_assets_profile")] [InlineData("ix_classification_selection")] [InlineData("ix_trait_selection")] [InlineData("ix_campaign_objective")]
    public void MissingRequiredTableOrIndexStops(string name)
    {
        using var f = new CatalogTestFixture(); f.Catalog.Initialize();
        using (var c = f.Raw(false)) CatalogTestFixture.Execute(c, (name.StartsWith("ix_", StringComparison.Ordinal) ? "DROP INDEX " : "DROP TABLE ") + name);
        CatalogTestFixture.Stop(Assert.Throws<CatalogException>(() => f.Catalog.CheckIntegrity()), NapIssueCodes.CatalogInvalid);
    }
    [Fact]
    public void ForeignKeyInconsistencyIsExplicitStop()
    {
        using var f = new CatalogTestFixture(); f.Catalog.Initialize();
        using (var c = f.Raw(false)) CatalogTestFixture.Execute(c, "INSERT INTO classifications VALUES('future_world','missing','dimension','value')");
        CatalogTestFixture.Stop(Assert.Throws<CatalogException>(() => f.Catalog.CheckIntegrity()), NapIssueCodes.CatalogIntegrityFailed);
    }
    [Fact]
    public void ConstraintsRejectAnotherUniverseDuplicatesAndDanglingAssociations()
    {
        using var f = new CatalogTestFixture(); f.Catalog.Initialize(); using var c = f.Raw();
        Assert.Throws<SqliteException>(() => CatalogTestFixture.Execute(c, "INSERT INTO campaigns VALUES('wrong','x','X')"));
        CatalogTestFixture.Execute(c, "INSERT INTO campaigns VALUES('future_world','case','X'); INSERT INTO campaigns VALUES('future_world','CASE','Y')");
        Assert.Equal(2L, CatalogTestFixture.Scalar(c, "SELECT COUNT(*) FROM campaigns"));
        Assert.Throws<SqliteException>(() => CatalogTestFixture.Execute(c, "INSERT INTO campaigns VALUES('future_world','case','Z')"));
        Assert.Throws<SqliteException>(() => CatalogTestFixture.Execute(c, "INSERT INTO campaign_objectives VALUES('future_world','case','missing')"));
        Assert.Throws<SqliteException>(() => CatalogTestFixture.Execute(c, "INSERT INTO objectives VALUES('future_world','o','O',0,NULL,NULL,NULL)"));
    }
    [Fact]
    public void SameAssetIdAndSeparateCatalogsAreIsolatedAcrossUniverses()
    {
        using var a = new CatalogTestFixture(universe: "alpha"); using var b = new CatalogTestFixture(universe: "beta");
        var first = a.Publish(automatic: true); b.Publish(automatic: true);
        Assert.Equal(first.Package.AssetKey.AssetId, Assert.Single(b.Catalog.Query()).AssetKey.AssetId);
        Assert.Equal(new UniverseId("alpha"), Assert.Single(a.Catalog.Query()).AssetKey.UniverseId);
        Assert.Equal(new UniverseId("beta"), Assert.Single(b.Catalog.Query()).AssetKey.UniverseId);
        File.Copy(a.Catalog.CatalogPath, b.Catalog.CatalogPath, true);
        CatalogTestFixture.Stop(Assert.Throws<CatalogException>(() => b.Catalog.Query()), NapIssueCodes.CatalogWrongUniverse);
    }
}

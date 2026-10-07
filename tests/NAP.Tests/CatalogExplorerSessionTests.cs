using System.Reflection;
using Microsoft.Data.Sqlite;
using NAP.Core;
using NAP.Presentation;
using Xunit;

namespace NAP.Tests;

public sealed class CatalogExplorerSessionTests
{
    private static int Count(CatalogExplorerReader reader, string property = "StrongValidationCount") =>
        (int)typeof(CatalogExplorerReader).GetProperty(property, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(reader)!;
    private static CatalogExplorerReader ServiceReader(ExplorerService service) =>
        (CatalogExplorerReader)typeof(ExplorerService).GetField("_reader", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
    [Fact]
    public void NewSessionValidatesStronglyOnceAcrossPagesDetailAndAggregates()
    {
        using var f = new CatalogTestFixture(); f.Publish("emblem_example_001", automatic: true); f.Publish("emblem_example_002", automatic: true); f.Publish("emblem_example_003", automatic: true);
        var reader = new CatalogExplorerReader(f.Context); var filter = new CatalogFilter(); Assert.Equal(0, Count(reader));
        Assert.Single(reader.Page(filter, 0, 1).Items); Assert.Equal(1, Count(reader));
        Assert.Equal("emblem_example_002", reader.Page(filter, 1, 1).Items.Single().AssetKey.AssetId);
        Assert.Equal("emblem_example_003", reader.Page(filter, 2, 1).Items.Single().AssetKey.AssetId);
        Assert.NotNull(reader.Detail("emblem_example_001")); Assert.Equal(3, reader.Statistics().TotalAssets); Assert.Empty(reader.Planning().Objectives);
        Assert.Equal(1, Count(reader)); Assert.Equal(1, Count(reader, "CountQueryCount"));
        Assert.Empty(reader.Page(filter, 3, 1).Items); Assert.Equal(1, Count(reader));
        reader.InvalidateValidation(); reader.Page(filter); Assert.Equal(2, Count(reader)); Assert.Equal(2, Count(reader, "CountQueryCount"));
    }
    [Fact]
    public async Task AdapterActuallyReusesTheSessionAndReopensAfterExplicitBegin()
    {
        using var f = new CatalogTestFixture(); f.Publish(automatic: true); var service = new ExplorerService(); var filter = new CatalogFilter();
        await service.BeginSessionAsync(f.Context, default); await service.PageAsync(f.Context, filter, 0, 60, default);
        var reader = ServiceReader(service); await service.PageAsync(f.Context, filter, 1, 60, default); await service.StatisticsAsync(f.Context, filter, default); await service.PlanningAsync(f.Context, default);
        Assert.Same(reader, ServiceReader(service)); Assert.Equal(1, Count(reader));
        await service.BeginSessionAsync(f.Context, default); await service.PageAsync(f.Context, filter, 0, 60, default); Assert.Equal(2, Count(reader));
    }
    [Fact]
    public void FilterChangesAndRandomOrRepeatedPagesKeepTheCorrectCountAndOrder()
    {
        using var f = new CatalogTestFixture();
        foreach (var n in new[] { 3, 1, 2 }) f.Publish($"emblem_example_00{n}", automatic: true);
        var reader = new CatalogExplorerReader(f.Context);
        var filter = new CatalogFilter(assetType: "emblem", productionProfile: "painted_icon", traits: [new("/nested/future_trait", "true", CatalogTraitType.Boolean)]);
        Assert.Equal("emblem_example_003", reader.Page(filter, 2, 1).Items.Single().AssetKey.AssetId);
        Assert.Equal("emblem_example_001", reader.Page(filter, 0, 1).Items.Single().AssetKey.AssetId);
        Assert.Equal("emblem_example_002", reader.Page(filter, 1, 1).Items.Single().AssetKey.AssetId);
        Assert.Equal("emblem_example_002", reader.Page(filter, 1, 1).Items.Single().AssetKey.AssetId);
        Assert.Equal(3, reader.Page(filter, 2, 1).TotalCount); Assert.Equal(1, Count(reader, "CountQueryCount"));
        var absent = new CatalogFilter(assetType: "absent"); Assert.Equal(0, reader.Page(absent).TotalCount);
        Assert.Empty(reader.Page(absent, 1, 1).Items); Assert.Equal(2, Count(reader, "CountQueryCount"));
        Assert.Equal(3, reader.Page(filter).Items.Count); Assert.Equal(3, Count(reader, "CountQueryCount")); Assert.Equal(1, Count(reader));
    }
    [Fact]
    public void RealCatalogWriteInvalidatesEvenWithRestoredModificationTime()
    {
        using var f = new CatalogTestFixture(); f.Catalog.Initialize(); var reader = new CatalogExplorerReader(f.Context); var filter = new CatalogFilter();
        Assert.Equal(0, reader.Page(filter).TotalCount); var time = File.GetLastWriteTimeUtc(f.Catalog.CatalogPath);
        f.Publish(automatic: true); File.SetLastWriteTimeUtc(f.Catalog.CatalogPath, time);
        Assert.Equal(1, reader.Page(filter).TotalCount); Assert.Equal(2, Count(reader)); Assert.Equal(2, Count(reader, "CountQueryCount"));
    }
    [Fact]
    public void AtomicReplacementInvalidatesEvenWithIdenticalBytesLengthAndWriteTime()
    {
        using var f = new CatalogTestFixture(); f.Catalog.Initialize(); var reader = new CatalogExplorerReader(f.Context); reader.Page();
        var path = f.Catalog.CatalogPath; var time = File.GetLastWriteTimeUtc(path); var creation = File.GetCreationTimeUtc(path); var copy = path + ".replacement";
        File.Copy(path, copy); File.SetLastWriteTimeUtc(copy, time); if (OperatingSystem.IsWindows()) File.SetCreationTimeUtc(copy, creation);
        File.Move(copy, path, true); Assert.Empty(reader.Page().Items); Assert.Equal(2, Count(reader));
    }
    [Fact]
    public void InPlaceRewriteOfSameBytesAndHeaderWithRestoredWriteTimeInvalidates()
    {
        using var f = new CatalogTestFixture(); f.Catalog.Initialize(); var reader = new CatalogExplorerReader(f.Context); reader.Page();
        var path = f.Catalog.CatalogPath; var time = File.GetLastWriteTimeUtc(path); var bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(path, bytes); File.SetLastWriteTimeUtc(path, time);
        Assert.Empty(reader.Page().Items); Assert.Equal(2, Count(reader));
    }
    [Fact]
    public void WarmSessionRejectsReplacementFromAnotherUniverse()
    {
        using var a = new CatalogTestFixture(universe: "alpha"); using var b = new CatalogTestFixture(universe: "beta"); a.Catalog.Initialize(); b.Catalog.Initialize();
        var reader = new CatalogExplorerReader(a.Context); reader.Page(); File.Copy(b.Catalog.CatalogPath, a.Catalog.CatalogPath, true);
        CatalogTestFixture.Stop(Assert.Throws<CatalogException>(() => reader.Page()), NapIssueCodes.CatalogWrongUniverse); Assert.Equal(2, Count(reader));
        File.Copy(b.Catalog.CatalogPath, a.Catalog.CatalogPath, true); Assert.Throws<CatalogException>(() => reader.Statistics()); Assert.Equal(3, Count(reader));
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void WarmSessionStillRejectsVersionOrSchemaChange(bool version)
    {
        using var f = new CatalogTestFixture(); f.Catalog.Initialize(); var reader = new CatalogExplorerReader(f.Context); reader.Page();
        using (var c = f.Raw()) CatalogTestFixture.Execute(c, version ? "PRAGMA user_version=2" : "CREATE TABLE unexpected(value TEXT)");
        CatalogTestFixture.Stop(Assert.Throws<CatalogException>(() => reader.Page()), version ? NapIssueCodes.CatalogSchemaUnsupported : NapIssueCodes.CatalogInvalid);
        Assert.Equal(2, Count(reader));
    }
    private static void ForeignKeyCorruption(CatalogTestFixture f)
    {
        using var c = f.Raw(foreignKeys: false);
        CatalogTestFixture.Execute(c, "INSERT INTO objective_classifications VALUES($u,'missing_objective','material','steel')", ("$u", f.Context.Id.Value));
    }
    [Fact]
    public void ForeignKeyCorruptionIsStrongValidationStopOnNewAndChangedSession()
    {
        using var f = new CatalogTestFixture(); f.Catalog.Initialize(); var warm = new CatalogExplorerReader(f.Context); warm.Page(); ForeignKeyCorruption(f);
        CatalogTestFixture.Stop(Assert.Throws<CatalogException>(() => warm.Page()), NapIssueCodes.CatalogIntegrityFailed); Assert.Equal(2, Count(warm));
        var fresh = new CatalogExplorerReader(f.Context); CatalogTestFixture.Stop(Assert.Throws<CatalogException>(() => fresh.Page()), NapIssueCodes.CatalogIntegrityFailed); Assert.Equal(1, Count(fresh));
    }
    [Fact]
    public void StrongValidationDetectsPhysicalBtreeCorruption()
    {
        using var f = new CatalogTestFixture(); f.Catalog.Initialize(); var reader = new CatalogExplorerReader(f.Context); reader.Page();
        long rootPage;
        using (var c = f.Raw()) rootPage = Convert.ToInt64(CatalogTestFixture.Scalar(c, "SELECT rootpage FROM sqlite_master WHERE name='assets'"));
        var bytes = File.ReadAllBytes(f.Catalog.CatalogPath); var size = (bytes[16] << 8) | bytes[17]; if (size == 1) size = 65536;
        bytes[checked((int)(rootPage - 1) * size)] = 0xff; File.WriteAllBytes(f.Catalog.CatalogPath, bytes);
        // Identity/schema still pass; the full integrity check must reach the damaged assets B-tree.
        var schema = typeof(AssetCatalog).Assembly.GetType("NAP.Core.CatalogSchema")!;
        using (var c = f.Raw())
        {
            schema.GetMethod("ValidateContract", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [c, f.Context.Id]);
            var invoked = Assert.Throws<TargetInvocationException>(() => schema.GetMethod("Validate", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [c, f.Context.Id]));
            Assert.True(Assert.IsType<CatalogException>(invoked.InnerException).Issues.ShouldStop);
        }
        var error = Assert.Throws<CatalogException>(() => reader.Page()); Assert.True(error.Issues.ShouldStop); Assert.Equal(2, Count(reader));
        Assert.True(Assert.Throws<CatalogException>(() => f.Catalog.CheckIntegrity()).Issues.ShouldStop);
    }
    [Fact]
    public void PhaseTenValidateRetainsForeignKeyChecks()
    {
        using var f = new CatalogTestFixture(); f.Catalog.Initialize(); ForeignKeyCorruption(f);
        var schema = typeof(AssetCatalog).Assembly.GetType("NAP.Core.CatalogSchema")!;
        using var c = f.Raw();
        schema.GetMethod("ValidateContract", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [c, f.Context.Id]);
        var invoked = Assert.Throws<TargetInvocationException>(() => schema.GetMethod("Validate", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [c, f.Context.Id]));
        CatalogTestFixture.Stop(Assert.IsType<CatalogException>(invoked.InnerException), NapIssueCodes.CatalogIntegrityFailed);
        CatalogTestFixture.Stop(Assert.Throws<CatalogException>(() => f.Catalog.CheckIntegrity()), NapIssueCodes.CatalogIntegrityFailed);
        CatalogTestFixture.Stop(Assert.Throws<CatalogException>(() => f.Catalog.Initialize()), NapIssueCodes.CatalogIntegrityFailed);
        CatalogTestFixture.Stop(Assert.Throws<CatalogException>(() => f.Catalog.Query()), NapIssueCodes.CatalogIntegrityFailed);
    }
    private sealed class Clock : TimeProvider
    {
        public long Ticks { get; set; }
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Ticks;
    }
    [Fact]
    public void SessionHasBoundedLifetimeEvenWithoutFileChanges()
    {
        using var f = new CatalogTestFixture(); f.Catalog.Initialize(); var reader = new CatalogExplorerReader(f.Context); var clock = new Clock();
        typeof(CatalogExplorerReader).GetProperty("Clock", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(reader, clock);
        reader.Page(); clock.Ticks += TimeSpan.FromMinutes(4).Ticks; reader.Page(); Assert.Equal(1, Count(reader));
        clock.Ticks += TimeSpan.FromMinutes(1).Ticks; reader.Page(); Assert.Equal(2, Count(reader));
    }
}

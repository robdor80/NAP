using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class CatalogSafetyTests
{
    [Fact]
    public void ArchivePublicationTempsRemainNonAuthoritativeWithoutWeakeningOriginalPackageValidation()
    {
        using var f = new CatalogTestFixture(); var result = f.Publish(); var master = result.Archive.FilesVerified.Single(file => file.Role == "original");
        var temp = master.DestinationPath + "." + Guid.NewGuid().ToString("N") + ".tmp"; File.WriteAllBytes(temp, [1, 2, 3]);
        var archiveDirectory = Path.GetDirectoryName(master.DestinationPath)!;
        Assert.True(new PackageSemanticValidator().Validate(archiveDirectory, f.Context).Issues.ShouldStop);
        var before = f.Sources(); var importer = new CatalogImporter(f.Context); var plan = importer.Plan(); Assert.True(plan.Issues.IsClean);
        Assert.Equal(1, importer.Import(plan)); new CatalogRebuilder(f.Context).Rebuild(); f.AssertSources(before);
        Assert.DoesNotContain(Assert.Single(f.Catalog.Query()).Files, file => file.RelativePath.EndsWith(".tmp")); Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(temp));
        var packageTemp = result.Package.FilesByRole["original"] + "." + Guid.NewGuid().ToString("N") + ".tmp"; File.WriteAllBytes(packageTemp, [1, 2, 3]);
        Assert.True(new PackageSemanticValidator().Validate(result.Package.PackageRoot, f.Context).Issues.ShouldStop);
    }
    [Fact]
    public void ChangedOriginalPackageAfterVerifiedResultsCannotRegisterButExplicitImportStillUsesPhysicalArchive()
    {
        using var f = new CatalogTestFixture(); var result = f.Publish(); File.AppendAllText(result.Package.FilesByRole["prompt"], "changed");
        CatalogTestFixture.Stop(Assert.Throws<CatalogException>(() => f.Catalog.RegisterVerified(result.Package, result.Plan, result.Archive, result.Production)), NapIssueCodes.CatalogSourceInvalid);
        Assert.False(File.Exists(f.Catalog.CatalogPath)); var importer = new CatalogImporter(f.Context); Assert.Equal(1, importer.Import(importer.Plan()));
    }
    [Fact]
    public void WalArtifactsAreRejectedRatherThanSilentlyConvertedOrDiscarded()
    {
        using var f = new CatalogTestFixture(); f.Catalog.Initialize(); var path = f.Catalog.CatalogPath + "-wal"; File.WriteAllBytes(path, [1, 2, 3]);
        var before = ArchiveTestFixture.Snapshot(f.Root);
        CatalogTestFixture.Stop(Assert.Throws<CatalogException>(() => f.Catalog.CheckIntegrity()), NapIssueCodes.CatalogInvalid);
        CatalogTestFixture.Stop(Assert.Throws<CatalogException>(() => new CatalogRebuilder(f.Context).Rebuild()), NapIssueCodes.CatalogInvalid);
        ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }
    [Theory]
    [InlineData("state")] [InlineData("workspace")] [InlineData("catalog")] [InlineData("journal")] [InlineData("wal")]
    public void BoundaryRejectsRealReparsePathsWithoutFollowingThem(string kind)
    {
        using var f = new CatalogTestFixture(); var external = Directory.CreateDirectory(Path.Combine(f.Root, "external_catalog")).FullName;
        File.WriteAllText(Path.Combine(external, "sentinel"), "do not touch"); string link;
        if (kind == "state") { Directory.Delete(f.Context.Storage.StateRoot, true); link = f.Context.Storage.StateRoot; }
        else if (kind == "workspace")
        {
            var moved = f.Context.Storage.WorkspaceRoot + "_moved"; Directory.Move(f.Context.Storage.WorkspaceRoot, moved); link = f.Context.Storage.WorkspaceRoot;
        }
        else link = f.Catalog.CatalogPath + (kind == "journal" ? "-journal" : kind == "wal" ? "-wal" : "");
        ArchiveTestFixture.Junction(link, external);
        try
        {
            var before = ArchiveTestFixture.Snapshot(f.Root);
            CatalogTestFixture.Stop(Assert.Throws<CatalogException>(() => f.Catalog.Initialize()), NapIssueCodes.CatalogInvalid);
            ArchiveTestFixture.AssertSnapshot(before, f.Root); Assert.Equal("do not touch", File.ReadAllText(Path.Combine(external, "sentinel")));
        }
        finally { Directory.Delete(link); }
    }
    [Theory]
    [InlineData("../escape")] [InlineData("./asset")] [InlineData("icons//asset")] [InlineData("/home/asset")]
    [InlineData("C:/Users/asset")] [InlineData("\\\\server\\asset")] [InlineData("icons\\asset")]
    [InlineData("icons/CON/asset")] [InlineData("icons/LPT1.txt/asset")] [InlineData("icons/.git/asset")]
    public void PersistedAssetPathsCannotEscapeOrUseAmbiguousSegments(string relative)
    {
        using var f = new CatalogTestFixture(); f.Publish(automatic: true);
        using (var c = f.Raw()) CatalogTestFixture.Execute(c, "UPDATE assets SET archive_directory=$p,production_directory=$p", ("$p", relative));
        CatalogTestFixture.Stop(Assert.Throws<CatalogException>(() => f.Catalog.Get("emblem_example_001")), NapIssueCodes.CatalogInvalid);
    }
    [Fact]
    public void RealProductionAndArchiveLinksStopBeforeDbMutation()
    {
        using var f = new CatalogTestFixture(); var result = f.Publish(); var path = Path.GetDirectoryName(result.Production.FilesVerified[0].DestinationPath)!;
        var moved = path + "_moved"; Directory.Move(path, moved); ArchiveTestFixture.Junction(path, moved);
        try { Assert.True(new CatalogImporter(f.Context).Plan().Issues.ShouldStop); Assert.False(File.Exists(f.Catalog.CatalogPath)); }
        finally { Directory.Delete(path); }
    }
    [Fact]
    public void RecognizedProductionTempsRemainNonAuthoritativeAndNeverEnterCatalog()
    {
        using var f = new CatalogTestFixture(); var result = f.Publish(); var file = result.Production.FilesVerified[0];
        var temp = file.DestinationPath + "." + Guid.NewGuid().ToString("N") + ".tmp"; File.WriteAllBytes(temp, [1, 2, 3]);
        var before = f.Sources(); var importer = new CatalogImporter(f.Context); var plan = importer.Plan(); Assert.True(plan.Issues.IsClean);
        importer.Import(plan); Assert.DoesNotContain(Assert.Single(f.Catalog.Query()).Files, file => file.RelativePath.EndsWith(".tmp"));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(temp)); f.AssertSources(before);
    }
    [Fact]
    public void TamperedDocumentContentFailsLogicalIntegrityEvenWhenSqlitePageChecksPass()
    {
        using var f = new CatalogTestFixture(); f.Publish(automatic: true);
        using (var c = f.Raw()) CatalogTestFixture.Execute(c, "UPDATE documents SET content=$b WHERE role='prompt'", ("$b", new byte[] { 1, 2, 3 }));
        CatalogTestFixture.Stop(Assert.Throws<CatalogException>(() => f.Catalog.CheckIntegrity()), NapIssueCodes.CatalogIntegrityFailed);
    }
    [Fact]
    public void RegisterRejectsDifferentContextAndChangedVerifiedPhysicalResults()
    {
        using var a = new CatalogTestFixture(universe: "alpha"); using var b = new CatalogTestFixture(universe: "beta"); var result = a.Publish();
        Assert.Throws<CatalogException>(() => b.Catalog.RegisterVerified(result.Package, result.Plan, result.Archive, result.Production));
        File.AppendAllText(result.Production.FilesVerified.Single(p => p.Role == "prompt").DestinationPath, "changed");
        CatalogTestFixture.Stop(Assert.Throws<CatalogException>(() => a.Catalog.RegisterVerified(result.Package, result.Plan, result.Archive, result.Production)), NapIssueCodes.CatalogSourceInvalid);
        Assert.False(File.Exists(a.Catalog.CatalogPath)); Assert.False(File.Exists(b.Catalog.CatalogPath));
    }
}

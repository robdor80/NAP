using NAP.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace NAP.Tests;

public sealed class CatalogImportTests
{
    [Fact]
    public void ImportNeverInventsAuditPassButValidatedPipelineEvidenceCanEnrichUnknownState()
    {
        using var f = new CatalogTestFixture(); var result = f.Publish(); var importer = new CatalogImporter(f.Context); importer.Import(importer.Plan());
        Assert.Null(f.Catalog.Get(result.Package.AssetKey.AssetId)!.AuditState);
        Assert.False(f.Catalog.RegisterVerified(result.Package, result.Plan, result.Archive, result.Production, ProductionTestFixture.Pass));
        Assert.Equal("PASS", f.Catalog.Get(result.Package.AssetKey.AssetId)!.AuditState);
        var before = ArchiveTestFixture.Snapshot(f.Root); importer.Import(importer.Plan()); ArchiveTestFixture.AssertSnapshot(before, f.Root);
        Assert.Equal("PASS", f.Catalog.Get(result.Package.AssetKey.AssetId)!.AuditState);
    }
    [Fact]
    public void CompleteSnapshotsImportAtomicallyWithoutJobsAndPreserveDocumentsAndFingerprints()
    {
        using var f = new CatalogTestFixture(); var a = f.Publish("emblem_a_001"); f.Publish("emblem_b_002");
        foreach (var journal in Directory.GetFiles(f.Context.Storage.StateRoot, "*.json")) File.Delete(journal);
        Directory.Delete(Path.Combine(f.Context.Storage.StateRoot, "production-executions"), true);
        var sources = f.Sources(); var importer = new CatalogImporter(f.Context); var plan = importer.Plan();
        Assert.True(plan.Issues.IsClean); Assert.Equal(2, plan.Assets.Count); Assert.False(File.Exists(f.Catalog.CatalogPath));
        Assert.Equal(2, importer.Import(plan)); Assert.Equal(0, importer.Import(importer.Plan())); f.AssertSources(sources);
        var actual = f.Catalog.Get(a.Package.AssetKey.AssetId)!; Assert.Equal(CatalogTestFixture.Logical(plan.Assets[0]), CatalogTestFixture.Logical(actual));
        Assert.Equal(10, actual.Files.Count); Assert.Equal(4, actual.Documents.Count); Assert.Null(actual.AuditState);
        Assert.Equal(a.Archive.MasterDigest, actual.MasterDigest); Assert.NotEqual(actual.MasterDigest, actual.ProductionDigest);
        Assert.Contains(actual.Traits, t => t.Key == "/eye_color" && t.Value == "green-grey");
        foreach (var document in actual.Documents)
        {
            var file = a.Production.FilesVerified.Single(p => p.Role == document.Role);
            Assert.Equal(File.ReadAllBytes(file.DestinationPath), document.ToArray());
            var copy = document.ToArray(); if (copy.Length > 0) copy[0] ^= 255; Assert.Equal(File.ReadAllBytes(file.DestinationPath), document.ToArray());
        }
        Assert.All(actual.Files, file => { Assert.False(Path.IsPathRooted(file.RelativePath)); Assert.DoesNotContain('\\', file.RelativePath); Assert.True(file.Verified); });
    }
    [Theory]
    [InlineData("manifest")] [InlineData("universe")] [InlineData("classification")] [InlineData("profile")]
    [InlineData("webp")] [InlineData("dimensions")] [InlineData("companion")] [InlineData("extra")]
    [InlineData("png")] [InlineData("index_hash")] [InlineData("master")] [InlineData("routing")]
    [InlineData("visual_identity")]
    public void InvalidCandidateBlocksWholeImportBeforeSqliteMutation(string mutation)
    {
        using var f = new CatalogTestFixture(); f.Publish("emblem_a_001"); var bad = f.Publish("emblem_b_002");
        var directory = bad.Production.FilesVerified[0].DestinationPath; directory = Path.GetDirectoryName(directory)!;
        var manifest = Path.Combine(directory, bad.Package.AssetKey.AssetId + "_manifest.json");
        switch (mutation)
        {
            case "manifest": File.WriteAllText(manifest, "{"); break;
            case "universe": File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("future_world", "other_world")); break;
            case "classification": File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("\"classification\":{}", "\"classification\":{\"unknown\":\"value\"}")); break;
            case "profile": File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("painted_icon", "other_profile")); break;
            case "webp": File.WriteAllText(bad.Production.FilesVerified.Single(p => p.Kind == ProductionAssetFileKind.GeneratedWebp).DestinationPath, "broken"); break;
            case "dimensions":
                using (var image = new Image<Rgba32>(3, 7)) image.SaveAsWebp(bad.Production.FilesVerified.Single(p => p.Kind == ProductionAssetFileKind.GeneratedWebp).DestinationPath);
                break;
            case "companion": File.Delete(bad.Production.FilesVerified.Single(p => p.Role == "prompt").DestinationPath); break;
            case "extra": File.WriteAllText(Path.Combine(directory, "external.txt"), "external"); break;
            case "png": File.Copy(bad.Package.FilesByRole["original"], Path.Combine(directory, "master.png")); break;
            case "index_hash":
                var indexPath = new ArchiveMasterIndexStore(f.Context).IndexPath;
                File.WriteAllText(indexPath, File.ReadAllText(indexPath).Replace(bad.Archive.MasterDigest.Hex, new string('f', 64))); break;
            case "master": File.WriteAllText(bad.Archive.FilesVerified.Single(p => p.Role == "original").DestinationPath, "bad"); break;
            case "routing": Directory.Move(directory, Path.Combine(Path.GetDirectoryName(directory)!, "historical")); break;
            case "visual_identity":
                foreach (var file in bad.Archive.FilesVerified.Where(p => p.FileName.EndsWith("_visual_identity.json"))) File.WriteAllText(file.DestinationPath, "{");
                foreach (var file in bad.Production.FilesVerified.Where(p => p.FileName.EndsWith("_visual_identity.json"))) File.WriteAllText(file.DestinationPath, "{"); break;
        }
        var before = ArchiveTestFixture.Snapshot(f.Root); var importer = new CatalogImporter(f.Context); var plan = importer.Plan();
        Assert.True(plan.Issues.ShouldStop); Assert.Throws<CatalogException>(() => importer.Import(plan));
        Assert.False(File.Exists(f.Catalog.CatalogPath)); ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }
    [Fact]
    public void PlanCollectsFailuresForEveryCandidateAndDetectsImagesWithoutManifest()
    {
        using var f = new CatalogTestFixture(); var a = f.Publish("emblem_a_001"); var b = f.Publish("emblem_b_002");
        File.Delete(a.Production.FilesVerified.Single(p => p.Role == "prompt").DestinationPath);
        File.Delete(b.Production.FilesVerified.Single(p => p.Role == "manifest").DestinationPath);
        var plan = new CatalogImporter(f.Context).Plan(); Assert.Empty(plan.Assets); Assert.True(plan.Issues.Issues.Count >= 2);
    }
    [Fact]
    public void SourcesChangedAfterPreflightCauseStopWithoutCreatingCatalog()
    {
        using var f = new CatalogTestFixture(); var a = f.Publish(); var importer = new CatalogImporter(f.Context); var plan = importer.Plan();
        foreach (var file in a.Archive.FilesVerified.Where(p => p.Role == "prompt")) File.WriteAllText(file.DestinationPath, "new");
        File.WriteAllText(a.Production.FilesVerified.Single(p => p.Role == "prompt").DestinationPath, "new");
        var before = ArchiveTestFixture.Snapshot(f.Root);
        CatalogTestFixture.Stop(Assert.Throws<CatalogException>(() => importer.Import(plan)), NapIssueCodes.CatalogSourceInvalid);
        ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }
    [Fact]
    public void LaterConflictRollsBackPreviouslyInsertedAssetInWholeImport()
    {
        using var f = new CatalogTestFixture(); var existing = f.Publish("emblem_z_001");
        Assert.True(f.Catalog.RegisterVerified(existing.Package, existing.Plan, existing.Archive, existing.Production));
        f.Publish("emblem_a_001");
        using (var c = f.Raw())
        {
            CatalogTestFixture.Execute(c, "UPDATE assets SET master_sha256=$h; UPDATE files SET sha256=$h WHERE kind='master'", ("$h", new string('f', 64)));
        }
        var before = ArchiveTestFixture.Snapshot(f.Root); var importer = new CatalogImporter(f.Context);
        CatalogTestFixture.Stop(Assert.Throws<CatalogException>(() => importer.Import(importer.Plan())), NapIssueCodes.CatalogAssetConflict);
        Assert.Single(f.Catalog.Query()); Assert.Null(f.Catalog.Get("emblem_a_001")); ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }
    [Theory]
    [InlineData("hash")] [InlineData("metadata")]
    public void ExactRegistrationIsIdempotentButIncompatibleFactsNeverOverwrite(string kind)
    {
        using var f = new CatalogTestFixture(); var result = f.Publish();
        Assert.True(f.Catalog.RegisterVerified(result.Package, result.Plan, result.Archive, result.Production));
        var original = ArchiveTestFixture.Snapshot(f.Root);
        Assert.False(f.Catalog.RegisterVerified(result.Package, result.Plan, result.Archive, result.Production)); ArchiveTestFixture.AssertSnapshot(original, f.Root);
        using (var c = f.Raw()) CatalogTestFixture.Execute(c, kind == "hash"
            ? "UPDATE assets SET master_sha256=$h; UPDATE files SET sha256=$h WHERE kind='master'"
            : "INSERT INTO classifications VALUES('future_world','emblem_example_001','future_dimension','value')", ("$h", new string('f', 64)));
        var before = ArchiveTestFixture.Snapshot(f.Root);
        CatalogTestFixture.Stop(Assert.Throws<CatalogException>(() => f.Catalog.RegisterVerified(result.Package, result.Plan, result.Archive, result.Production)), NapIssueCodes.CatalogAssetConflict);
        ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }
}

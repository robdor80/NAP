using System.Collections;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class ArchiveMasterResolutionTests
{
    [Fact]
    public void MasterRoleAndAdditionalCompanionsComeFromTheGenericProfile()
    {
        using var fixture = new ArchiveTestFixture(ArchiveTestFixture.Generic("generic_universe",
            new AssetPackageFileRule("original_art", "_original", ".png", true, "png_master"),
            new AssetPackageFileRule("research", "_research", ".txt", true),
            new AssetPackageFileRule("optional_companion", "_optional", ".json", false)));
        var plan = fixture.Plan();
        Assert.Equal(4, plan.Files.Count);
        Assert.DoesNotContain(plan.Files, f => f.Role == "master");
        Assert.Equal(plan.Files.Single(f => f.Role == "original_art").Digest, plan.MasterDigest);
        Assert.Equal("originals/" + fixture.Package.AssetKey.AssetId, plan.RelativeDirectory);
        var result = fixture.Execute(plan);
        Assert.Equal(ArchiveMasterOutcome.Copied, result.Outcome);
        Assert.Equal(4, result.FilesVerified.Count);
        Assert.All(plan.Files, f => Assert.Equal(File.ReadAllBytes(f.SourcePath), File.ReadAllBytes(f.DestinationPath)));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("multiple")]
    [InlineData("optional")]
    [InlineData("missing_role")]
    [InlineData("manifest_role")]
    public void InvalidMasterOrManifestRoleStopsWithoutWrites(string mode)
    {
        using var fixture = new ArchiveTestFixture(ArchiveTestFixture.Generic());
        var original = fixture.Package;
        var master = original.AssetRule.PackageFiles.Single(f => f.ContentValidator == "png_master");
        var rules = mode switch
        {
            "none" => new[] { new AssetPackageFileRule(master.Role, "", ".png", true) },
            "multiple" => new[] { master, new AssetPackageFileRule("second_png", "_second", ".png", true, "png_master") },
            "optional" => new[] { new AssetPackageFileRule(master.Role, "", ".png", false, "png_master") },
            _ => original.AssetRule.PackageFiles.ToArray()
        };
        var rule = new UniverseAssetRule("portrait", "portrait_npc", [], [], rules, original.AssetRule.Routing);
        var files = original.FilesByRole.ToDictionary(pair => pair.Key, pair => pair.Value);
        if (mode == "missing_role") files.Remove(master.Role);
        if (mode == "manifest_role") files.Add("manifest", original.ManifestPath);
        var package = AiAuditTestData.Construct<ValidatedAssetPackage>(original.AssetKey, original.PackageRoot, original.ManifestPath, original.Manifest, rule, files);
        var processing = new ProcessingPlanBuilder().Build(package, new ProductionRepositoryValidator().Validate(fixture.Context).Repository!, fixture.Processing.ProductionDestination);
        var before = ArchiveTestFixture.Snapshot(fixture.Root);
        Assert.Throws<InvalidOperationException>(() => new ArchiveMasterPlanner(fixture.Context).Plan(package, processing));
        ArchiveTestFixture.AssertSnapshot(before, fixture.Root);
    }

    [Fact]
    public void PlanAndResultExposeOnlyImmutableCollectionsAndGetOnlyProperties()
    {
        using var fixture = new ArchiveTestFixture();
        var plan = fixture.Plan();
        var result = fixture.Execute(plan);
        Assert.Throws<NotSupportedException>(() => ((IList)plan.Files).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList)plan.FilesToCopy).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList)result.FilesVerified).Clear());
        foreach (var type in new[] { typeof(ArchiveMasterPlan), typeof(ArchiveMasterFile), typeof(ArchiveMasterResult) })
        {
            Assert.True(type.IsSealed);
            Assert.All(type.GetProperties(), p => Assert.Null(p.SetMethod));
            Assert.Empty(type.GetConstructors());
        }
        Assert.Equal(plan.AssetKey, result.AssetKey);
        Assert.Equal(plan.MasterDigest, result.MasterDigest);
        Assert.Equal(plan.RelativeDirectory, result.RelativeDirectory);
    }

    [Fact]
    public void ProcessingPlanCannotSubstituteFilesMetadataOrAuthorizedProductionRoot()
    {
        using var fixture = new ArchiveTestFixture();
        var original = fixture.Processing;
        foreach (var mode in new[] { "type", "profile", "classification", "source", "manifest", "root" })
        {
            var files = original.FilesByRole.ToDictionary(p => p.Key, p => p.Value);
            if (mode == "source") files["prompt"] = Path.Combine(fixture.Root, "outside.txt");
            var destination = mode == "root" ? AiAuditTestData.Construct<ProductionAssetDestination>(original.AssetKey,
                fixture.Root, original.ProductionDestination.RelativeDirectory, Path.Combine(fixture.Root, "outside")) : original.ProductionDestination;
            var forged = AiAuditTestData.Construct<ProcessingPlan>(original.AssetKey, mode == "type" ? "scene" : original.AssetType,
                mode == "profile" ? "future_profile" : original.ProductionProfile,
                mode == "classification" ? new Dictionary<string, string>() : original.Classification, original.PackageRoot,
                mode == "manifest" ? Path.Combine(fixture.Root, "outside.txt") : original.ManifestPath, files, destination);
            Assert.Throws<ArgumentException>(() => new ArchiveMasterPlanner(fixture.Context).Plan(fixture.Package, forged));
        }
    }
}

using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class ArchiveBoundaryTests
{
    [Fact]
    public void RootAndPlanAreReadOnlyAndPreserveTheExactNimroelPackage()
    {
        using var fixture = new ArchiveTestFixture();
        File.WriteAllText(Path.Combine(fixture.Package.PackageRoot, "unknown.txt"), "late unknown");
        File.WriteAllText(Path.Combine(fixture.Package.PackageRoot, "transport.zip"), "transport");
        File.WriteAllText(Path.Combine(fixture.Package.PackageRoot, "generated.webp"), "output");
        var before = ArchiveTestFixture.Snapshot(fixture.Root);
        Assert.True(new ArchiveRootValidator().Validate(fixture.Context).IsClean);
        var plan = fixture.Plan();
        ArchiveTestFixture.AssertSnapshot(before, fixture.Root);
        var relative = "portraits/norgard/treskal/farmer/male/" + ArchiveTestFixture.NimroelAsset;
        Assert.Equal(relative, plan.RelativeDirectory);
        Assert.Equal(fixture.Processing.ProductionDestination.RelativeDirectory, plan.RelativeDirectory);
        Assert.Equal(Path.Combine(fixture.Context.Storage.ArchiveRoot, Path.Combine(relative.Split('/'))), plan.DestinationDirectory);
        Assert.Equal(ArchiveMasterAction.CopyAndIndex, plan.Action);
        Assert.Equal(5, plan.Files.Count);
        Assert.Equal(plan.Files, plan.FilesToCopy);
        Assert.Equal(new[] { "info", "manifest", "master", "prompt", "visual_identity" }, plan.Files.Select(f => f.Role));
        Assert.All(plan.Files, file =>
        {
            Assert.Equal(Path.GetFileName(file.SourcePath), file.FileName);
            Assert.Equal(new Sha256Hasher().Compute(file.SourcePath), file.Digest);
            Assert.Equal(new FileInfo(file.SourcePath).Length, file.SizeBytes);
            Assert.Equal(Path.Combine(plan.DestinationDirectory, file.FileName), file.DestinationPath);
        });
        Assert.Equal(plan.Files.Single(f => f.Role == "master").Digest, plan.MasterDigest);
        Assert.False(Directory.Exists(Path.Combine(plan.ArchiveRoot, "_nap")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrFileRootStopsWithoutCreatingIt(bool fileRoot)
    {
        using var fixture = new ArchiveTestFixture();
        File.Delete(Path.Combine(fixture.Context.Storage.ArchiveRoot, "sentinel.txt"));
        Directory.Delete(fixture.Context.Storage.ArchiveRoot);
        if (fileRoot) File.WriteAllText(fixture.Context.Storage.ArchiveRoot, "file blocker");
        var before = ArchiveTestFixture.Snapshot(fixture.Root);
        var code = fileRoot ? NapIssueCodes.ArchiveRootInvalid : NapIssueCodes.ArchiveRootMissing;
        Assert.Equal(code, Assert.Single(new ArchiveRootValidator().Validate(fixture.Context).Issues).Code);
        ArchiveTestFixture.AssertStop(Assert.Throws<ArchiveStorageException>(() => fixture.Plan()), code);
        ArchiveTestFixture.AssertStop(Assert.Throws<ArchiveStorageException>(() => fixture.Store.Load()), code);
        ArchiveTestFixture.AssertSnapshot(before, fixture.Root);
        Assert.False(Directory.Exists(fixture.Context.Storage.ArchiveRoot));
    }

    [Theory]
    [InlineData("production")]
    [InlineData("production_child")]
    [InlineData("production_parent")]
    [InlineData("workspace")]
    public void ArchiveRootCannotWriteInsideOrAboveProtectedStorage(string mode)
    {
        using var fixture = new ArchiveTestFixture();
        var storage = fixture.Context.Storage;
        var archive = mode switch
        {
            "production" => storage.ProductionRoot,
            "production_child" => Path.Combine(storage.ProductionRoot, "archive"),
            "production_parent" => fixture.Root,
            _ => storage.WorkspaceRoot
        };
        var context = new UniverseContext(fixture.Context.Profile, new UniverseStorageConfig(fixture.Context.Id, storage.WorkspaceRoot, storage.ProductionRoot, archive));
        Assert.Equal(NapIssueCodes.ArchiveRootInvalid, Assert.Single(new ArchiveRootValidator().Validate(context).Issues).Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("originals/../escape")]
    [InlineData("originals/./escape")]
    [InlineData("originals//escape")]
    [InlineData("originals/")]
    [InlineData("/escape")]
    [InlineData("C:/escape")]
    [InlineData("C:escape")]
    [InlineData("//server/share")]
    [InlineData("originals\\escape")]
    [InlineData("originals/a:b")]
    [InlineData("originals/\u0001")]
    [InlineData("con")]
    [InlineData("originals/COM1.txt")]
    [InlineData("originals/lpt9")]
    [InlineData("originals/nul.png")]
    [InlineData("originals/COM¹.txt")]
    [InlineData("originals/conout$")]
    [InlineData("originals/trailing.")]
    [InlineData("originals/trailing ")]
    [InlineData("_nap/asset")]
    public void UnsafeLogicalPathsAreRejectedBeforeIo(string relative)
    {
        using var fixture = new ArchiveTestFixture();
        var before = ArchiveTestFixture.Snapshot(fixture.Root);
        Assert.Throws<ArgumentException>(() => ArchiveTestFixture.Invoke(null, ArchiveTestFixture.CoreType("ArchivePaths"), "Resolve",
            fixture.Context.Storage.ArchiveRoot, relative));
        if (relative.Length == 0) return;
        var destination = AiAuditTestData.Construct<ProductionAssetDestination>(fixture.Package.AssetKey, fixture.Context.Storage.ProductionRoot,
            relative, Path.Combine(fixture.Context.Storage.ProductionRoot, "unused"));
        var plan = new ProcessingPlanBuilder().Build(fixture.Package,
            new ProductionRepositoryValidator().Validate(fixture.Context).Repository!, destination);
        Assert.Throws<ArgumentException>(() => new ArchiveMasterPlanner(fixture.Context).Plan(fixture.Package, plan));
        ArchiveTestFixture.AssertSnapshot(before, fixture.Root);
    }

    [Fact]
    public void SegmentContainmentRejectsSiblingPrefixAndRootItself()
    {
        using var fixture = new ArchiveTestFixture();
        var paths = ArchiveTestFixture.CoreType("ArchivePaths");
        Assert.False((bool)ArchiveTestFixture.Invoke(null, paths, "Within", fixture.Context.Storage.ArchiveRoot, fixture.Context.Storage.ArchiveRoot)!);
        Assert.False((bool)ArchiveTestFixture.Invoke(null, paths, "Within", fixture.Context.Storage.ArchiveRoot, fixture.Context.Storage.ArchiveRoot + "_evil/file")!);
        Assert.True((bool)ArchiveTestFixture.Invoke(null, paths, "Within", fixture.Context.Storage.ArchiveRoot, Path.Combine(fixture.Context.Storage.ArchiveRoot, "asset"))!);
    }

    [Theory]
    [InlineData("root")]
    [InlineData("parent")]
    [InlineData("destination")]
    [InlineData("infrastructure")]
    public void ExistingJunctionsAreRejectedWithoutFollowingThem(string mode)
    {
        using var fixture = new ArchiveTestFixture();
        var target = Directory.CreateDirectory(Path.Combine(fixture.Root, "junction_target")).FullName;
        File.WriteAllText(Path.Combine(target, "protected.txt"), "outside");
        var archive = fixture.Context.Storage.ArchiveRoot;
        var link = mode switch
        {
            "root" => Path.Combine(fixture.Root, "archive_link"),
            "parent" => Path.Combine(fixture.Root, "parent_link"),
            "destination" => Path.Combine(archive, "portraits"),
            _ => Path.Combine(archive, "_nap")
        };
        ArchiveTestFixture.Junction(link, target);
        try
        {
            var before = ArchiveTestFixture.Snapshot(target);
            if (mode is "root" or "parent")
            {
                if (mode == "parent") Directory.CreateDirectory(Path.Combine(target, "archive"));
                var context = new UniverseContext(fixture.Context.Profile, new UniverseStorageConfig(fixture.Context.Id,
                    fixture.Context.Storage.WorkspaceRoot, fixture.Context.Storage.ProductionRoot, mode == "root" ? link : Path.Combine(link, "archive")));
                Assert.Equal(NapIssueCodes.ArchiveRootReparse, Assert.Single(new ArchiveRootValidator().Validate(context).Issues).Code);
                ArchiveTestFixture.AssertStop(Assert.Throws<ArchiveStorageException>(() => new ArchiveMasterIndexStore(context).Load()), NapIssueCodes.ArchiveRootReparse);
                if (mode == "parent") Directory.Delete(Path.Combine(target, "archive"));
            }
            else if (mode == "infrastructure")
                ArchiveTestFixture.AssertStop(Assert.Throws<ArchiveStorageException>(() => fixture.Plan()), NapIssueCodes.ArchiveEntryReparse);
            else
            {
                var plan = fixture.Plan();
                Assert.Contains(plan.Issues.Issues, i => i.Code == NapIssueCodes.ArchiveEntryReparse);
                ArchiveTestFixture.AssertStop(Assert.Throws<ArchiveStorageException>(() => fixture.Execute(plan)), NapIssueCodes.ArchiveEntryReparse);
            }
            ArchiveTestFixture.AssertSnapshot(before, target);
        }
        finally { Directory.Delete(link); }
    }

    [Fact]
    public void NullInputsFailExplicitly()
    {
        Assert.Throws<ArgumentNullException>(() => new ArchiveRootValidator().Validate(null!));
        Assert.Throws<ArgumentNullException>(() => new ArchiveMasterPlanner(null!));
        Assert.Throws<ArgumentNullException>(() => new ArchiveMasterIndexStore(null!));
        Assert.Throws<ArgumentNullException>(() => new ArchiveMasterExecutor(null!));
        using var fixture = new ArchiveTestFixture();
        Assert.Throws<ArgumentNullException>(() => new ArchiveMasterPlanner(fixture.Context).Plan(null!, fixture.Processing));
        Assert.Throws<ArgumentNullException>(() => new ArchiveMasterPlanner(fixture.Context).Plan(fixture.Package, null!));
    }
}

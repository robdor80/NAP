using NAP.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace NAP.Tests;

public sealed class ProductionSafetyTests
{
    [Theory]
    [InlineData("valid")] [InlineData("missing")] [InlineData("file")] [InlineData("workspace")]
    [InlineData("workspace_child")] [InlineData("archive")] [InlineData("archive_child")] [InlineData("parent")]
    [InlineData("git_root")] [InlineData("git_ancestor")]
    public void RootValidationIsReadOnlyAndMissingRootIsNeverCreated(string kind)
    {
        using var f = new ProductionTestFixture();
        var path = kind switch { "missing" => Path.Combine(f.Root, "missing"), "file" => Path.Combine(f.Root, "file"),
            "workspace" => f.Context.Storage.WorkspaceRoot, "workspace_child" => Path.Combine(f.Context.Storage.WorkspaceRoot, "child"),
            "archive" => f.Context.Storage.ArchiveRoot, "archive_child" => Path.Combine(f.Context.Storage.ArchiveRoot, "child"),
            "git_root" => Path.Combine(f.Root, ".git"), "git_ancestor" => Path.Combine(f.Root, ".git", "objects"),
            "parent" => f.Root, _ => f.Context.Storage.ProductionRoot };
        if (kind == "file") File.WriteAllText(path, "file root");
        if (kind.StartsWith("git")) Directory.CreateDirectory(path);
        var context = ProductionTestFixture.WithProduction(f.Context, path);
        var before = ArchiveTestFixture.Snapshot(f.Root);
        var report = new ProductionStorageRootValidator().Validate(context);
        if (kind == "valid") Assert.True(report.IsClean);
        else Assert.Contains(report.Issues, i => i.Code == (kind == "missing" ? NapIssueCodes.ProductionRootMissing : NapIssueCodes.ProductionRootInvalid) && i.StopsProcessing);
        ArchiveTestFixture.AssertSnapshot(before, f.Root);
        if (kind == "missing") Assert.False(Directory.Exists(path));
    }

    [Theory]
    [InlineData("root")] [InlineData("ancestor")] [InlineData("child")] [InlineData("final_directory")]
    public void ReparsePointsAreRejectedWithoutFollowingTarget(string kind)
    {
        using var f = new ProductionTestFixture(); var plan = f.Plan();
        var target = Directory.CreateDirectory(Path.Combine(f.Root, "reparse_target")).FullName;
        File.WriteAllText(Path.Combine(target, "protected.txt"), "protected");
        var link = kind is "root" or "ancestor" ? Path.Combine(f.Root, "link") : kind == "child" ? Path.Combine(plan.ProductionRoot, "icons") : plan.DestinationDirectory;
        if (kind == "final_directory") Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        ArchiveTestFixture.Junction(link, target);
        try
        {
            var before = ArchiveTestFixture.Snapshot(f.Root);
            if (kind is "root" or "ancestor")
            {
                var context = ProductionTestFixture.WithProduction(f.Context, kind == "root" ? link : Path.Combine(link, "child"));
                Assert.Contains(new ProductionStorageRootValidator().Validate(context).Issues, i => i.Code == NapIssueCodes.ProductionRootReparse && i.StopsProcessing);
            }
            else ProductionTestFixture.Stop(Assert.Throws<ProductionStorageException>(() => f.Execute(plan)), NapIssueCodes.ProductionEntryReparse);
            ArchiveTestFixture.AssertSnapshot(before, f.Root);
        }
        finally { Directory.Delete(link); }
    }

    [Theory]
    [InlineData("")] [InlineData("../escape")] [InlineData("icons/../escape")] [InlineData("./asset")]
    [InlineData("icons/./asset")] [InlineData("/rooted")] [InlineData("C:/rooted")]
    [InlineData("icons//asset")] [InlineData("icons/asset/")] [InlineData("icons\\asset")]
    [InlineData("icons/a:b")] [InlineData("icons/a\u0001b")] [InlineData("icons/a<b")]
    [InlineData("icons/a>b")] [InlineData("icons/a\"b")] [InlineData("icons/a|b")]
    [InlineData("icons/a?b")] [InlineData("icons/a*b")] [InlineData("icons/CON")]
    [InlineData("icons/prn.txt")] [InlineData("icons/AUX")] [InlineData("icons/NUL")]
    [InlineData("icons/COM1")] [InlineData("icons/LPT9.txt")] [InlineData("icons/COM¹")]
    [InlineData("icons/a.")] [InlineData("icons/a ")] [InlineData(".git/asset")] [InlineData("icons/_nap/asset")]
    public void UnsafeLogicalPathsAreRejected(string relative)
    {
        using var f = new ProductionTestFixture(); var before = ArchiveTestFixture.Snapshot(f.Root);
        ProductionTestFixture.Stop(Assert.Throws<ProductionStorageException>(() => ProductionTestFixture.Invoke(null, "ProductionPaths", "Resolve", f.Context.Storage.ProductionRoot, relative)), NapIssueCodes.ProductionPathInvalid);
        ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }

    [Fact]
    public void ContainmentUsesSegmentsAndDoesNotAcceptPrefixTrap()
    {
        using var f = new ProductionTestFixture(); var root = f.Context.Storage.ProductionRoot;
        Assert.False((bool)ProductionTestFixture.Invoke(null, "ProductionPaths", "Within", root, root + "_evil/asset")!);
        Assert.False((bool)ProductionTestFixture.Invoke(null, "ProductionPaths", "Within", root, root)!);
        Assert.True((bool)ProductionTestFixture.Invoke(null, "ProductionPaths", "Within", root, Path.Combine(root, "asset"))!);
        Assert.Throws<ArgumentException>(() => new UniverseStorageConfig(f.Context.Id, f.Context.Storage.WorkspaceRoot, "relative", f.Context.Storage.ArchiveRoot));
    }

    [Theory]
    [InlineData("directory_blocker")] [InlineData("directory_casing")] [InlineData("file_casing")]
    public void BlockersAndWindowsCasingConflictsAreStops(string kind)
    {
        using var f = new ProductionTestFixture(); var plan = f.Plan();
        if (kind == "directory_blocker") File.WriteAllText(Path.Combine(plan.ProductionRoot, "icons"), "blocker");
        else if (kind == "directory_casing") Directory.CreateDirectory(Path.Combine(plan.ProductionRoot, "ICONS"));
        else { f.Prepare(plan, 0); File.WriteAllBytes(Path.Combine(plan.DestinationDirectory, plan.Files[0].FileName.ToUpperInvariant()), plan.Files[0].ToArray()); }
        var before = ArchiveTestFixture.Snapshot(f.Root);
        if (kind == "directory_blocker" || OperatingSystem.IsWindows())
            ProductionTestFixture.Stop(Assert.Throws<ProductionStorageException>(() => f.Execute(plan)), NapIssueCodes.ProductionFileCollision);
        ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }

    [Theory]
    [InlineData("source_changed")] [InlineData("source_missing")] [InlineData("temp_corrupt")]
    [InlineData("temp_short")] [InlineData("final_appeared")] [InlineData("unexpected_appeared")]
    [InlineData("final_identical")]
    public void PublicationBoundaryRejectsDeterministicRaceAndRetainsExistingFiles(string mutation)
    {
        using var f = new ProductionTestFixture(); var plan = f.Plan(); f.Prepare(plan, 0);
        var file = plan.Files.Single(p => p.Role == "prompt");
        var temp = file.DestinationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(temp, ProductionTestFixture.Bytes(file));
        switch (mutation)
        {
            case "source_changed": File.AppendAllText(file.SourcePath!, "changed during copy"); break;
            case "source_missing": File.Delete(file.SourcePath!); break;
            case "temp_corrupt": var bytes = File.ReadAllBytes(temp); bytes[0] ^= 1; File.WriteAllBytes(temp, bytes); break;
            case "temp_short": File.WriteAllBytes(temp, [1]); break;
            case "final_appeared": File.WriteAllText(file.DestinationPath, "raced final"); break;
            case "final_identical": File.WriteAllBytes(file.DestinationPath, ProductionTestFixture.Bytes(file)); break;
            case "unexpected_appeared": File.WriteAllText(Path.Combine(plan.DestinationDirectory, "alien"), "raced entry"); break;
        }
        var before = ArchiveTestFixture.Snapshot(f.Root);
        var ex = Assert.Throws<ProductionStorageException>(() => ProductionTestFixture.Invoke(new ProductionAssetExecutor(f.Context), "ProductionAssetExecutor", "PublishFile", plan, file, temp));
        ProductionTestFixture.Stop(ex, mutation.StartsWith("source") ? NapIssueCodes.ProductionSourceChanged : mutation.StartsWith("temp") ? NapIssueCodes.ProductionVerificationFailed : mutation.StartsWith("final") ? NapIssueCodes.ProductionFileCollision : NapIssueCodes.ProductionUnexpectedEntry);
        ArchiveTestFixture.AssertSnapshot(before, f.Root); Assert.True(File.Exists(temp));
    }

    [Fact]
    public void DirectoryReplacingWithReparseAtPublicationIsStopped()
    {
        using var f = new ProductionTestFixture(); var plan = f.Plan(); f.Prepare(plan, 0);
        var file = plan.Files[0]; var temp = file.DestinationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(temp, file.ToArray());
        var moved = Path.Combine(f.Root, "moved_destination"); Directory.Move(plan.DestinationDirectory, moved);
        ArchiveTestFixture.Junction(plan.DestinationDirectory, moved);
        try
        {
            var before = ArchiveTestFixture.Snapshot(f.Root);
            ProductionTestFixture.Stop(Assert.Throws<ProductionStorageException>(() => ProductionTestFixture.Invoke(new ProductionAssetExecutor(f.Context), "ProductionAssetExecutor", "PublishFile", plan, file, temp)), NapIssueCodes.ProductionEntryReparse);
            ArchiveTestFixture.AssertSnapshot(before, f.Root);
        }
        finally { Directory.Delete(plan.DestinationDirectory); }
    }

    [Theory]
    [InlineData("wrong_sha")] [InlineData("wrong_size")] [InlineData("invalid_webp")] [InlineData("wrong_dimensions")]
    public void PersistedWebpIsHashedAndReallyDecoded(string kind)
    {
        using var f = new ProductionTestFixture(); var plan = f.Plan(); f.Prepare(plan, 5);
        var original = plan.Files[0];
        if (kind == "wrong_sha") { var bytes = original.ToArray(); bytes[^1] ^= 1; File.WriteAllBytes(original.DestinationPath, bytes); }
        if (kind == "wrong_size") File.WriteAllBytes(original.DestinationPath, [1]);
        var file = original;
        if (kind is "invalid_webp" or "wrong_dimensions")
        {
            byte[] bytes;
            if (kind == "invalid_webp") bytes = [1, 2, 3, 4];
            else { using var image = new Image<Rgba32>(3, 2); using var stream = new MemoryStream(); image.Save(stream, new WebpEncoder()); bytes = stream.ToArray(); }
            using var content = new MemoryStream(bytes);
            file = AiAuditTestData.Construct<ProductionAssetFile>(ProductionAssetFileKind.GeneratedWebp, original.Role, original.FileName, null!, original.DestinationPath,
                new Sha256Hasher().Compute(content), (long)bytes.Length, bytes);
            File.WriteAllBytes(file.DestinationPath, bytes);
        }
        var before = ArchiveTestFixture.Snapshot(f.Root);
        ProductionTestFixture.Stop(Assert.Throws<ProductionStorageException>(() => ProductionTestFixture.Invoke(null, "ProductionAssetPlanner", "VerifyOutput", plan, file, file.DestinationPath, NapIssueCodes.ProductionVerificationFailed)), NapIssueCodes.ProductionVerificationFailed);
        ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }

    [Fact]
    public void ReadOnlyVerifyNeverRepairsMissingFinalOrReportsSuccess()
    {
        using var f = new ProductionTestFixture(); var plan = f.Plan(); f.Prepare(plan, 3);
        var before = ArchiveTestFixture.Snapshot(f.Root);
        ProductionTestFixture.Stop(Assert.Throws<ProductionStorageException>(() => new ProductionAssetExecutor(f.Context).Verify(plan)), NapIssueCodes.ProductionVerificationFailed);
        ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }
}

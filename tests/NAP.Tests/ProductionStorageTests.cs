using System.Reflection;
using System.Runtime.CompilerServices;
using NAP.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace NAP.Tests;

public sealed class ProductionStorageTests
{
    [Fact]
    public void PlannerIsReadOnlyAndFreezesExactDeclaredFileSet()
    {
        using var f = new ProductionTestFixture(extraRole: true);
        var archive = f.Archive();
        File.WriteAllText(Path.Combine(f.Package.PackageRoot, "unknown.zip"), "not declared");
        File.WriteAllText(Path.Combine(f.Package.PackageRoot, "cache.tmp"), "not declared");
        var before = ArchiveTestFixture.Snapshot(f.Root);
        var plan = f.Plan(archive);
        ArchiveTestFixture.AssertSnapshot(before, f.Root);
        Assert.Equal(f.Processing.ProductionDestination.RelativeDirectory, plan.RelativeDirectory);
        Assert.Equal("icons/approved/emblem_example_001", plan.RelativeDirectory);
        Assert.Equal(6, plan.Files.Count); Assert.Equal(6, plan.FilesToWrite.Count);
        Assert.Single(plan.Files, p => p.Kind == ProductionAssetFileKind.GeneratedWebp);
        Assert.Single(plan.Files, p => p.Kind == ProductionAssetFileKind.Manifest);
        Assert.Contains(plan.Files, p => p.Role == "future_notes");
        Assert.DoesNotContain(plan.Files, p => p.FileName.EndsWith(".png") || p.FileName.EndsWith(".zip") || p.FileName.EndsWith(".tmp"));
        Assert.True(plan.Issues.IsClean); Assert.Equal(ProductionAssetAction.WriteAndVerify, plan.Action);
        var webp = plan.Files.Single(p => p.Kind == ProductionAssetFileKind.GeneratedWebp);
        var bytes = webp.ToArray(); bytes[0] ^= 255;
        Assert.NotEqual(bytes, webp.ToArray());
        Assert.Throws<NotSupportedException>(() => ((IList<ProductionAssetFile>)plan.Files).Clear());
        Assert.All(plan.Files.Where(p => p.SourcePath is not null), p => Assert.Throws<InvalidOperationException>(() => p.ToArray()));
    }

    [Theory]
    [InlineData(1)] [InlineData(3)] [InlineData(5)] [InlineData(0)]
    public void PartialRecoveryOnlyWritesMissingFilesAndSecondRunIsExactNoOp(int existing)
    {
        using var f = new ProductionTestFixture();
        var archive = f.Archive(); var initial = f.Plan(archive); f.Prepare(initial, existing);
        var epoch = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        foreach (var file in initial.Files.Take(existing)) File.SetLastWriteTimeUtc(file.DestinationPath, epoch);
        var retained = initial.Files.Take(existing).ToDictionary(p => p.DestinationPath, p => File.GetLastWriteTimeUtc(p.DestinationPath));
        var plan = f.Plan(archive); Assert.Equal(5 - existing, plan.FilesToWrite.Count);
        var protectedArchive = ArchiveTestFixture.Snapshot(f.Context.Storage.ArchiveRoot);
        var master = File.ReadAllBytes(f.Master);
        var result = f.Execute(plan);
        Assert.Equal(existing == 5 ? ProductionAssetOutcome.AlreadyProduced : ProductionAssetOutcome.Produced, result.Outcome);
        Assert.Equal(5, result.FilesVerified.Count);
        foreach (var p in result.FilesVerified)
        {
            Assert.Equal(ProductionTestFixture.Bytes(p), File.ReadAllBytes(p.DestinationPath));
            Assert.Equal(p.Digest, new Sha256Hasher().Compute(p.DestinationPath));
            Assert.Equal(p.SizeBytes, new FileInfo(p.DestinationPath).Length);
        }
        foreach (var (path, timestamp) in retained) Assert.Equal(timestamp, File.GetLastWriteTimeUtc(path));
        Assert.Equal(master, File.ReadAllBytes(f.Master));
        ArchiveTestFixture.AssertSnapshot(protectedArchive, f.Context.Storage.ArchiveRoot);
        Assert.Equal(JobState.Audited, new JobStateStore(f.Context).Load(f.ProductionJob).State);
        foreach (var file in plan.Files) File.SetLastWriteTimeUtc(file.DestinationPath, epoch);
        var before = ArchiveTestFixture.Snapshot(f.Root);
        var already = f.Plan(archive); Assert.Equal(ProductionAssetAction.AlreadyProduced, already.Action); Assert.Empty(already.FilesToWrite);
        Assert.Equal(ProductionAssetOutcome.AlreadyProduced, f.Execute(already).Outcome);
        // Even a stale WriteAndVerify plan must be physically revalidated.
        Assert.Equal(ProductionAssetOutcome.AlreadyProduced, f.Execute(initial).Outcome);
        ArchiveTestFixture.AssertSnapshot(before, f.Root);
        Assert.Equal(5, Directory.GetFiles(plan.DestinationDirectory).Length);
        Assert.Empty(Directory.GetFiles(f.Context.Storage.ProductionRoot, "*.png", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(f.Context.Storage.ProductionRoot, "*.tmp", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(8, 10, 0)] [InlineData(16, 20, 100)] [InlineData(4, 5, 37)] [InlineData(12, 15, 90)]
    public void ConversionHonorsGenericProfileDimensionsQualityAndUsesFullFrame(int width, int height, int quality)
    {
        using var f = new ProductionTestFixture(width: width, height: height, quality: quality);
        ProductionTestFixture.WritePng(f.Master, 8, 10); f.Refresh();
        var plan = f.Plan();
        Assert.Equal("emblem", plan.AssetType); Assert.Equal("original", plan.Conversion.SourceRole);
        Assert.Equal(width, plan.Conversion.OutputWidth); Assert.Equal(height, plan.Conversion.OutputHeight); Assert.Equal(quality, plan.Conversion.WebpQuality);
        var result = f.Execute(plan); var webp = result.FilesVerified.Single(p => p.Kind == ProductionAssetFileKind.GeneratedWebp);
        using var decoded = Image.Load<Rgba32>(webp.DestinationPath);
        Assert.Equal(width, decoded.Width); Assert.Equal(height, decoded.Height);
        Assert.Equal(255, decoded[0, 0].A); Assert.Equal(255, decoded[width - 1, height - 1].A);
        // Independent encoder at unchanged source dimensions proves profile quality reaches the encoder.
        if (width == 8 && height == 10)
        {
            using var source = Image.Load<Rgba32>(f.Master); using var output = new MemoryStream();
            source.Save(output, new WebpEncoder { FileFormat = WebpFileFormatType.Lossy, Quality = quality });
            Assert.Equal(output.ToArray(), webp.ToArray());
        }
    }

    [Theory]
    [InlineData("pixel_budget")] [InlineData("aspect_ratio")] [InlineData("invalid_png")] [InlineData("no_conversion")]
    public void ConversionFailuresDoNotWriteProduction(string kind)
    {
        using var f = new ProductionTestFixture(conversion: kind != "no_conversion");
        if (kind == "aspect_ratio") { ProductionTestFixture.WritePng(f.Master, 3, 2); f.Refresh(); }
        var archive = f.Archive();
        if (kind == "invalid_png") File.WriteAllText(f.Master, "invalid PNG");
        var before = ArchiveTestFixture.Snapshot(f.Root);
        var ex = Assert.Throws<ProductionStorageException>(() => f.Plan(archive, kind == "pixel_budget" ? 79 : ProductionTestFixture.Budget));
        ProductionTestFixture.Stop(ex, kind == "invalid_png" ? NapIssueCodes.ProductionArchiveMismatch : NapIssueCodes.ProductionConversionFailed);
        ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }

    [Theory]
    [InlineData("original")] [InlineData("prompt")] [InlineData("info")] [InlineData("identity")] [InlineData("manifest")]
    public void SourceChangesAfterArchiveOrPlanningAreStops(string role)
    {
        using var f = new ProductionTestFixture(); var archive = f.Archive(); var plan = f.Plan(archive);
        var source = role == "manifest" ? f.Package.ManifestPath : f.Package.FilesByRole[role];
        File.AppendAllText(source, "changed");
        var before = ArchiveTestFixture.Snapshot(f.Root);
        ProductionTestFixture.Stop(Assert.Throws<ProductionStorageException>(() => f.Plan(archive)), NapIssueCodes.ProductionArchiveMismatch);
        ProductionTestFixture.Stop(Assert.Throws<ProductionStorageException>(() => f.Execute(plan)), NapIssueCodes.ProductionSourceChanged);
        ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void EveryFinalCollisionIsStopAndNeverOverwritten(int index)
    {
        using var f = new ProductionTestFixture(); var archive = f.Archive(); var plan = f.Plan(archive); f.Prepare(plan, 5);
        File.WriteAllText(plan.Files[index].DestinationPath, "different final");
        var before = ArchiveTestFixture.Snapshot(f.Root);
        var conflict = f.Plan(archive); Assert.True(conflict.Issues.ShouldStop);
        ProductionTestFixture.Stop(Assert.Throws<ProductionStorageException>(() => f.Execute(plan)), NapIssueCodes.ProductionFileCollision);
        ProductionTestFixture.Stop(Assert.Throws<ProductionStorageException>(() => f.Execute(conflict)), NapIssueCodes.ProductionFileCollision);
        ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }

    [Theory]
    [InlineData("file")] [InlineData("directory")] [InlineData("foreign.tmp")] [InlineData("uppercase_guid")]
    [InlineData("empty_guid")] [InlineData("wrong_prefix")] [InlineData("short_guid")]
    public void UnexpectedEntriesAreNeverCleaned(string kind)
    {
        using var f = new ProductionTestFixture(); var plan = f.Plan(); f.Prepare(plan, 0);
        var name = kind switch { "uppercase_guid" => plan.Files[0].FileName + ".ABCDEFABCDEFABCDEFABCDEFABCDEFABCD.tmp",
            "empty_guid" => plan.Files[0].FileName + "." + new string('0', 32) + ".tmp",
            "short_guid" => plan.Files[0].FileName + ".123.tmp", "wrong_prefix" => "other." + Guid.NewGuid().ToString("N") + ".tmp", _ => kind };
        var path = Path.Combine(plan.DestinationDirectory, name);
        if (kind == "directory") Directory.CreateDirectory(path); else File.WriteAllText(path, "unowned");
        var before = ArchiveTestFixture.Snapshot(f.Root);
        ProductionTestFixture.Stop(Assert.Throws<ProductionStorageException>(() => f.Execute(plan)), NapIssueCodes.ProductionUnexpectedEntry);
        ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }

    [Fact]
    public void RecognizedOrphanTempIsIgnoredAndRetainedWithoutReplacingFinal()
    {
        using var f = new ProductionTestFixture(); var plan = f.Plan(); f.Prepare(plan, 0);
        var orphan = plan.Files[0].DestinationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(orphan, "not valid output and never authoritative");
        var before = File.GetLastWriteTimeUtc(orphan);
        Assert.Equal(ProductionAssetOutcome.Produced, f.Execute(plan).Outcome);
        Assert.Equal("not valid output and never authoritative", File.ReadAllText(orphan)); Assert.Equal(before, File.GetLastWriteTimeUtc(orphan));
    }

    [Theory]
    [InlineData("warning")] [InlineData("fail")] [InlineData("invalid")] [InlineData("null")]
    public void NonPassAuditStopsBeforeAnyWriteIncludingJournal(string kind)
    {
        using var f = new ProductionTestFixture(); var plan = f.Plan(); var job = f.Job();
        AiAuditReport? audit = kind switch {
            "warning" => new(AiAuditDecision.Warning, "Review.", [AiAuditTestData.Finding()]),
            "fail" => new(AiAuditDecision.Fail, "Stop.", [AiAuditTestData.Finding(AiAuditFindingSeverity.Error)]),
            "invalid" => (AiAuditReport)RuntimeHelpers.GetUninitializedObject(typeof(AiAuditReport)), _ => null };
        if (kind == "invalid") typeof(AiAuditReport).GetField("<Decision>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(audit, (AiAuditDecision)999);
        var before = ArchiveTestFixture.Snapshot(f.Root);
        var ex = Record.Exception(() => new ProductionAssetExecutor(f.Context).Execute(plan, audit!));
        Assert.IsType(kind == "null" ? typeof(ArgumentNullException) : typeof(InvalidOperationException), ex);
        ex = Record.Exception(() => new AssetExecutionCoordinator(f.Context).Execute(job, f.Package, f.Processing, f.ArchivePlan(), audit!, ProductionTestFixture.Budget));
        Assert.IsType(kind == "null" ? typeof(ArgumentNullException) : typeof(InvalidOperationException), ex);
        Assert.Equal(JobState.Planned, new JobStateStore(f.Context).Load(job).State);
        ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }

    [Theory]
    [InlineData(ArchiveMasterOutcome.Copied)] [InlineData(ArchiveMasterOutcome.IndexedExisting)] [InlineData(ArchiveMasterOutcome.AlreadyArchived)]
    public void AllPhysicallyVerifiedArchiveOutcomesAreAccepted(ArchiveMasterOutcome outcome)
    {
        using var f = new ProductionTestFixture(); var ap = f.ArchivePlan();
        if (outcome == ArchiveMasterOutcome.IndexedExisting)
        { Directory.CreateDirectory(ap.DestinationDirectory); foreach (var file in ap.Files) File.Copy(file.SourcePath, file.DestinationPath); }
        var result = new ArchiveMasterExecutor(f.Context).Execute(ap, ProductionTestFixture.Pass);
        if (outcome == ArchiveMasterOutcome.AlreadyArchived) result = f.Archive();
        Assert.Equal(outcome, result.Outcome); Assert.True(f.Plan(result).Issues.IsClean);
    }

    [Theory]
    [InlineData("asset")] [InlineData("universe")] [InlineData("relative")] [InlineData("physical_archive")]
    public void IncoherentArchivePrerequisiteIsRejected(string kind)
    {
        using var f = new ProductionTestFixture(); var result = f.Archive(); var ap = f.ArchivePlan();
        if (kind == "physical_archive") File.AppendAllText(result.FilesVerified[0].DestinationPath, "corrupt");
        else
        {
            var key = kind == "asset" ? new UniverseAssetKey(f.Context.Id, "emblem_other_002") : kind == "universe" ? new UniverseAssetKey(new UniverseId("other_world"), f.Package.AssetKey.AssetId) : ap.AssetKey;
            var relative = kind == "relative" ? "elsewhere/asset" : ap.RelativeDirectory;
            var forged = AiAuditTestData.Construct<ArchiveMasterPlan>(key, ap.AssetType, ap.ArchiveRoot, relative, ap.DestinationDirectory,
                ap.MasterDigest, ap.MasterSizeBytes, ap.Files, Array.Empty<ArchiveMasterFile>(), ap.Issues, ap.Action);
            result = AiAuditTestData.Construct<ArchiveMasterResult>(forged, ArchiveMasterOutcome.AlreadyArchived);
        }
        var before = ArchiveTestFixture.Snapshot(f.Root);
        ProductionTestFixture.Stop(Assert.Throws<ProductionStorageException>(() => f.Plan(result)), NapIssueCodes.ProductionArchiveMismatch);
        ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }

    [Fact]
    public void AiRequestRemainsLogicalAndHistoricalValidatorStillRejectsExistingDirectory()
    {
        using var f = new ProductionTestFixture();
        var repository = new ProductionRepositoryValidator().Validate(f.Context).Repository!;
        var snapshot = new ProductionRepositoryScanner().Scan(repository).Snapshot!;
        var report = new ProcessingPlanValidator().Validate(f.Processing, snapshot);
        Assert.False(report.ShouldStop);
        var audit = new AiAuditRequestBuilder().Build(f.Processing, report);
        var text = System.Text.Json.JsonSerializer.Serialize(audit);
        Assert.DoesNotContain(f.Root, text); Assert.DoesNotContain("ProductionRoot", text); Assert.DoesNotContain("SourcePath", text);
        var plan = f.Plan(); f.Execute(plan);
        var historical = new ProcessingPlanValidator().Validate(f.Processing, new ProductionRepositoryScanner().Scan(repository).Snapshot!);
        Assert.Contains(historical.Issues, i => i.Code == NapIssueCodes.PlanDestinationExists && i.StopsProcessing);
    }
}

using System.IO.Compression;
using System.Text.Json.Nodes;
using NAP.Core;
using NAP.Presentation;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace NAP.Tests;

public sealed class ImageNormalizationTests
{
    [Theory]
    [InlineData(PngColorType.Rgb, PngBitDepth.Bit8)]
    [InlineData(PngColorType.Rgb, PngBitDepth.Bit16)]
    [InlineData(PngColorType.RgbWithAlpha, PngBitDepth.Bit8)]
    [InlineData(PngColorType.RgbWithAlpha, PngBitDepth.Bit16)]
    public void AllOriginalPixelsAlphaAndEdgesRemainIdenticalAfterRoundTrip(PngColorType color, PngBitDepth depth)
    {
        using var f = new ImageNormalizationTestFixture(1586, 992, color: color, depth: depth);
        var before = ArchiveTestFixture.Snapshot(f.Root);
        var first = new ImageNormalizationEngine().Prepare(f.PngPath, ImageNormalizationGeometryTests.Rule());
        var second = new ImageNormalizationEngine().Prepare(f.PngPath, ImageNormalizationGeometryTests.Rule());
        Assert.Equal(first.GetCandidatePng(), second.GetCandidatePng());
        using var original = Image.Load<Rgba64>(f.PngPath); using var candidate = Image.Load<Rgba64>(first.GetCandidatePng());
        Assert.Equal((1592, 995), (candidate.Width, candidate.Height));
        for (var y = 0; y < candidate.Height; y++)
            for (var x = 0; x < candidate.Width; x++) Assert.Equal(original[Math.Clamp(x - 3, 0, 1585), Math.Clamp(y - 1, 0, 991)], candidate[x, y]);
        var png = new PngMasterValidator().Validate(new MemoryStream(first.GetCandidatePng())).ImageInfo!;
        Assert.Equal((byte)depth, png.BitDepth); Assert.Equal((byte)color, png.ColorType);
        Assert.Equal(original.Metadata.GetPngMetadata().Gamma, candidate.Metadata.GetPngMetadata().Gamma);
        Assert.Equal(original.Metadata.GetPngMetadata().TextData, candidate.Metadata.GetPngMetadata().TextData);
        var copy = first.GetCandidatePng(); copy[0] = 0;
        Assert.Equal(second.GetCandidatePng(), first.GetCandidatePng());
        ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }

    [Theory][InlineData(1600, 1000)][InlineData(1920, 1200)]
    public void ExactRatioIsByteExactAndDoesNotGenerateANewMaster(int width, int height)
    {
        using var f = new ImageNormalizationTestFixture(width, height);
        var proposal = f.Service.PrepareImage(f.PngPath, f.Source.Manifest.AssetId, "scene", "scene_cartography");
        Assert.False(proposal.Preview.Geometry.HasChanges); Assert.Equal("unchanged", proposal.Preview.Method);
        Assert.Equal(File.ReadAllBytes(f.PngPath), proposal.Preview.GetCandidatePng());
        var result = f.Service.Decide(proposal, ImageNormalizationTestFixture.Authorize(proposal));
        Assert.Equal("Unchanged", result.Receipt.Decision); Assert.Null(result.CandidatePath); Assert.Empty(result.Receipt.OutputSha256);
    }

    [Theory][InlineData(true)][InlineData(false)]
    public void StandaloneMasterNeedsBoundApprovalOrRejectionAndNeverOverwrites(bool approved)
    {
        using var f = new ImageNormalizationTestFixture(); var png = File.ReadAllBytes(f.PngPath); var zip = File.ReadAllBytes(f.ZipPath);
        var proposal = f.Service.PrepareImage(f.PngPath, f.Source.Manifest.AssetId, "scene", "scene_cartography");
        Assert.False(Directory.Exists(f.Service.Root));
        Assert.Throws<ImageNormalizationException>(() => f.Service.Decide(proposal, new(proposal.OperationId, new('0', 64), approved)));
        var result = f.Service.Decide(proposal, ImageNormalizationTestFixture.Authorize(proposal, approved));
        Assert.Equal(approved ? "Approved" : "Rejected", result.Receipt.Decision);
        if (approved)
        {
            Assert.Equal(proposal.Preview.GetCandidatePng(), File.ReadAllBytes(result.CandidatePath!));
            Assert.Equal(proposal.Preview.CandidateSha256, new Sha256Hasher().Compute(result.CandidatePath!).Hex);
        }
        else Assert.Null(result.CandidatePath);
        var snapshot = ArchiveTestFixture.Snapshot(f.Root);
        Assert.True(f.Service.Decide(proposal, ImageNormalizationTestFixture.Authorize(proposal, approved)).AlreadyRecorded);
        ArchiveTestFixture.AssertSnapshot(snapshot, f.Root);
        Assert.Throws<ImageNormalizationException>(() => f.Service.Decide(proposal, ImageNormalizationTestFixture.Authorize(proposal, !approved)));
        Assert.Equal(png, File.ReadAllBytes(f.PngPath)); Assert.Equal(zip, File.ReadAllBytes(f.ZipPath));
    }

    [Fact]
    public async Task UnknownNarrativeProfileIsNeverReinterpretedAndCorrectionNeedsIndependentConsent()
    {
        using var f = new ImageNormalizationTestFixture(narrative: true); var original = File.ReadAllBytes(f.ZipPath);
        Assert.Equal("normalization_profile_unknown", (await Assert.ThrowsAsync<ImageNormalizationException>(() => f.Service.PreparePackageAsync(f.ZipPath))).Code);
        var proposal = await f.Service.PreparePackageAsync(f.ZipPath, "scene_cartography");
        Assert.True(proposal.RequiresMetadataApproval);
        Assert.Equal("normalization_metadata_approval_required", Assert.Throws<ImageNormalizationException>(() => f.Service.Decide(proposal, ImageNormalizationTestFixture.Authorize(proposal))).Code);
        Assert.Empty(f.Service.Scan().Recorded);
        var result = f.Service.Decide(proposal, ImageNormalizationTestFixture.Authorize(proposal, metadata: true));
        using var zip = ZipFile.OpenRead(result.CandidatePath!);
        Assert.Equal(5, zip.Entries.Count);
        foreach (var entry in zip.Entries)
        {
            using var buffer = new MemoryStream(); using (var input = entry.Open()) input.CopyTo(buffer);
            if (entry.Name.EndsWith("_manifest.json", StringComparison.Ordinal))
            {
                var candidate = JsonNode.Parse(buffer.ToArray())!; var source = JsonNode.Parse(File.ReadAllBytes(f.Source.ManifestPath))!;
                Assert.Equal("scene_cartography", candidate["production_profile"]!.GetValue<string>());
                candidate["production_profile"] = "scene_narrative"; Assert.True(JsonNode.DeepEquals(source, candidate));
            }
            else if (!entry.Name.EndsWith(".png", StringComparison.Ordinal)) Assert.Equal(File.ReadAllBytes(Path.Combine(f.Source.PackageRoot, entry.Name)), buffer.ToArray());
        }
        Assert.Equal(original, File.ReadAllBytes(f.ZipPath)); Assert.Equal("scene_narrative", AssetManifestV2Loader.Load(f.Source.ManifestPath).ProductionProfile);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public async Task ApprovedSyntheticZipCompletesNormalAuditArchiveProductionAndCatalog(bool generic)
    {
        using var f = new ImageNormalizationTestFixture(generic ? 1499 : 1586, generic ? 1000 : 992, generic: generic);
        var originalZip = File.ReadAllBytes(f.ZipPath); var originalPng = File.ReadAllBytes(f.PngPath);
        File.WriteAllText(Path.Combine(f.Context.Storage.ProductionRoot, "legacy-sentinel.txt"), "published assets stay intact");
        var existing = ArchiveTestFixture.Snapshot(f.Context.Storage.ProductionRoot);
        var proposal = await f.Service.PreparePackageAsync(f.ZipPath);
        var result = f.Service.Decide(proposal, ImageNormalizationTestFixture.Authorize(proposal));
        var ui = new ProfessionalUiService(new OfflineAuditor());
        var prepared = await ui.PrepareNormalizedAsync(f.Context, result, default);
        Assert.Null(new JobRecoveryScanner(f.Context).Scan().Issues.Issues.FirstOrDefault(i => i.StopsProcessing));
        Assert.Equal(JobState.Planned, new JobStateStore(f.Context).Load(prepared.JobId).State);
        ArchiveTestFixture.AssertSnapshot(existing, f.Context.Storage.ProductionRoot);
        var audit = await ui.AuditAsync(f.Context, prepared, default); // Synthetic offline PASS only, never a real map approval.
        var production = await ui.ExecuteAsync(f.Context, prepared, audit, default);
        Assert.Equal(JobState.Completed, new JobStateStore(f.Context).Load(prepared.JobId).State);
        Assert.Equal(prepared.Package.AssetKey, Assert.Single(new AssetCatalog(f.Context).Query()).AssetKey);
        var master = prepared.Archive.Files.Single(file => file.Role == prepared.Package.AssetRule.Conversion!.SourceRole);
        Assert.Equal(proposal.Preview.GetCandidatePng(), File.ReadAllBytes(master.DestinationPath));
        using var webp = Image.Load<Rgba32>(production.FilesVerified.Single(file => file.Kind == ProductionAssetFileKind.GeneratedWebp).DestinationPath);
        Assert.Equal((proposal.Conversion.OutputWidth, proposal.Conversion.OutputHeight), (webp.Width, webp.Height));
        Assert.Equal(originalZip, File.ReadAllBytes(f.ZipPath)); Assert.Equal(originalPng, File.ReadAllBytes(f.PngPath));
        Assert.Equal("published assets stay intact", File.ReadAllText(Path.Combine(f.Context.Storage.ProductionRoot, "legacy-sentinel.txt")));
        var snapshot = ArchiveTestFixture.Snapshot(f.Root);
        Assert.True(f.Service.Decide(proposal, ImageNormalizationTestFixture.Authorize(proposal)).AlreadyRecorded);
        ArchiveTestFixture.AssertSnapshot(snapshot, f.Root);
    }

    [Theory]
    [InlineData("candidate-written", false)]
    [InlineData("before-publish", false)]
    [InlineData("candidate-written", true)]
    public void FailedWritesNeverExposeADefinitiveCandidateAndRetryIsSafe(string checkpoint, bool denied)
    {
        using var f = new ImageNormalizationTestFixture(); var source = File.ReadAllBytes(f.PngPath);
        var proposal = f.Service.PrepareImage(f.PngPath, f.Source.Manifest.AssetId, "scene", "scene_cartography");
        var broken = f.Fault(step => { if (step == checkpoint) { if (denied) throw new UnauthorizedAccessException("synthetic permission failure"); throw new IOException("synthetic interrupted write"); } });
        if (denied) Assert.Throws<UnauthorizedAccessException>(() => broken.Decide(proposal, ImageNormalizationTestFixture.Authorize(proposal)));
        else Assert.Throws<IOException>(() => broken.Decide(proposal, ImageNormalizationTestFixture.Authorize(proposal)));
        Assert.Equal(denied ? "normalization_access_denied" : "normalization_write_failed", Assert.Single(f.Service.Scan().Recorded).Receipt.IncidentCodes.Single());
        Assert.DoesNotContain(f.Service.Scan().Recorded, record => record.CandidatePath is not null);
        Assert.Equal(source, File.ReadAllBytes(f.PngPath));
        Assert.Equal("Approved", f.Service.Decide(proposal, ImageNormalizationTestFixture.Authorize(proposal)).Receipt.Decision);
    }

    [Fact]
    public void LostAcknowledgementAfterCommitRecoversWithoutRewriting()
    {
        using var f = new ImageNormalizationTestFixture(); var proposal = f.Service.PrepareImage(f.PngPath, f.Source.Manifest.AssetId, "scene", "scene_cartography");
        Assert.Throws<IOException>(() => f.Fault(step => { if (step == "after-publish") throw new IOException("lost acknowledgement"); }).Decide(proposal, ImageNormalizationTestFixture.Authorize(proposal)));
        var snapshot = ArchiveTestFixture.Snapshot(f.Root);
        Assert.Single(new ImageNormalizationService(f.Context).Scan().Recorded);
        Assert.True(f.Service.Decide(proposal, ImageNormalizationTestFixture.Authorize(proposal)).AlreadyRecorded);
        ArchiveTestFixture.AssertSnapshot(snapshot, f.Root);
    }

    [Fact]
    public void CancellationBeforeAtomicPublishCleansOwnedTemporariesAndNeverPublishes()
    {
        using var f = new ImageNormalizationTestFixture(); using var cancellation = new CancellationTokenSource();
        var proposal = f.Service.PrepareImage(f.PngPath, f.Source.Manifest.AssetId, "scene", "scene_cartography");
        Assert.Throws<OperationCanceledException>(() => f.Fault(step => { if (step == "before-publish") cancellation.Cancel(); }).Decide(proposal, ImageNormalizationTestFixture.Authorize(proposal), cancellation.Token));
        Assert.Equal("Cancelled", Assert.Single(f.Service.Scan().Recorded).Receipt.Decision); Assert.Empty(Directory.GetFileSystemEntries(f.Context.Storage.ProductionRoot));
    }

    [Fact]
    public async Task AlreadyCancelledPreparationDoesNotCreateStorage()
    {
        using var f = new ImageNormalizationTestFixture(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.Service.PreparePackageAsync(f.ZipPath, ct: cancellation.Token));
        Assert.False(Directory.Exists(f.Service.Root));
    }

    [Fact]
    public async Task InspectionAlwaysUsesTheHashedSnapshotEvenIfTheOriginalChangesAndReverts()
    {
        using var f = new ImageNormalizationTestFixture(); using var other = new ImageNormalizationTestFixture(1596, 1000);
        var originalZip = File.ReadAllBytes(f.ZipPath); var otherZip = File.ReadAllBytes(other.ZipPath);
        var service = f.Fault(step =>
        {
            if (step == "source-snapshotted") File.WriteAllBytes(f.ZipPath, otherZip);
            if (step == "package-inspected") File.WriteAllBytes(f.ZipPath, originalZip);
        });
        var proposal = await service.PreparePackageAsync(f.ZipPath);
        Assert.Equal(new Sha256Hasher().Compute(f.ZipPath).Hex, proposal.SourceSha256);
        Assert.Equal(new Sha256Hasher().Compute(f.PngPath).Hex, proposal.Preview.OriginalSha256);
        Assert.Equal(1598, proposal.Preview.Geometry.OriginalWidth);
        Assert.Equal(originalZip, File.ReadAllBytes(f.ZipPath));
        Assert.Empty(Directory.GetFileSystemEntries(f.Service.Root));
    }

    [Fact]
    public async Task OriginalChangesInvalidateApprovalBeforeAnyCandidateWrite()
    {
        using var f = new ImageNormalizationTestFixture(); var proposal = await f.Service.PreparePackageAsync(f.ZipPath);
        File.AppendAllText(f.ZipPath, "external mutation"); var changed = File.ReadAllBytes(f.ZipPath);
        Assert.Equal("normalization_source_changed", Assert.Throws<ImageNormalizationException>(() => f.Service.Decide(proposal, ImageNormalizationTestFixture.Authorize(proposal))).Code);
        Assert.Equal("normalization_source_changed", Assert.Single(f.Service.Scan().Recorded).Receipt.IncidentCodes.Single());
        Assert.DoesNotContain(f.Service.Scan().Recorded, record => record.CandidatePath is not null);
        Assert.Equal(changed, File.ReadAllBytes(f.ZipPath));
    }

    [Fact]
    public void ForeignContextCannotApproveAndPreexistingDestinationCannotBeAdopted()
    {
        using var f = new ImageNormalizationTestFixture(); using var other = new ImageNormalizationTestFixture();
        var proposal = f.Service.PrepareImage(f.PngPath, f.Source.Manifest.AssetId, "scene", "scene_cartography");
        Assert.Equal("normalization_approval_mismatch", Assert.Throws<ImageNormalizationException>(() => other.Service.Decide(proposal, ImageNormalizationTestFixture.Authorize(proposal))).Code);
        var collision = Directory.CreateDirectory(Path.Combine(f.Service.Root, proposal.OperationId)).FullName;
        File.WriteAllText(Path.Combine(collision, "foreign.txt"), "unowned"); var before = ArchiveTestFixture.Snapshot(f.Root);
        Assert.Throws<ImageNormalizationException>(() => f.Service.Decide(proposal, ImageNormalizationTestFixture.Authorize(proposal)));
        ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }

    [Fact]
    public void RecoveryReportsCrashResidueWithoutOpeningPromotingOrDeletingIt()
    {
        using var f = new ImageNormalizationTestFixture();
        var pending = Directory.CreateDirectory(Path.Combine(f.Service.Root, ".nap-" + Guid.NewGuid().ToString("N") + ".pending")).FullName;
        File.WriteAllText(Path.Combine(pending, "partial.png"), "truncated");
        var before = ArchiveTestFixture.Snapshot(f.Root); var scan = f.Service.Scan();
        Assert.Equal(pending, Assert.Single(scan.IncompleteDirectories)); Assert.Empty(scan.Recorded);
        ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }

    [Theory][InlineData("../escape.png")][InlineData("/absolute.png")][InlineData("nested/asset.png")][InlineData("C:/escape.png")]
    public async Task MaliciousOrNonFlatZipStopsWithoutCandidateOrEscapedFiles(string entryName)
    {
        using var f = new ImageNormalizationTestFixture(); File.Delete(f.ZipPath);
        using (var archive = ZipFile.Open(f.ZipPath, ZipArchiveMode.Create))
        { using var stream = archive.CreateEntry(entryName).Open(); stream.Write(new byte[] { 1, 2, 3 }); }
        var zip = File.ReadAllBytes(f.ZipPath);
        await Assert.ThrowsAsync<ImageNormalizationException>(() => f.Service.PreparePackageAsync(f.ZipPath));
        Assert.Equal(zip, File.ReadAllBytes(f.ZipPath)); Assert.Empty(f.Service.Scan().Recorded); Assert.Empty(Directory.GetFileSystemEntries(f.Service.Root));
    }

    [Fact]
    public async Task UnexpectedPackageFilesCannotBeRemovedToMakeAValidCandidate()
    {
        using var f = new ImageNormalizationTestFixture();
        using (var archive = ZipFile.Open(f.ZipPath, ZipArchiveMode.Update)) { using var stream = archive.CreateEntry("README.md").Open(); stream.WriteByte(1); }
        Assert.Equal("normalization_package_invalid", (await Assert.ThrowsAsync<ImageNormalizationException>(() => f.Service.PreparePackageAsync(f.ZipPath))).Code);
        Assert.Empty(f.Service.Scan().Recorded);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void CorruptOrAnimatedPngNeverProducesPreview(bool animated)
    {
        using var f = new ImageNormalizationTestFixture(); File.WriteAllBytes(f.PngPath, animated ? PackageSemanticTestFixture.Png(unsupported: true) : [137, 80, 78, 71]);
        Assert.Equal("normalization_png_invalid", Assert.Throws<ImageNormalizationException>(() => new ImageNormalizationEngine().Prepare(f.PngPath, ImageNormalizationGeometryTests.Rule())).Code);
    }
    [Fact]
    public void GrayscaleRequiresSeparateSupportedImplementation()
    {
        using var f = new ImageNormalizationTestFixture(); using (var image = new Image<L8>(16, 10)) image.SaveAsPng(f.PngPath);
        Assert.Equal("normalization_png_unsupported", Assert.Throws<ImageNormalizationException>(() => new ImageNormalizationEngine().Prepare(f.PngPath, ImageNormalizationGeometryTests.Rule())).Code);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void FileAndPixelBudgetsStopBeforeImageAllocation(bool bytes)
    {
        using var f = new ImageNormalizationTestFixture();
        var policy = bytes ? new ImageNormalizationPolicy { MaxFileBytes = 10 } : new() { MaxInputPixels = 10 };
        Assert.Equal(bytes ? "normalization_file_limit" : "normalization_pixel_limit", Assert.Throws<ImageNormalizationException>(() => new ImageNormalizationEngine().Prepare(f.PngPath, ImageNormalizationGeometryTests.Rule(), policy)).Code);
    }

    [Fact]
    public async Task CandidateTamperingBlocksRecoveryAndNormalPreparation()
    {
        using var f = new ImageNormalizationTestFixture(); var proposal = await f.Service.PreparePackageAsync(f.ZipPath);
        var result = f.Service.Decide(proposal, ImageNormalizationTestFixture.Authorize(proposal)); File.AppendAllText(result.CandidatePath!, "external mutation");
        Assert.Throws<ImageNormalizationException>(() => f.Service.ReadReceipt(proposal.OperationId)); Assert.NotEmpty(f.Service.Scan().Problems);
        await Assert.ThrowsAsync<ImageNormalizationException>(() => new ProfessionalUiService().PrepareNormalizedAsync(f.Context, result, default));
        Assert.False(Directory.Exists(f.Context.Storage.StateRoot));
    }

    private sealed class OfflineAuditor : IAiAuditClient
    { public Task<AiAuditReport> AuditAsync(AiAuditRequest request, CancellationToken cancellationToken = default) => Task.FromResult(ProductionTestFixture.Pass); }
}

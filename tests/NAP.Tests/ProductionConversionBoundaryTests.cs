using System.Buffers.Binary;
using System.Text;
using NAP.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace NAP.Tests;

public sealed class ProductionConversionBoundaryTests
{
    [Theory]
    [InlineData("invalid")] [InlineData("unsupported")] [InlineData("oversized")] [InlineData("decode_failure")]
    [InlineData("aspect_ratio")]
    public void EngineRejectsInvalidUnsupportedOversizedAndUndecodablePngBeforePublication(string kind)
    {
        using var f = new ProductionTestFixture();
        var width = kind == "oversized" ? 40_000 : kind == "aspect_ratio" ? 3 : 8;
        var height = kind == "oversized" ? 50_000 : kind == "aspect_ratio" ? 2 : 10;
        File.WriteAllBytes(f.Master, kind == "invalid" ? [1, 2, 3] : Synthetic(width, height, kind == "unsupported"));
        var conversion = new ImageConversionResolver().Resolve(f.Package, ProductionTestFixture.Budget)!;
        var before = ArchiveTestFixture.Snapshot(f.Root);
        var ex = Assert.Throws<ProductionStorageException>(() => ProductionTestFixture.Invoke(null, "ProductionImageEngine", "Generate", conversion));
        ProductionTestFixture.Stop(ex, NapIssueCodes.ProductionConversionFailed);
        if (kind == "oversized") Assert.Contains("pixel budget", ex.Issues.Issues[0].Message);
        if (kind == "decode_failure") Assert.Contains("decoded", ex.Issues.Issues[0].Message);
        ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }

    [Fact]
    public void OrientationMetadataDoesNotRotateOrCropTheFullFrame()
    {
        using var f = new ProductionTestFixture(width: 16, height: 20, quality: 100);
        using (var image = new Image<Rgba32>(16, 20))
        {
            for (var y = 0; y < 20; y++) for (var x = 0; x < 16; x++) image[x, y] = x < 8 ? new Rgba32(255, 0, 0) : new Rgba32(0, 0, 255);
            image.Metadata.ExifProfile = new ExifProfile(); image.Metadata.ExifProfile.SetValue(ExifTag.Orientation, (ushort)6);
            image.Save(f.Master, new PngEncoder());
        }
        f.Refresh(); var plan = f.Plan(); f.Execute(plan);
        using var output = Image.Load<Rgba32>(plan.Files[0].DestinationPath);
        Assert.Equal(16, output.Width); Assert.Equal(20, output.Height);
        Assert.True(output[1, 1].R > 200); Assert.True(output[14, 18].B > 200);
    }

    [Theory]
    [InlineData("truncated")] [InlineData("invalid_container")] [InlineData("dimensions")]
    public void InMemoryWebpOutputRequiresCompleteContainerDecodeAndExactDimensions(string kind)
    {
        using var f = new ProductionTestFixture(); var plan = f.Plan(); var bytes = plan.Files[0].ToArray();
        var conversion = plan.Conversion;
        if (kind == "truncated") bytes = bytes[..^1];
        if (kind == "invalid_container") bytes[0] = 0;
        if (kind == "dimensions") conversion = AiAuditTestData.Construct<ResolvedImageConversion>(conversion.AssetKey, conversion.Kind, conversion.SourceRole,
            conversion.SourcePath, 4, 5, conversion.WebpQuality, conversion.MaxInputPixels);
        var before = ArchiveTestFixture.Snapshot(f.Root);
        ProductionTestFixture.Stop(Assert.Throws<ProductionStorageException>(() => ProductionTestFixture.Invoke(null, "ProductionImageEngine", "Validate", bytes, conversion)), NapIssueCodes.ProductionOutputInvalid);
        ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }

    [Fact]
    public void SyntheticManifestRoleCannotBypassArchivePrerequisite()
    {
        using var f = new ProductionTestFixture(); var archive = f.Archive(); var inputs = new Dictionary<string, string>(f.Package.FilesByRole) { ["manifest"] = f.Package.ManifestPath };
        var forged = AiAuditTestData.Construct<ValidatedAssetPackage>(f.Package.AssetKey, f.Package.PackageRoot, f.Package.ManifestPath, f.Package.Manifest, f.Package.AssetRule, inputs);
        var before = ArchiveTestFixture.Snapshot(f.Root);
        ProductionTestFixture.Stop(Assert.Throws<ProductionStorageException>(() => new ProductionAssetPlanner(f.Context).Plan(forged, f.Processing, archive, ProductionTestFixture.Budget)), NapIssueCodes.ProductionArchiveMismatch);
        ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }

    [Theory]
    [InlineData("Production")] [InlineData("Job")]
    public void ConcurrentExecutionsCannotOverwriteOrLoseJobState(string scope)
    {
        using var f = new ProductionTestFixture(); var plan = f.Plan(); var archive = f.ArchivePlan(); var job = f.Job();
        using var start = new Barrier(2); var outcomes = new object?[2]; var errors = new Exception?[2];
        var workers = Enumerable.Range(0, 2).Select(i => new Thread(() => {
            try
            {
                if (!start.SignalAndWait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
                outcomes[i] = scope == "Production" ? new ProductionAssetExecutor(f.Context).Execute(plan, ProductionTestFixture.Pass) :
                    new AssetExecutionCoordinator(f.Context).Execute(job, f.Package, f.Processing, archive, ProductionTestFixture.Pass, ProductionTestFixture.Budget);
            }
            catch (Exception ex) { errors[i] = ex; }
        })).ToArray();
        foreach (var worker in workers) worker.Start(); foreach (var worker in workers) Assert.True(worker.Join(TimeSpan.FromSeconds(20)));
        Assert.Contains(outcomes, o => o is not null); Assert.All(errors.Where(e => e is not null), e => Assert.IsType<IOException>(e));
        Assert.Equal(5, Directory.GetFiles(plan.DestinationDirectory).Length);
        var verifiedPlan = scope == "Job" ? f.Plan(f.Archive(), jobId: job) : plan;
        Assert.Equal(ProductionAssetOutcome.AlreadyProduced, f.Execute(verifiedPlan).Outcome);
        if (scope == "Job") Assert.Equal(JobState.Completed, new JobStateStore(f.Context).Load(job).State);
    }

    private static byte[] Synthetic(int width, int height, bool unsupported)
    {
        using var stream = new MemoryStream(); stream.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13]; BinaryPrimitives.WriteInt32BigEndian(header, width); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height); header[8] = 8; header[9] = 6;
        Chunk("IHDR", header); if (unsupported) Chunk("acTL", new byte[8]); Chunk("IDAT", [1, 2, 3]); Chunk("IEND", []); return stream.ToArray();
        void Chunk(string type, byte[] content)
        {
            var number = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(number, (uint)content.Length); stream.Write(number);
            var label = Encoding.ASCII.GetBytes(type); stream.Write(label); stream.Write(content); uint crc = uint.MaxValue;
            foreach (var value in label.Concat(content)) { crc ^= value; for (var bit = 0; bit < 8; bit++) crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xedb88320; }
            BinaryPrimitives.WriteUInt32BigEndian(number, ~crc); stream.Write(number);
        }
    }
}

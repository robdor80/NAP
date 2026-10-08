using System.Text.Json.Nodes;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class ImageNormalizationSafetyTests
{
    [Theory][InlineData("source")][InlineData("output")][InlineData("workspace")]
    public async Task RealReparseAncestorsNeverReadOrWriteAnotherDirectory(string kind)
    {
        using var f = new ImageNormalizationTestFixture(); using var other = new ImageNormalizationTestFixture();
        var before = ArchiveTestFixture.Snapshot(other.Root);
        var link = kind == "output" ? f.Service.Root : Path.Combine(f.Root, "linked");
        try
        {
            if (kind == "source")
            {
                ArchiveTestFixture.Junction(link, other.Source.PackageRoot);
                Assert.Throws<ImageNormalizationException>(() => new ImageNormalizationEngine().Prepare(Path.Combine(link, Path.GetFileName(other.PngPath)), ImageNormalizationGeometryTests.Rule()));
            }
            else if (kind == "output")
            {
                ArchiveTestFixture.Junction(f.Service.Root, other.Root);
                await Assert.ThrowsAsync<ImageNormalizationException>(() => f.Service.PreparePackageAsync(f.ZipPath));
            }
            else
            {
                ArchiveTestFixture.Junction(link, other.Context.Storage.WorkspaceRoot);
                var context = new UniverseContext(f.Context.Profile, new(f.Context.Id, link, f.Context.Storage.ProductionRoot, f.Context.Storage.ArchiveRoot));
                await Assert.ThrowsAsync<ImageNormalizationException>(() => new ImageNormalizationService(context).PreparePackageAsync(f.ZipPath));
            }
            ArchiveTestFixture.AssertSnapshot(before, other.Root);
        }
        finally { if (Directory.Exists(link)) Directory.Delete(link); }
    }

    [Theory][InlineData("universe")][InlineData("classification")][InlineData("identity")][InlineData("family")]
    public async Task NormalizationCannotRepairUnrelatedManifestErrors(string kind)
    {
        using var f = new ImageNormalizationTestFixture();
        var manifest = f.Source.Manifest;
        if (kind == "universe") manifest = manifest with { UniverseId = "another_universe" };
        if (kind == "classification") manifest.Classification.Remove("culture");
        if (kind == "identity") manifest = manifest with { AssetId = "scene_another_identity_001" };
        if (kind == "family") manifest = manifest with { AssetType = "portrait", ProductionProfile = "scene_cartography" };
        File.WriteAllText(f.Source.ManifestPath, System.Text.Json.JsonSerializer.Serialize(manifest)); File.Delete(f.ZipPath);
        System.IO.Compression.ZipFile.CreateFromDirectory(f.Source.PackageRoot, f.ZipPath);
        var before = File.ReadAllBytes(f.ZipPath);
        await Assert.ThrowsAsync<ImageNormalizationException>(() => f.Service.PreparePackageAsync(f.ZipPath, "scene_cartography"));
        Assert.Equal(before, File.ReadAllBytes(f.ZipPath)); Assert.Empty(f.Service.Scan().Recorded);
    }

    [Theory][InlineData("unknown")][InlineData("null")][InlineData("geometry")][InlineData("hash")][InlineData("valid_but_wrong_hash")][InlineData("duplicate")]
    public void MalformedControlRecordsCannotAuthorizeOrRecoverCandidates(string corruption)
    {
        using var f = new ImageNormalizationTestFixture(); var proposal = f.Service.PrepareImage(f.PngPath, f.Source.Manifest.AssetId, "scene", "scene_cartography");
        var result = f.Service.Decide(proposal, ImageNormalizationTestFixture.Authorize(proposal));
        var path = Path.Combine(result.DirectoryPath, "control", "receipt.json"); var node = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        if (corruption == "unknown") node["unsafe_field"] = "unexpected";
        if (corruption == "null") node["geometry"] = null;
        if (corruption == "geometry") node["geometry"]!["left"] = 99;
        if (corruption == "hash") node["candidate_png_sha256"] = "invalid";
        if (corruption == "valid_but_wrong_hash") node["candidate_png_sha256"] = new string('0', 64);
        var text = node.ToJsonString();
        if (corruption == "duplicate") text = text[..^1] + ",\"schema_version\":1}";
        File.WriteAllText(path, text); var before = ArchiveTestFixture.Snapshot(f.Root);
        Assert.Throws<ImageNormalizationException>(() => f.Service.ReadReceipt(proposal.OperationId)); Assert.NotEmpty(f.Service.Scan().Problems);
        ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }

    [Fact]
    public void RealFilesystemCollisionDuringControlWritingCleansOnlyOwnedTemporary()
    {
        using var f = new ImageNormalizationTestFixture(); var proposal = f.Service.PrepareImage(f.PngPath, f.Source.Manifest.AssetId, "scene", "scene_cartography");
        var service = f.Fault(step =>
        {
            if (step == "candidate-written")
            {
                var temporary = Directory.GetDirectories(f.Service.Root, ".nap-*.pending").Single();
                File.WriteAllText(Path.Combine(temporary, "control"), "file blocks directory creation");
            }
        });
        Assert.Throws<IOException>(() => service.Decide(proposal, ImageNormalizationTestFixture.Authorize(proposal)));
        Assert.Equal("Failed", Assert.Single(f.Service.Scan().Recorded).Receipt.Decision); Assert.True(File.Exists(f.PngPath));
    }

    [Fact]
    public void OriginalBytesChangedDuringFinalCopyPreventAtomicPublication()
    {
        using var f = new ImageNormalizationTestFixture(); var proposal = f.Service.PrepareImage(f.PngPath, f.Source.Manifest.AssetId, "scene", "scene_cartography");
        var service = f.Fault(step => { if (step == "candidate-written") File.AppendAllText(f.PngPath, "external mutation"); });
        Assert.Equal("normalization_source_changed", Assert.Throws<ImageNormalizationException>(() => service.Decide(proposal, ImageNormalizationTestFixture.Authorize(proposal))).Code);
        Assert.Equal("normalization_source_changed", Assert.Single(f.Service.Scan().Recorded).Receipt.IncidentCodes.Single());
    }
}

using System.IO.Compression;
using System.Reflection;
using NAP.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace NAP.Tests;

/// <summary>Synthetic pixels and isolated storage only; never opens a real map or host NAP settings.</summary>
internal sealed class ImageNormalizationTestFixture : IDisposable
{
    internal PackageSemanticTestFixture Source { get; }
    internal UniverseContext Context => Source.Context;
    internal string Root => Source.Root;
    internal string PngPath { get; }
    internal string ZipPath { get; }
    internal ImageNormalizationService Service => new(Context);
    internal ImageNormalizationTestFixture(int width = 1598, int height = 1000, bool narrative = false, bool generic = false,
        PngColorType color = PngColorType.RgbWithAlpha, PngBitDepth depth = PngBitDepth.Bit8)
    {
        UniverseProfile? profile = null;
        if (generic)
            profile = new(new("normalization_fixture"), "Synthetic diagrams", ["material"],
                [new UniverseAssetRule("diagram", "schematic", ["material"], [],
                    [new("source_image", "_master", ".png", true, "png_master"), new("notes", "_notes", ".md", true)],
                    new([AssetRouteSegment.Literal("diagrams"), AssetRouteSegment.AssetId()]), new(ImageConversionKind.PngToWebp, "source_image", 300, 200, 87))]);
        Source = new(profile, generic ? "diagram_synthetic_001" : "scene_synthetic_test_map_001", generic ? "diagram" : "scene", generic ? "schematic" : "scene_cartography");
        if (!generic) { Source.Manifest.Classification["culture"] = "test_culture"; Source.Manifest.Classification["location"] = "test_location"; }
        if (narrative) Source.Manifest = Source.Manifest with { ProductionProfile = "scene_narrative" };
        Source.WriteManifest();
        foreach (var file in Context.Profile.AssetRules.Single(r => r.ProductionProfile == (generic ? "schematic" : "scene_cartography")).PackageFiles.Where(f => f.ContentValidator is null))
            File.WriteAllText(Source.PathFor(file), file.Extension == ".json" ? "{\"synthetic_test\":true}" : "# Synthetic document; unchanged bytes\n");
        PngPath = Path.Combine(Source.PackageRoot, Source.Manifest.AssetId + (generic ? "_master" : "") + ".png");
        Pattern(PngPath, width, height, color, depth);
        foreach (var path in new[] { Context.Storage.WorkspaceRoot, Context.Storage.ProductionRoot, Context.Storage.ArchiveRoot, Context.Storage.InboxRoot }) Directory.CreateDirectory(path);
        ZipPath = Path.Combine(Context.Storage.InboxRoot, Source.Manifest.AssetId + ".zip");
        ZipFile.CreateFromDirectory(Source.PackageRoot, ZipPath);
    }
    internal static void Pattern(string path, int width, int height, PngColorType color, PngBitDepth depth)
    {
        using var image = new Image<Rgba64>(width, height);
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                image[x, y] = new((ushort)((x * 257 + y * 3) % 65536), (ushort)((y * 257 + x * 17) % 65536), (ushort)((x * 191 + y * 13) % 65536),
                    (ushort)(color == PngColorType.Rgb ? 65535 : (x + y) % 5 == 0 ? 0 : 65535));
        image.Metadata.GetPngMetadata().Gamma = 0.45455f;
        image.Metadata.GetPngMetadata().TextData.Add(new("Description", "Synthetic fixture", "", ""));
        image.Save(path, new PngEncoder { ColorType = color, BitDepth = depth, TransparentColorMode = PngTransparentColorMode.Preserve });
    }
    internal static ImageNormalizationAuthorization Authorize(ImageNormalizationProposal p, bool approved = true, bool metadata = false) => new(p.OperationId, p.EvidenceSha256, approved, metadata);
    internal ImageNormalizationService Fault(Action<string> checkpoint) => (ImageNormalizationService)Activator.CreateInstance(typeof(ImageNormalizationService),
        BindingFlags.Instance | BindingFlags.NonPublic, null, [Context, new ImageNormalizationPolicy(), checkpoint], null)!;
    public void Dispose() => Source.Dispose();
}

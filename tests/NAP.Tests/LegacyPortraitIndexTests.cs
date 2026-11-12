using System.Security.Cryptography;
using NAP.Core;
using NAP.Presentation;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace NAP.Tests;

public sealed class LegacyPortraitIndexTests
{
    [Fact]
    public async Task HistoricalPortraitIsSqliteIndexedAndVisibleThroughTheNormalCatalog()
    {
        using var f = new CatalogTestFixture(nimroel: true);
        f.Catalog.Initialize();
        var nativeDbBefore = SHA256.HashData(File.ReadAllBytes(f.Catalog.CatalogPath));
        var assetId = "portrait_treskal_farmer_male_001";
        var assetsRoot = Path.Combine(f.Root, "legacy_assets");
        var folder = Path.Combine(assetsRoot, "portraits", "norgard", "treskal",
            "farmer", "male", assetId);
        Directory.CreateDirectory(folder);
        var webp = Path.Combine(folder, assetId + ".webp");
        using (var image = new Image<Rgba32>(768, 960)) image.Save(webp, new WebpEncoder());
        File.WriteAllText(Path.Combine(folder, assetId + "_prompt.md"), "Retrato histórico");
        File.WriteAllText(Path.Combine(folder, assetId + "_info.md"), "Documento original");
        File.WriteAllText(Path.Combine(folder, assetId + "_visual_identity.json"),
            "{\"asset_id\":\"" + assetId + "\",\"identity\":{\"culture\":\"Norgard\",\"location\":\"Treskal\",\"profession\":\"farmer\",\"sex\":\"male\"},\"eyes\":{\"color\":{\"value\":\"brown\"}}}");
        var index = new LegacyPortraitIndex(f.Context);
        Assert.Equal(1, index.Import(assetsRoot));
        Assert.True(File.Exists(index.DatabasePath));
        Assert.Equal(nativeDbBefore, SHA256.HashData(File.ReadAllBytes(f.Catalog.CatalogPath)));

        var service = new ExplorerService();
        var page = await service.PageAsync(f.Context, new(), 0, 60, default);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(assetId, page.Items.Single().AssetKey.AssetId);
        var filter = new CatalogFilter(classification: new Dictionary<string, string>
            { ["role"] = "farmer", ["location"] = "treskal" });
        Assert.Single((await service.PageAsync(f.Context, filter, 0, 60, default)).Items);
        Assert.Equal(1, (await service.StatisticsAsync(f.Context, new(), default)).TotalAssets);
        Assert.Null(await service.DetailAsync(f.Context, assetId, default));
        Assert.NotNull(await service.LegacyDetailAsync(f.Context, assetId, default));
        Assert.True(File.Exists((await service.ThumbnailAsync(f.Context, page.Items.Single(), default)).CachePath));
        Assert.Equal(nativeDbBefore, SHA256.HashData(File.ReadAllBytes(f.Catalog.CatalogPath)));
        Assert.Empty(f.Catalog.Query()); // The certified schema-v1 native catalog was never falsified.
    }

    [Fact]
    public void MissingLegacyAssetsFailWithoutCreatingAnIndex()
    {
        using var f = new CatalogTestFixture(nimroel: true);
        f.Catalog.Initialize();
        var index = new LegacyPortraitIndex(f.Context);
        Assert.Throws<DirectoryNotFoundException>(() => index.Import(Path.Combine(f.Root, "missing")));
        Assert.False(File.Exists(index.DatabasePath));
    }
}

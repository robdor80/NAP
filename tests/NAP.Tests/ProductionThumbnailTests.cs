using NAP.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace NAP.Tests;

public sealed class ProductionThumbnailTests
{
    private static CatalogAssetSummary Publish(CatalogTestFixture f)
    { f.Publish(automatic: true); return new CatalogExplorerReader(f.Context).Page().Items.Single(); }
    [Fact]
    public async Task RealWebpCacheRoundTripKeepsCanonicalSourcesUnchangedAndNeedsNoMaster()
    {
        using var f = new CatalogTestFixture(); var asset = Publish(f); var sources = f.Sources(); var cache = new ProductionThumbnailCache(f.Context);
        var first = await cache.GetAsync(asset); Assert.False(first.CacheHit); Assert.StartsWith(f.Context.Storage.CacheRoot + Path.DirectorySeparatorChar, first.CachePath, StringComparison.Ordinal);
        using (var image = Image.Load(first.CachePath)) { Assert.Equal(8, image.Width); Assert.Equal(10, image.Height); }
        var bytes = File.ReadAllBytes(first.CachePath); var time = File.GetLastWriteTimeUtc(first.CachePath);
        var second = await cache.GetAsync(asset); Assert.True(second.CacheHit); Assert.Equal(bytes, File.ReadAllBytes(second.CachePath)); Assert.Equal(time, File.GetLastWriteTimeUtc(second.CachePath)); f.AssertSources(sources);
        foreach (var path in Directory.EnumerateFiles(f.Context.Storage.ArchiveRoot, "*.png", SearchOption.AllDirectories)) File.Delete(path);
        Assert.True((await cache.GetAsync(asset)).CacheHit);
    }
    [Theory] [InlineData(800, 400, 320, 160)] [InlineData(400, 800, 160, 320)] [InlineData(401, 601, 214, 320)] [InlineData(20, 20, 20, 20)]
    public async Task FullFramePreservedAcrossGenericRatios(int w, int h, int expectedWidth, int expectedHeight)
    {
        using var f = new CatalogTestFixture(); var original = Publish(f); var path = Path.Combine(f.Context.Storage.ProductionRoot, original.ProductionRelativePath.Replace('/', Path.DirectorySeparatorChar));
        using (var image = new Image<Rgba32>(w, h, Color.Red.ToPixel<Rgba32>()))
        { for (var y = 0; y < h; y++) for (var x = w / 2; x < w; x++) image[x, y] = Color.Blue.ToPixel<Rgba32>(); image.Save(path, new WebpEncoder { FileFormat = WebpFileFormatType.Lossless }); }
        var asset = original with { ProductionDigest = new Sha256Hasher().Compute(path), ProductionSizeBytes = new FileInfo(path).Length };
        var result = await new ProductionThumbnailCache(f.Context).GetAsync(asset); Assert.Equal(expectedWidth, result.Width); Assert.Equal(expectedHeight, result.Height);
        using var thumbnail = Image.Load<Rgba32>(result.CachePath); Assert.True(thumbnail[0, 0].R > 200); Assert.True(thumbnail[thumbnail.Width - 1, thumbnail.Height - 1].B > 200);
        Assert.True(Math.Abs((double)thumbnail.Width / thumbnail.Height - (double)w / h) <= 1d / thumbnail.Height);
    }
    [Fact] public async Task DifferentFingerprintGetsDifferentCacheKey()
    {
        using var f = new CatalogTestFixture(); var asset = Publish(f); var cache = new ProductionThumbnailCache(f.Context); var first = await cache.GetAsync(asset);
        var source = Path.Combine(f.Context.Storage.ProductionRoot, asset.ProductionRelativePath.Replace('/', Path.DirectorySeparatorChar));
        using (var image = new Image<Rgba32>(8, 10, Color.Blue.ToPixel<Rgba32>())) image.Save(source, new WebpEncoder { Quality = 70 });
        var changed = asset with { ProductionDigest = new Sha256Hasher().Compute(source), ProductionSizeBytes = new FileInfo(source).Length };
        Assert.NotEqual(cache.CacheKey(asset), cache.CacheKey(changed)); var next = await cache.GetAsync(changed); Assert.NotEqual(first.CachePath, next.CachePath); Assert.False(next.CacheHit);
    }
    [Fact] public async Task ModifiedSourceStopsEvenWhenCached()
    {
        using var f = new CatalogTestFixture(); var asset = Publish(f); var cache = new ProductionThumbnailCache(f.Context); await cache.GetAsync(asset);
        var path = Path.Combine(f.Context.Storage.ProductionRoot, asset.ProductionRelativePath.Replace('/', Path.DirectorySeparatorChar)); var bytes = File.ReadAllBytes(path); bytes[^1] ^= 1; File.WriteAllBytes(path, bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => cache.GetAsync(asset));
    }
    [Fact] public async Task MissingSourceDoesNotReturnCachedPreview()
    { using var f = new CatalogTestFixture(); var a = Publish(f); var cache = new ProductionThumbnailCache(f.Context); await cache.GetAsync(a); File.Delete(Path.Combine(f.Context.Storage.ProductionRoot, a.ProductionRelativePath.Replace('/', Path.DirectorySeparatorChar))); await Assert.ThrowsAsync<FileNotFoundException>(() => cache.GetAsync(a)); }
    [Fact] public async Task CorruptCacheRegenerates()
    { using var f = new CatalogTestFixture(); var a = Publish(f); var cache = new ProductionThumbnailCache(f.Context); var first = await cache.GetAsync(a); File.WriteAllText(first.CachePath, "corrupt"); var next = await cache.GetAsync(a); Assert.False(next.CacheHit); using var image = Image.Load(next.CachePath); Assert.Equal(8, image.Width); }
    [Fact] public async Task GridAndDetailCanRequestSameMissingThumbnailConcurrently()
    {
        using var f = new CatalogTestFixture(); var a = Publish(f);
        var results = await Task.WhenAll(new ProductionThumbnailCache(f.Context).GetAsync(a), new ProductionThumbnailCache(f.Context).GetAsync(a));
        Assert.Equal(results[0].CachePath, results[1].CachePath); using var image = Image.Load(results[0].CachePath); Assert.Equal(8, image.Width);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(results[0].CachePath)!, "*.tmp"));
    }
    [Fact] public async Task CancellationCreatesNoThumbnail()
    { using var f = new CatalogTestFixture(); var a = Publish(f); using var ct = new CancellationTokenSource(); ct.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ProductionThumbnailCache(f.Context).GetAsync(a, ct.Token)); Assert.False(Directory.Exists(Path.Combine(f.Context.Storage.CacheRoot, "thumbnails"))); }
    [Fact] public async Task WrongUniverseRejected()
    { using var f = new CatalogTestFixture(); var a = Publish(f); using var other = new CatalogTestFixture(universe: "another"); await Assert.ThrowsAsync<InvalidDataException>(() => new ProductionThumbnailCache(other.Context).GetAsync(a)); Assert.NotEqual(new ProductionThumbnailCache(f.Context).CacheKey(a), new ProductionThumbnailCache(other.Context).CacheKey(a with { AssetKey = new(other.Context.Id, a.AssetKey.AssetId) })); }
    [Theory] [InlineData("../../emblem_example_001.webp")] [InlineData("/emblem_example_001.webp")] [InlineData(".git/emblem_example_001.webp")] [InlineData("icons/../emblem_example_001.webp")] [InlineData("icons\\emblem_example_001.webp")]
    public async Task TraversalAndProtectedPathsRejected(string path)
    {
        using var f = new CatalogTestFixture(); var a = Publish(f) with { ProductionRelativePath = path };
        var error = await Record.ExceptionAsync(() => new ProductionThumbnailCache(f.Context).GetAsync(a));
        Assert.NotNull(error); Assert.True(error is ProductionStorageException or InvalidDataException);
    }
    [Fact] public async Task PngSourceRejectedRegardlessOfFingerprint()
    { using var f = new CatalogTestFixture(); var a = Publish(f) with { ProductionRelativePath = "images/emblem_example_001.png" }; await Assert.ThrowsAsync<InvalidDataException>(() => new ProductionThumbnailCache(f.Context).GetAsync(a)); }
    [Fact] public async Task ForgedWebpContainingPngIsRejected()
    {
        using var f = new CatalogTestFixture(); var a = Publish(f); var path = Path.Combine(f.Context.Storage.ProductionRoot, a.ProductionRelativePath.Replace('/', Path.DirectorySeparatorChar));
        using (var image = new Image<Rgba32>(8, 10)) image.SaveAsPng(path);
        a = a with { ProductionDigest = new Sha256Hasher().Compute(path), ProductionSizeBytes = new FileInfo(path).Length };
        await Assert.ThrowsAnyAsync<Exception>(() => new ProductionThumbnailCache(f.Context).GetAsync(a));
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task CacheAndSourceLinksRejected(bool sourceLink)
    {
        using var f = new CatalogTestFixture(); var a = Publish(f); var target = Directory.CreateDirectory(Path.Combine(f.Root, "external")).FullName;
        var link = sourceLink ? Path.Combine(f.Context.Storage.ProductionRoot, "linked") : Path.Combine(f.Context.Storage.CacheRoot, "thumbnails");
        ArchiveTestFixture.Junction(link, target);
        try { if (sourceLink) a = a with { ProductionRelativePath = "linked/emblem_example_001.webp" }; await Assert.ThrowsAsync<ProductionStorageException>(() => new ProductionThumbnailCache(f.Context).GetAsync(a)); Assert.Empty(Directory.GetFiles(target)); }
        finally { Directory.Delete(link); }
    }
}

using System.Security.Cryptography;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace NAP.Core;

public sealed record ThumbnailResult(string CachePath, int Width, int Height, bool CacheHit);

/// <summary>On-demand PNG previews of catalog-fingerprinted production WebP only. No master input API.</summary>
public sealed class ProductionThumbnailCache(UniverseContext context)
{
    public const int Bound = 320;
    private static readonly SemaphoreSlim Workers = new(2, 2);
    private static readonly SemaphoreSlim Publications = new(1, 1);
    public async Task<ThumbnailResult> GetAsync(CatalogAssetSummary asset, CancellationToken cancellation = default)
    {
        if (asset.AssetKey.UniverseId != context.Id) throw new InvalidDataException("Thumbnail universe mismatch.");
        if (!asset.ProductionRelativePath.EndsWith("/" + asset.AssetKey.AssetId + ".webp", StringComparison.Ordinal))
            throw new InvalidDataException("Only cataloged production WebP is a thumbnail source.");
        await Workers.WaitAsync(cancellation).ConfigureAwait(false);
        try { return await Task.Run(() => GenerateAsync(asset, cancellation), cancellation).ConfigureAwait(false); }
        finally { Workers.Release(); }
    }
    public string CacheKey(CatalogAssetSummary asset) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonKey(asset)))).ToLowerInvariant();
    private static string JsonKey(CatalogAssetSummary asset) => System.Text.Json.JsonSerializer.Serialize(new
    { version = 1, bound = Bound, universe = asset.AssetKey.UniverseId.Value, id = asset.AssetKey.AssetId,
        source = asset.ProductionRelativePath, sha256 = asset.ProductionDigest.Hex, size = asset.ProductionSizeBytes });
    private async Task<ThumbnailResult> GenerateAsync(CatalogAssetSummary asset, CancellationToken cancellation)
    {
        ProductionStorageRootValidator.Require(context);
        if (ProductionPaths.Overlaps(context.Storage.CacheRoot, context.Storage.ProductionRoot) || ProductionPaths.Overlaps(context.Storage.CacheRoot, context.Storage.ArchiveRoot))
            throw new InvalidDataException("Thumbnail cache overlaps canonical roots.");
        var source = ProductionPaths.Resolve(context.Storage.ProductionRoot, asset.ProductionRelativePath);
        ProductionPaths.CheckPath(source, NapIssueCodes.ProductionSourceChanged);
        var cache = Path.Combine(context.Storage.CacheRoot, "thumbnails");
        ProductionPaths.CheckPath(cache, NapIssueCodes.CatalogInvalid, NapIssueCodes.CatalogInvalid);
        var target = Path.Combine(cache, CacheKey(asset) + ".png");
        // Verify the source even on a cache hit: a modified/missing external file never produces a stale success.
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
        if (input.Length != asset.ProductionSizeBytes || input.Length is <= 0 or > 134217728) throw new InvalidDataException("Thumbnail source size mismatch or limit exceeded.");
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellation).ConfigureAwait(false)).ToLowerInvariant();
        if (hash != asset.ProductionDigest.Hex) throw new InvalidDataException("Thumbnail source fingerprint changed.");
        input.Position = 0;
        var info = await WebpDecoder.Instance.IdentifyAsync(new DecoderOptions { MaxFrames = 1 }, input, cancellation).ConfigureAwait(false);
        if ((long)info.Width * info.Height > 64_000_000 || info.Width < 1 || info.Height < 1) throw new InvalidDataException("Thumbnail source pixel limit exceeded.");
        var scale = Math.Min(1d, (double)Bound / Math.Max(info.Width, info.Height));
        var width = Math.Max(1, (int)Math.Round(info.Width * scale)); var height = Math.Max(1, (int)Math.Round(info.Height * scale));
        ProductionPaths.CheckPath(target, NapIssueCodes.CatalogInvalid, NapIssueCodes.CatalogInvalid);
        if (File.Exists(target))
        {
            try
            {
                await using var cached = File.OpenRead(target);
                if (cached.Length > 2 * 1024 * 1024) throw new InvalidDataException("Thumbnail cache size limit exceeded.");
                var cachedInfo = await PngDecoder.Instance.IdentifyAsync(new DecoderOptions { MaxFrames = 1 }, cached, cancellation).ConfigureAwait(false);
                if (cachedInfo.Width != width || cachedInfo.Height != height) throw new InvalidDataException("Thumbnail cache geometry changed.");
                cached.Position = 0;
                using var image = await PngDecoder.Instance.DecodeAsync(new DecoderOptions { MaxFrames = 1 }, cached, cancellation).ConfigureAwait(false);
                if (image.Width == width && image.Height == height) return new(target, width, height, true);
            }
            catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException or InvalidDataException) { /* Regenerable cache only. */ }
        }
        cancellation.ThrowIfCancellationRequested(); input.Position = 0;
        using var decoded = await WebpDecoder.Instance.DecodeAsync(new DecoderOptions { MaxFrames = 1 }, input, cancellation).ConfigureAwait(false);
        decoded.Mutate(x => x.Resize(new ResizeOptions { Size = new(width, height), Mode = ResizeMode.Stretch, Sampler = KnownResamplers.Lanczos3 }));
        input.Position = 0;
        if (Convert.ToHexString(await SHA256.HashDataAsync(input, cancellation).ConfigureAwait(false)).ToLowerInvariant() != hash)
            throw new InvalidDataException("Thumbnail source changed during decode.");
        ProductionPaths.CheckPath(cache, NapIssueCodes.CatalogInvalid, NapIssueCodes.CatalogInvalid);
        Directory.CreateDirectory(cache);
        var temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await Publications.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            using var lease = ExecutionMutex.Acquire("Thumbnail", cache, CacheKey(asset));
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { await decoded.SaveAsync(output, new PngEncoder { CompressionLevel = PngCompressionLevel.Level6 }, cancellation).ConfigureAwait(false); output.Flush(true); }
            cancellation.ThrowIfCancellationRequested();
            ProductionPaths.CheckPath(target, NapIssueCodes.CatalogInvalid, NapIssueCodes.CatalogInvalid);
            File.Move(temp, target, overwrite: true);
        }
        finally
        {
            try
            {
                ProductionPaths.CheckPath(temp, NapIssueCodes.CatalogInvalid, NapIssueCodes.CatalogInvalid);
                if (File.Exists(temp)) File.Delete(temp);
            }
            finally { Publications.Release(); }
        }
        return new(target, width, height, false);
    }
}

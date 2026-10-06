using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace NAP.Core;

/// <summary>Small generic engine for the resolved PngToWebp capability; historical Portrait/Scene contracts stay untouched.</summary>
internal static class ProductionImageEngine
{
    internal static byte[] Generate(ResolvedImageConversion conversion)
    {
        if (conversion.Kind != ImageConversionKind.PngToWebp)
            throw ProductionStorageException.Stop(NapIssueCodes.ProductionConversionFailed, "The resolved conversion kind is unsupported.");
        if (!ProductionPaths.FileExists(conversion.SourcePath, NapIssueCodes.ProductionSourceChanged))
            throw ProductionStorageException.Stop(NapIssueCodes.ProductionSourceChanged, "The conversion source is missing.", conversion.SourcePath);
        using var input = new FileStream(conversion.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var validation = new PngMasterValidator().Validate(input);
        if (!validation.IsValid) throw Failure("The conversion source is not a supported validated PNG.");
        var info = validation.ImageInfo!;
        if ((long)info.Width * info.Height > conversion.MaxInputPixels) throw Failure("The source exceeds the runtime pixel budget.");
        if (new ImageConversionGeometryValidator().Validate(info, conversion).ShouldStop)
            throw Failure("The source/output ratio must be exact; full-frame conversion is required.");
        input.Position = 0;
        Image<Rgba32> decoded;
        try { decoded = PngDecoder.Instance.Decode<Rgba32>(new DecoderOptions { SkipMetadata = true }, input); }
        catch (ImageFormatException ex) { throw Failure("The PNG source could not be decoded.", ex); }
        using (decoded)
        {
            if (decoded.Width != info.Width || decoded.Height != info.Height) throw Failure("The decoded PNG dimensions differ from its validated header.");
            if (decoded.Width != conversion.OutputWidth || decoded.Height != conversion.OutputHeight)
                decoded.Mutate(c => c.Resize(new ResizeOptions { Size = new Size(conversion.OutputWidth, conversion.OutputHeight),
                    Mode = ResizeMode.Stretch, Sampler = KnownResamplers.Lanczos3 }));
            using var output = new MemoryStream();
            decoded.Save(output, new WebpEncoder { FileFormat = WebpFileFormatType.Lossy, Quality = conversion.WebpQuality });
            var bytes = output.ToArray();
            Validate(bytes, conversion);
            return bytes;
        }
        ProductionStorageException Failure(string message, Exception? inner = null) =>
            ProductionStorageException.Stop(NapIssueCodes.ProductionConversionFailed, message, conversion.SourcePath, inner);
    }

    internal static void Validate(byte[] bytes, ResolvedImageConversion conversion)
    {
        if (!WebpContainerValidator.IsComplete(bytes)) throw Invalid("The output is not a complete WebP container.");
        using var input = new MemoryStream(bytes, writable: false);
        Image<Rgba32> decoded;
        try { decoded = WebpDecoder.Instance.Decode<Rgba32>(new DecoderOptions { SkipMetadata = true }, input); }
        catch (ImageFormatException ex) { throw Invalid("The WebP output cannot be decoded.", ex); }
        using (decoded)
            if (decoded.Width != conversion.OutputWidth || decoded.Height != conversion.OutputHeight)
                throw Invalid("Decoded WebP dimensions differ from the resolved conversion.");
        ProductionStorageException Invalid(string message, Exception? inner = null) => ProductionStorageException.Stop(NapIssueCodes.ProductionOutputInvalid, message, inner: inner);
    }
}

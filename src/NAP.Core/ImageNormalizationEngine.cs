using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace NAP.Core;

/// <summary>Immutable preview evidence. Public byte access returns copies, never the approved buffer.</summary>
public sealed class ImageNormalizationPreview
{
    private readonly byte[] _original, _candidate;
    internal ImageNormalizationPreview(byte[] original, byte[] candidate, ImageNormalizationGeometry geometry)
    { _original = original; _candidate = candidate; Geometry = geometry; OriginalSha256 = ImageNormalizationEngine.Hash(original); CandidateSha256 = ImageNormalizationEngine.Hash(candidate); }
    public ImageNormalizationGeometry Geometry { get; }
    public string OriginalSha256 { get; }
    public string CandidateSha256 { get; }
    public string Method => Geometry.HasChanges ? "nearest-edge-canvas-v1" : "unchanged";
    public byte[] GetOriginalPng() => (byte[])_original.Clone();
    public byte[] GetCandidatePng() => (byte[])_candidate.Clone();
    internal ReadOnlySpan<byte> Candidate => _candidate;
}

/// <summary>Read-only preparation, integer geometry and nearest-edge extension; never resamples or writes a source.</summary>
public sealed class ImageNormalizationEngine
{
    public ImageNormalizationPreview Prepare(string sourcePath, ImageConversionRule rule,
        ImageNormalizationPolicy? policy = null, CancellationToken cancellation = default)
    {
        policy ??= new(); policy.Validate();
        return Prepare(ImageNormalizationFiles.Read(sourcePath, policy.MaxFileBytes, cancellation), rule, policy, cancellation);
    }

    internal ImageNormalizationPreview Prepare(byte[] source, ImageConversionRule rule, ImageNormalizationPolicy policy, CancellationToken ct)
    {
        policy.Validate(); ct.ThrowIfCancellationRequested();
        if (source.LongLength > policy.MaxFileBytes) throw ImageNormalizationException.Stop("normalization_file_limit", "El PNG supera el límite de bytes.");
        using var input = new MemoryStream(source, false);
        var validation = new PngMasterValidator().Validate(input);
        if (!validation.IsValid) throw ImageNormalizationException.Stop("normalization_png_invalid", "El maestro no es un PNG estático válido: " + validation.Reason);
        var info = validation.ImageInfo!;
        if (info.ColorType is not (2 or 6) || info.BitDepth is not (8 or 16))
            throw ImageNormalizationException.Stop("normalization_png_unsupported", "La normalización v1 admite únicamente PNG RGB/RGBA de 8 o 16 bits.");
        var geometry = ImageNormalizationGeometry.Calculate(info, rule, policy);
        try
        {
            input.Position = 0;
            using var original = Image.Load<Rgba64>(input);
            if (original.Frames.Count != 1 || original.Width != info.Width || original.Height != info.Height)
                throw ImageNormalizationException.Stop("normalization_decode_invalid", "El decode no coincide con el maestro validado.");
            ct.ThrowIfCancellationRequested();
            if (!geometry.HasChanges) return new((byte[])source.Clone(), (byte[])source.Clone(), geometry);
            using var canvas = new Image<Rgba64>(geometry.CanvasWidth, geometry.CanvasHeight);
            CopyMetadata(original, canvas);
            for (var y = 0; y < canvas.Height; y++)
            {
                ct.ThrowIfCancellationRequested();
                var sourceY = Math.Clamp(y - geometry.Top, 0, original.Height - 1);
                for (var x = 0; x < canvas.Width; x++) canvas[x, y] = original[Math.Clamp(x - geometry.Left, 0, original.Width - 1), sourceY];
            }
            using var output = new MemoryStream();
            canvas.Save(output, new PngEncoder { BitDepth = (PngBitDepth)info.BitDepth, ColorType = (PngColorType)info.ColorType,
                TransparentColorMode = PngTransparentColorMode.Preserve, InterlaceMethod = PngInterlaceMode.None });
            if (output.Length > policy.MaxFileBytes) throw ImageNormalizationException.Stop("normalization_file_limit", "El candidato supera el límite de bytes.");
            var bytes = output.ToArray();
            output.Position = 0;
            if (!new PngMasterValidator().Validate(output).IsValid) throw ImageNormalizationException.Stop("normalization_output_invalid", "El PNG candidato no pasa la validación.");
            using var verified = Image.Load<Rgba64>(bytes);
            for (var y = 0; y < verified.Height; y++)
            {
                ct.ThrowIfCancellationRequested();
                for (var x = 0; x < verified.Width; x++)
                    if (verified[x, y] != original[Math.Clamp(x - geometry.Left, 0, original.Width - 1), Math.Clamp(y - geometry.Top, 0, original.Height - 1)])
                        throw ImageNormalizationException.Stop("normalization_pixel_mismatch", "El candidato no conserva exactamente todos los píxeles y bordes.");
            }
            return new((byte[])source.Clone(), bytes, geometry);
        }
        catch (ImageFormatException ex) { throw ImageNormalizationException.Stop("normalization_decode_invalid", "El maestro no se puede decodificar con seguridad.", ex); }
    }

    internal static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

    private static void CopyMetadata(Image<Rgba64> source, Image<Rgba64> target)
    {
        target.Metadata.HorizontalResolution = source.Metadata.HorizontalResolution;
        target.Metadata.VerticalResolution = source.Metadata.VerticalResolution;
        target.Metadata.ResolutionUnits = source.Metadata.ResolutionUnits;
        target.Metadata.IccProfile = source.Metadata.IccProfile?.DeepClone();
        target.Metadata.ExifProfile = source.Metadata.ExifProfile?.DeepClone();
        target.Metadata.XmpProfile = source.Metadata.XmpProfile?.DeepClone();
        var from = source.Metadata.GetPngMetadata(); var to = target.Metadata.GetPngMetadata();
        to.Gamma = from.Gamma; to.TransparentColor = from.TransparentColor;
        foreach (var text in from.TextData) to.TextData.Add(text);
    }
}

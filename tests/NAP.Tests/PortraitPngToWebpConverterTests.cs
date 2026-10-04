using System.Buffers.Binary;
using System.Globalization;
using System.Reflection;
using System.Text;
using NAP.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace NAP.Tests;

public sealed class PortraitPngToWebpConverterTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("nap-portrait-tests-").FullName;
    private readonly PortraitPngToWebpConverter _converter = new();

    [Fact]
    public void SettingsExposeOnlyFourGetOnlyPropertiesAndExactConstructor()
    {
        var type = typeof(PortraitConversionSettings);
        Assert.True(type.IsSealed);
        var properties = type.GetProperties();
        Assert.Equal(new[] { "MaxInputPixels", "OutputHeight", "OutputWidth", "WebpQuality" }, properties.Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.All(properties, p => Assert.Null(p.SetMethod));
        Assert.Equal(new[] { typeof(int), typeof(int), typeof(int), typeof(long) },
            Assert.Single(type.GetConstructors()).GetParameters().Select(p => p.ParameterType));
        Assert.Equal(new[] { "outputWidth", "outputHeight", "webpQuality", "maxInputPixels" },
            Assert.Single(type.GetConstructors()).GetParameters().Select(p => p.Name));
        var settings = new PortraitConversionSettings(16383, 1, 0, long.MaxValue);
        Assert.Equal(16383, settings.OutputWidth);
        Assert.Equal(1, settings.OutputHeight);
        Assert.Equal(0, settings.WebpQuality);
        Assert.Equal(long.MaxValue, settings.MaxInputPixels);
        Assert.Equal(100, new PortraitConversionSettings(1, 16383, 100, 1).WebpQuality);
    }

    [Theory]
    [InlineData(0, 5, 90, 20L, "outputWidth")]
    [InlineData(-1, 5, 90, 20L, "outputWidth")]
    [InlineData(16384, 5, 90, 20L, "outputWidth")]
    [InlineData(4, 0, 90, 20L, "outputHeight")]
    [InlineData(4, -1, 90, 20L, "outputHeight")]
    [InlineData(4, 16384, 90, 20L, "outputHeight")]
    [InlineData(4, 5, -1, 20L, "webpQuality")]
    [InlineData(4, 5, 101, 20L, "webpQuality")]
    [InlineData(4, 5, 90, 0L, "maxInputPixels")]
    [InlineData(4, 5, 90, -1L, "maxInputPixels")]
    public void InvalidSettingsNameTheirParameter(int width, int height, int quality, long pixels, string parameter)
    {
        Assert.Equal(parameter, Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PortraitConversionSettings(width, height, quality, pixels)).ParamName);
    }

    [Fact]
    public void WebpImageIsSealedWithOnlyInternalConstructorGettersAndToArray()
    {
        var type = typeof(PortraitWebpImage);
        Assert.True(type.IsSealed);
        Assert.Empty(type.GetConstructors());
        Assert.True(Assert.Single(type.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)).IsAssembly);
        Assert.All(type.GetProperties(), p => Assert.Null(p.SetMethod));
        var method = Assert.Single(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly), m => !m.IsSpecialName);
        Assert.Equal("ToArray", method.Name);
        Assert.Equal(typeof(byte[]), method.ReturnType);
        Assert.Empty(method.GetParameters());
        Assert.Equal(new[] { "Height", "SourceHeight", "SourceWidth", "WebpQuality", "Width" },
            type.GetProperties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void ConstructorAndEveryToArrayCallCopyBytes()
    {
        byte[] input = [1, 2, 3];
        var image = ConstructImage(input);
        input[0] = 42;
        var a = image.ToArray();
        Assert.Equal(new byte[] { 1, 2, 3 }, a);
        a[0] = 99;
        var b = image.ToArray();
        Assert.NotSame(a, b);
        Assert.Equal(new byte[] { 1, 2, 3 }, b);
        Assert.Equal(8, image.SourceWidth);
        Assert.Equal(10, image.SourceHeight);
        Assert.Equal(4, image.Width);
        Assert.Equal(5, image.Height);
        Assert.Equal(73, image.WebpQuality);
    }

    [Fact]
    public void EmptyOutputStillReturnsANewArrayOnEveryCall()
    {
        var image = ConstructImage([]);
        var first = image.ToArray();
        var second = image.ToArray();
        Assert.Empty(first);
        Assert.Empty(second);
        Assert.NotSame(first, second);
    }

    [Fact]
    public void ResultEnforcesCleanIfAndOnlyIfImageExistsIncludingNonBlockingIssues()
    {
        var clean = new NapIssueReport([]);
        var dirty = new NapIssueReport([new NapIssue("synthetic_warning", NapIssueSeverity.Warning, NapIssueDisposition.Continue, "warning")]);
        var image = ConstructImage([1]);
        Assert.True(typeof(PortraitConversionResult).IsSealed);
        Assert.Equal("issues", Assert.Throws<ArgumentNullException>(() => new PortraitConversionResult(null!, null)).ParamName);
        Assert.Equal("image", Assert.Throws<ArgumentException>(() => new PortraitConversionResult(clean, null)).ParamName);
        Assert.Equal("image", Assert.Throws<ArgumentException>(() => new PortraitConversionResult(dirty, image)).ParamName);
        var success = new PortraitConversionResult(clean, image);
        Assert.Same(clean, success.Issues);
        Assert.Same(image, success.Image);
        Assert.True(success.IsConverted);
        var failed = new PortraitConversionResult(dirty, null);
        Assert.Same(dirty, failed.Issues);
        Assert.Null(failed.Image);
        Assert.False(failed.IsConverted);
    }

    [Fact]
    public void ConverterIsSealedStatelessWithOnlyExactConvertApi()
    {
        var type = typeof(PortraitPngToWebpConverter);
        Assert.True(type.IsSealed);
        Assert.Empty(type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static));
        var method = Assert.Single(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly));
        Assert.Equal("Convert", method.Name);
        Assert.Equal(typeof(PortraitConversionResult), method.ReturnType);
        Assert.Equal(new[] { typeof(string), typeof(PortraitConversionSettings) }, method.GetParameters().Select(p => p.ParameterType));
        Assert.Equal(new[] { "sourcePath", "settings" }, method.GetParameters().Select(p => p.Name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    public void InvalidSourceArgumentUsesSourcePath(string? path)
    {
        Assert.Equal("sourcePath", Assert.ThrowsAny<ArgumentException>(() => _converter.Convert(path!, Settings())).ParamName);
    }

    [Fact]
    public void NullSettingsUsesSettingsParameter()
    {
        Assert.Equal("settings", Assert.Throws<ArgumentNullException>(() => _converter.Convert("source.png", null!)).ParamName);
    }

    [Fact]
    public void MissingFileAndParentDirectoryPropagate()
    {
        Assert.Throws<FileNotFoundException>(() => _converter.Convert(Path.Combine(_root, "missing.png"), Settings()));
        Assert.Throws<DirectoryNotFoundException>(() => _converter.Convert(Path.Combine(_root, "absent", "source.png"), Settings()));
    }

    [Fact]
    public void OperationalAccessFailuresAreNotDecodeIssues()
    {
        var path = WritePng(4, 5);
        using var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.ThrowsAny<IOException>(() => _converter.Convert(path, Settings()));
    }

    [Theory]
    [InlineData("invalid_signature", NapIssueCodes.PngInvalid)]
    [InlineData("invalid_crc", NapIssueCodes.PngInvalid)]
    [InlineData("unsupported", NapIssueCodes.PngUnsupportedFeature)]
    [InlineData("jpeg", NapIssueCodes.PngInvalid)]
    public void StructuralRejectionReusesMapperAndPrecedesDecode(string kind, string code)
    {
        var bytes = SyntheticPng(4, 5, unsupported: kind == "unsupported");
        if (kind == "invalid_signature") bytes[0] = 0;
        if (kind == "invalid_crc") bytes[29] ^= 1;
        if (kind == "jpeg")
        {
            using var jpeg = new Image<Rgba32>(4, 5);
            using var output = new MemoryStream();
            jpeg.SaveAsJpeg(output);
            bytes = output.ToArray();
        }
        var path = WriteBytes(bytes);
        var validation = new PngMasterValidator().Validate(path);
        Assert.False(validation.IsValid);
        var expected = NapIssueMapper.Map(validation, path);
        var result = _converter.Convert(path, Settings());
        Assert.False(result.IsConverted);
        Assert.Null(result.Image);
        Assert.Equal(expected, Assert.Single(result.Issues.Issues));
        Assert.Equal(code, result.Issues.Issues[0].Code);
    }

    [Theory]
    [InlineData(4, 5, 19L)]
    [InlineData(100000, 125000, 2000000000L)]
    [InlineData(int.MaxValue, int.MaxValue, 100L)]
    public void PixelLimitRejectsBeforeDecodeUsingLongProducts(int width, int height, long limit)
    {
        var path = WriteBytes(SyntheticPng(width, height));
        Assert.True(new PngMasterValidator().Validate(path).IsValid);
        var result = _converter.Convert(path, new PortraitConversionSettings(4, 5, 90, limit));
        AssertStop(result, NapIssueCodes.PortraitInputTooLarge,
            "The portrait PNG exceeds the configured pixel safety limit.", path,
            FormattableString.Invariant($"{width}x{height}; max_pixels={limit}"));
    }

    [Fact]
    public void PixelBoundaryIsAcceptedAndNextPixelIsRejected()
    {
        var path = WritePng(4, 5);
        Assert.True(_converter.Convert(path, new PortraitConversionSettings(4, 5, 90, 20)).IsConverted);
        AssertStop(_converter.Convert(path, new PortraitConversionSettings(4, 5, 90, 19)),
            NapIssueCodes.PortraitInputTooLarge, "The portrait PNG exceeds the configured pixel safety limit.", path, "4x5; max_pixels=19");
    }

    [Theory]
    [InlineData(4, 6, 4, 5)]
    [InlineData(16382, 16383, 16381, 16382)]
    [InlineData(int.MaxValue, int.MaxValue - 1, 4, 5)]
    public void ExactIntegerRatioRejectsBeforeDecodeWithoutTolerance(int width, int height, int outputWidth, int outputHeight)
    {
        var path = WriteBytes(SyntheticPng(width, height));
        var result = _converter.Convert(path, new PortraitConversionSettings(outputWidth, outputHeight, 90, long.MaxValue));
        AssertStop(result, NapIssueCodes.PortraitAspectRatioMismatch,
            "The portrait PNG aspect ratio does not match the configured output ratio.", path,
            FormattableString.Invariant($"source={width}x{height}; output={outputWidth}x{outputHeight}"));
    }

    [Fact]
    public void StructurallyValidUndecodableIdatProducesOnlyDecodeFailed()
    {
        var path = WriteBytes(SyntheticPng(4, 5));
        Assert.True(new PngMasterValidator().Validate(path).IsValid);
        AssertStop(_converter.Convert(path, Settings()), NapIssueCodes.PortraitDecodeFailed,
            "The portrait PNG could not be decoded.", path, null);
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("ar-SA")]
    public void IssueDetailsUseInvariantNumbers(string cultureName)
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            var path = WriteBytes(SyntheticPng(100000, 125000));
            AssertStop(_converter.Convert(path, new PortraitConversionSettings(4, 5, 90, 2000000000)),
                NapIssueCodes.PortraitInputTooLarge, "The portrait PNG exceeds the configured pixel safety limit.",
                path, "100000x125000; max_pixels=2000000000");
            path = WriteBytes(SyntheticPng(4, 6));
            AssertStop(_converter.Convert(path, new PortraitConversionSettings(4, 5, 90, 24)), NapIssueCodes.PortraitAspectRatioMismatch,
                "The portrait PNG aspect ratio does not match the configured output ratio.", path, "source=4x6; output=4x5");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    [Theory]
    [InlineData(1024, 1280)]
    [InlineData(1536, 1920)]
    [InlineData(384, 480)]
    [InlineData(768, 960)]
    public void RepresentativeNimroelSizesEncodeRealLossyWebpInMemoryAndKeepSourceIntact(int width, int height)
    {
        var path = WritePng(width, height);
        var original = File.ReadAllBytes(path);
        var before = Directory.GetFileSystemEntries(_root);
        var settings = new PortraitConversionSettings(768, 960, 90, 4000000);
        var result = _converter.Convert(path, settings);
        Assert.True(result.IsConverted);
        Assert.True(result.Issues.IsClean);
        Assert.Empty(result.Issues.Issues);
        var output = Assert.IsType<PortraitWebpImage>(result.Image);
        Assert.Equal(width, output.SourceWidth);
        Assert.Equal(height, output.SourceHeight);
        Assert.Equal(768, output.Width);
        Assert.Equal(960, output.Height);
        Assert.Equal(90, output.WebpQuality);
        var bytes = output.ToArray();
        Assert.Equal(WebpFormat.Instance, Image.DetectFormat(bytes));
        using var decoded = Image.Load<Rgba32>(bytes);
        Assert.Equal(768, decoded.Width);
        Assert.Equal(960, decoded.Height);
        Assert.Equal(WebpFileFormatType.Lossy, decoded.Metadata.GetWebpMetadata().FileFormat);
        // Four colored quadrants and edges survive resize: no crop, pad or letterbox.
        AssertRed(decoded[10, 10]);
        AssertGreen(decoded[757, 10]);
        AssertBlue(decoded[10, 949]);
        AssertYellow(decoded[757, 949]);
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal(before, Directory.GetFileSystemEntries(_root));
        Assert.Empty(Directory.GetFiles(_root, "*.webp", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetDirectories(_root));
        var repeated = _converter.Convert(path, settings);
        using var repeatedDecoded = Image.Load<Rgba32>(repeated.Image!.ToArray());
        Assert.Equal(decoded.Size, repeatedDecoded.Size);
        Assert.Equal(decoded[10, 10], repeatedDecoded[10, 10]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void GenericExplicitSettingsWorkWithoutUniverseOrPlan(int quality)
    {
        var path = WritePng(12, 7);
        var result = _converter.Convert(path, new PortraitConversionSettings(24, 14, quality, 84));
        Assert.True(result.IsConverted);
        Assert.Equal(quality, result.Image!.WebpQuality);
        using var image = Image.Load<Rgba32>(result.Image.ToArray());
        Assert.Equal(new Size(24, 14), image.Size);
    }

    [Fact]
    public void ExifOrientationDoesNotRotateOrCropPixels()
    {
        var path = WritePng(4, 5, orientation: true);
        var result = _converter.Convert(path, Settings());
        Assert.True(result.IsConverted);
        using var decoded = Image.Load<Rgba32>(result.Image!.ToArray());
        Assert.Equal(new Size(4, 5), decoded.Size);
        AssertRed(decoded[0, 0]);
        AssertBlue(decoded[0, 4]);
    }

    [Fact]
    public void ThreeNewCodesAreExactAndUnique()
    {
        Assert.Equal("portrait_input_too_large", NapIssueCodes.PortraitInputTooLarge);
        Assert.Equal("portrait_aspect_ratio_mismatch", NapIssueCodes.PortraitAspectRatioMismatch);
        Assert.Equal("portrait_decode_failed", NapIssueCodes.PortraitDecodeFailed);
        var codes = typeof(NapIssueCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (string)f.GetRawConstantValue()!).ToArray();
        Assert.Equal(codes.Length, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(codes, code => Assert.True(AssetNamingRules.IsValidMachineIdentifier(code)));
    }

    private static PortraitConversionSettings Settings() => new(4, 5, 90, 20);

    private static PortraitWebpImage ConstructImage(byte[] bytes) =>
        Assert.IsType<PortraitWebpImage>(Assert.Single(typeof(PortraitWebpImage).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance))
            .Invoke([8, 10, 4, 5, 73, bytes]));

    private static void AssertStop(PortraitConversionResult result, string code, string message, string path, string? detail)
    {
        Assert.False(result.IsConverted);
        Assert.Null(result.Image);
        Assert.False(result.Issues.IsClean);
        Assert.True(result.Issues.ShouldStop);
        Assert.False(result.Issues.CanContinue);
        var issue = Assert.Single(result.Issues.Issues);
        Assert.Equal(code, issue.Code);
        Assert.Equal(NapIssueSeverity.Error, issue.Severity);
        Assert.Equal(NapIssueDisposition.Stop, issue.Disposition);
        Assert.Equal(message, issue.Message);
        Assert.Equal(path, issue.SubjectPath);
        Assert.Equal(detail, issue.Detail);
    }

    private string WritePng(int width, int height, bool orientation = false)
    {
        using var image = new Image<Rgba32>(width, height);
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                image[x, y] = y < height / 2
                    ? x < width / 2 ? new Rgba32(255, 0, 0) : new Rgba32(0, 255, 0)
                    : x < width / 2 ? new Rgba32(0, 0, 255) : new Rgba32(255, 255, 0);
        if (orientation)
        {
            image.Metadata.ExifProfile = new ExifProfile();
            image.Metadata.ExifProfile.SetValue(ExifTag.Orientation, (ushort)6);
        }
        using var stream = new MemoryStream();
        image.Save(stream, new PngEncoder());
        return WriteBytes(stream.ToArray());
    }

    private string WriteBytes(byte[] bytes)
    {
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    // Deliberately bad zlib payload; valid PNG container/CRCs with no raster allocation.
    private static byte[] SyntheticPng(int width, int height, bool unsupported = false)
    {
        using var stream = new MemoryStream();
        stream.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8;
        ihdr[9] = 6;
        Chunk(stream, "IHDR", ihdr);
        if (unsupported) Chunk(stream, "acTL", new byte[8]);
        Chunk(stream, "IDAT", [1, 2, 3]);
        Chunk(stream, "IEND", []);
        return stream.ToArray();
    }

    private static void Chunk(Stream stream, string type, byte[] data)
    {
        var number = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(number, (uint)data.Length);
        stream.Write(number);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);
        uint crc = uint.MaxValue;
        foreach (var value in typeBytes.Concat(data))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xedb88320;
        }
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc);
        stream.Write(number);
    }

    private static void AssertRed(Rgba32 pixel) => Assert.True(pixel.R > 180 && pixel.G < 70 && pixel.B < 70);
    private static void AssertGreen(Rgba32 pixel) => Assert.True(pixel.G > 180 && pixel.R < 70 && pixel.B < 70);
    private static void AssertBlue(Rgba32 pixel) => Assert.True(pixel.B > 180 && pixel.R < 70 && pixel.G < 70);
    private static void AssertYellow(Rgba32 pixel) => Assert.True(pixel.R > 180 && pixel.G > 180 && pixel.B < 70);

    public void Dispose() => Directory.Delete(_root, recursive: true);
}

using System.Buffers.Binary;
using System.Globalization;
using System.Reflection;
using System.Text;
using NAP.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace NAP.Tests;

public sealed class PortraitWebpOutputValidatorTests
{
    private readonly PortraitWebpOutputValidator _validator = new();

    [Fact]
    public void ValidatorIsSealedStatelessWithOnlyExactPublicApiAndDefaultConstructor()
    {
        var type = typeof(PortraitWebpOutputValidator);
        Assert.True(type.IsSealed);
        Assert.Empty(type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));
        var method = Assert.Single(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly));
        Assert.Equal("Validate", method.Name);
        Assert.Equal(typeof(NapIssueReport), method.ReturnType);
        Assert.Equal(new[] { typeof(PortraitWebpImage), typeof(PortraitConversionSettings) }, method.GetParameters().Select(p => p.ParameterType));
        Assert.Equal(new[] { "image", "settings" }, method.GetParameters().Select(p => p.Name));
        Assert.Empty(Assert.Single(type.GetConstructors()).GetParameters());
    }

    [Fact]
    public void NullArgumentsUseExactParameterNames()
    {
        Assert.Equal("image", Assert.Throws<ArgumentNullException>(() => _validator.Validate(null!, Settings())).ParamName);
        Assert.Equal("settings", Assert.Throws<ArgumentNullException>(() => _validator.Validate(Output([]), null!)).ParamName);
    }

    [Theory]
    [InlineData(3, 5, 90)]
    [InlineData(4, 6, 90)]
    [InlineData(4, 5, 89)]
    [InlineData(3, 6, 89)]
    public void MetadataMismatchPrecedesDecodeOfInvalidBytes(int width, int height, int quality)
    {
        var image = Output([], width, height, quality);
        AssertStop(_validator.Validate(image, Settings()), NapIssueCodes.PortraitOutputMetadataMismatch,
            "The portrait WebP metadata does not match the configured output.",
            FormattableString.Invariant($"image={width}x{height} q={quality}; expected=4x5 q=90"));
    }

    [Theory]
    [InlineData(WebpFileFormatType.Lossy)]
    [InlineData(WebpFileFormatType.Lossless)]
    public void RealWebpPassesAndQualityIsOnlyContractualMetadata(WebpFileFormatType format)
    {
        // Deliberately encoded at Q25: validator must not infer encoder quality from bytes.
        var image = Output(Webp(4, 5, format, 25));
        AssertClean(_validator.Validate(image, Settings()));
    }

    [Fact]
    public void SourceDimensionsAreHistoricalAndNotValidated()
    {
        var image = Output(Webp(4, 5), sourceWidth: -7, sourceHeight: 0);
        AssertClean(_validator.Validate(image, Settings()));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("random")]
    [InlineData("png")]
    [InlineData("jpeg")]
    [InlineData("riff_only")]
    [InlineData("truncated")]
    [InlineData("truncated_adjusted_riff")]
    [InlineData("corrupt_header")]
    [InlineData("bad_webp_tag")]
    [InlineData("overflow_chunk")]
    [InlineData("corrupt_pixels")]
    public void NonWebpTruncatedAndCorruptedBytesProduceSingleControlledIssue(string kind)
    {
        byte[] bytes;
        switch (kind)
        {
            case "empty": bytes = []; break;
            case "random": bytes = Enumerable.Range(0, 64).Select(n => (byte)n).ToArray(); break;
            case "png":
            case "jpeg":
                using (var image = new Image<Rgba32>(4, 5))
                using (var output = new MemoryStream())
                {
                    if (kind == "png") image.Save(output, new PngEncoder());
                    else image.SaveAsJpeg(output);
                    bytes = output.ToArray();
                    if (kind == "png") Assert.True(new PngMasterValidator().Validate(new MemoryStream(bytes)).IsValid);
                }
                break;
            case "riff_only": bytes = Encoding.ASCII.GetBytes("RIFF\u0004\0\0\0WEBP"); break;
            case "truncated":
                var full = Webp(4, 5);
                bytes = full[..(full.Length / 2)];
                break;
            case "truncated_adjusted_riff":
                bytes = Webp(4, 5)[..^2];
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), (uint)(bytes.Length - 8));
                break;
            case "corrupt_header":
                bytes = Webp(4, 5);
                bytes[0] = 0;
                break;
            case "bad_webp_tag":
                bytes = Webp(4, 5);
                bytes[8] = 0;
                break;
            case "overflow_chunk":
                bytes = Webp(4, 5);
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16, 4), uint.MaxValue);
                break;
            default:
                bytes = Webp(4, 5);
                // Retain RIFF/WEBP and chunk layout but break the VP8 keyframe signature.
                var chunk = FindChunk(bytes, "VP8 ");
                Assert.True(chunk >= 0);
                bytes[chunk + 8 + 3] = 0;
                bytes[chunk + 8 + 4] = 0;
                bytes[chunk + 8 + 5] = 0;
                break;
        }
        AssertStop(_validator.Validate(Output(bytes), Settings()), NapIssueCodes.PortraitOutputInvalidWebp,
            "The portrait output is not a valid decodable WebP image.", null);
    }

    [Fact]
    public void DecodeRevealsDimensionsDespiteMatchingObjectMetadataAndSettings()
    {
        var image = Output(Webp(640, 800), 768, 960);
        var settings = new PortraitConversionSettings(768, 960, 90, 4000000);
        AssertStop(_validator.Validate(image, settings), NapIssueCodes.PortraitOutputDimensionsMismatch,
            "The decoded portrait WebP dimensions do not match the configured output.",
            "decoded=640x800; expected=768x960");
    }

    [Theory]
    [InlineData(3, 5)]
    [InlineData(4, 6)]
    public void DecodedWidthAndHeightAreCheckedIndependently(int width, int height)
    {
        AssertStop(_validator.Validate(Output(Webp(width, height)), Settings()), NapIssueCodes.PortraitOutputDimensionsMismatch,
            "The decoded portrait WebP dimensions do not match the configured output.",
            FormattableString.Invariant($"decoded={width}x{height}; expected=4x5"));
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("ar-SA")]
    [InlineData("sv-SE")]
    public void DetailFormattingAndRepeatedValidationAreCultureIndependent(string cultureName)
    {
        var metadata = Output([], 640, 800, 25);
        var dimensions = Output(Webp(640, 800), 768, 960);
        var settings = new PortraitConversionSettings(768, 960, 90, 4000000);
        var expectedMetadata = _validator.Validate(metadata, settings);
        var expectedDimensions = _validator.Validate(dimensions, settings);
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            Assert.Equal(expectedMetadata.Issues, _validator.Validate(metadata, settings).Issues);
            Assert.Equal(expectedDimensions.Issues, _validator.Validate(dimensions, settings).Issues);
            Assert.Equal("image=640x800 q=25; expected=768x960 q=90", Assert.Single(expectedMetadata.Issues).Detail);
            Assert.Equal("decoded=640x800; expected=768x960", Assert.Single(expectedDimensions.Issues).Detail);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    [Theory]
    [InlineData("clean")]
    [InlineData("metadata")]
    [InlineData("invalid")]
    [InlineData("dimensions")]
    public void ValidationDoesNotAlterBytesMetadataOrSettingsAndRepeatsLogically(string kind)
    {
        var image = kind switch
        {
            "metadata" => Output([], 3),
            "invalid" => Output([1, 2, 3]),
            "dimensions" => Output(Webp(3, 5)),
            _ => Output(Webp(4, 5))
        };
        var settings = Settings();
        var before = image.ToArray();
        var imageMetadata = (image.SourceWidth, image.SourceHeight, image.Width, image.Height, image.WebpQuality);
        var settingsBefore = (settings.OutputWidth, settings.OutputHeight, settings.WebpQuality, settings.MaxInputPixels);
        var first = _validator.Validate(image, settings);
        var second = _validator.Validate(image, settings);
        Assert.Equal(before, image.ToArray());
        Assert.Equal(imageMetadata, (image.SourceWidth, image.SourceHeight, image.Width, image.Height, image.WebpQuality));
        Assert.Equal(settingsBefore, (settings.OutputWidth, settings.OutputHeight, settings.WebpQuality, settings.MaxInputPixels));
        Assert.Equal(first.Issues, second.Issues);
        Assert.Equal(first.IsClean, second.IsClean);
        Assert.Equal(first.ShouldStop, second.ShouldStop);
        Assert.Equal(first.CanContinue, second.CanContinue);
    }

    [Fact]
    public void RealConverterToValidatorNimroelExampleIsCleanAndCreatesNoOutputFiles()
    {
        var root = Directory.CreateTempSubdirectory("nap-output-validation-tests-").FullName;
        try
        {
            var path = Path.Combine(root, "master.png");
            using (var png = new Image<Rgba32>(1024, 1280, new Rgba32(180, 70, 30)))
                png.Save(path, new PngEncoder());
            var sourceBefore = File.ReadAllBytes(path);
            var settings = new PortraitConversionSettings(768, 960, 90, 4000000);
            var converted = new PortraitPngToWebpConverter().Convert(path, settings);
            Assert.True(converted.IsConverted);
            var image = Assert.IsType<PortraitWebpImage>(converted.Image);
            var bytesBefore = image.ToArray();
            var entriesBefore = Directory.GetFileSystemEntries(root);
            AssertClean(_validator.Validate(image, settings));
            Assert.Equal(bytesBefore, image.ToArray());
            Assert.Equal(sourceBefore, File.ReadAllBytes(path));
            Assert.Equal(entriesBefore, Directory.GetFileSystemEntries(root));
            Assert.Empty(Directory.GetDirectories(root));
            Assert.Empty(Directory.GetFiles(root, "*.webp"));
            Assert.Equal(768, image.Width);
            Assert.Equal(960, image.Height);
            Assert.Equal(90, image.WebpQuality);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ThreeCodesAreExactUniqueMachineIdentifiers()
    {
        Assert.Equal("portrait_output_metadata_mismatch", NapIssueCodes.PortraitOutputMetadataMismatch);
        Assert.Equal("portrait_output_invalid_webp", NapIssueCodes.PortraitOutputInvalidWebp);
        Assert.Equal("portrait_output_dimensions_mismatch", NapIssueCodes.PortraitOutputDimensionsMismatch);
        var codes = typeof(NapIssueCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (string)f.GetRawConstantValue()!).ToArray();
        Assert.Equal(codes.Length, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(codes, code => Assert.True(AssetNamingRules.IsValidMachineIdentifier(code)));
    }

    private static PortraitConversionSettings Settings() => new(4, 5, 90, 20);

    private static PortraitWebpImage Output(byte[] bytes, int width = 4, int height = 5, int quality = 90,
        int sourceWidth = 8, int sourceHeight = 10) =>
        Assert.IsType<PortraitWebpImage>(Assert.Single(typeof(PortraitWebpImage).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic))
            .Invoke([sourceWidth, sourceHeight, width, height, quality, bytes]));

    private static byte[] Webp(int width, int height, WebpFileFormatType format = WebpFileFormatType.Lossy, int quality = 90)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(130, 70, 30));
        using var stream = new MemoryStream();
        image.Save(stream, new WebpEncoder { FileFormat = format, Quality = quality });
        return stream.ToArray();
    }

    private static int FindChunk(byte[] bytes, string type)
    {
        for (var offset = 12; offset <= bytes.Length - 8;)
        {
            if (Encoding.ASCII.GetString(bytes, offset, 4) == type) return offset;
            var length = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4, 4));
            offset += checked(8 + (int)length + (int)(length & 1));
        }
        return -1;
    }

    private static void AssertClean(NapIssueReport report)
    {
        Assert.Empty(report.Issues);
        Assert.True(report.IsClean);
        Assert.False(report.ShouldStop);
        Assert.True(report.CanContinue);
    }

    private static void AssertStop(NapIssueReport report, string code, string message, string? detail)
    {
        Assert.False(report.IsClean);
        Assert.True(report.ShouldStop);
        Assert.False(report.CanContinue);
        var issue = Assert.Single(report.Issues);
        Assert.Equal(code, issue.Code);
        Assert.Equal(message, issue.Message);
        Assert.Equal(NapIssueSeverity.Error, issue.Severity);
        Assert.Equal(NapIssueDisposition.Stop, issue.Disposition);
        Assert.Null(issue.SubjectPath);
        Assert.Equal(detail, issue.Detail);
    }
}

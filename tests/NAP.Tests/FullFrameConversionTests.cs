using NAP.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace NAP.Tests;

public sealed class FullFrameConversionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CompatibleDownscaleAndUpscaleKeepAllFourBordersAndDistinctCorners(bool scene, bool upscale)
    {
        var root = Directory.CreateTempSubdirectory("nap-full-frame-").FullName;
        try
        {
            var outputWidth = scene ? 120 : 80;
            var outputHeight = scene ? 80 : 100;
            var width = upscale ? outputWidth / 2 : outputWidth * 2;
            var height = upscale ? outputHeight / 2 : outputHeight * 2;
            var path = Path.Combine(root, "source.png");
            using (var source = Pattern(width, height)) source.SaveAsPng(path);
            var original = File.ReadAllBytes(path);
            byte[] bytes;
            if (scene)
            {
                var result = new ScenePngToWebpConverter().Convert(path, new SceneConversionSettings(outputWidth, outputHeight, 90, width * height));
                Assert.True(result.IsConverted);
                Assert.True(result.Issues.IsClean);
                bytes = result.Image!.ToArray();
            }
            else
            {
                var result = new PortraitPngToWebpConverter().Convert(path, new PortraitConversionSettings(outputWidth, outputHeight, 90, width * height));
                Assert.True(result.IsConverted);
                Assert.True(result.Issues.IsClean);
                bytes = result.Image!.ToArray();
            }
            using var decoded = Image.Load<Rgba32>(bytes);
            Assert.Equal(new Size(outputWidth, outputHeight), decoded.Size);
            var left = outputWidth / 16;
            var right = outputWidth - 1 - left;
            var top = outputHeight / 16;
            var bottom = outputHeight - 1 - top;
            AssertColor(new Rgba32(255, 0, 0), decoded[outputWidth / 2, top]);
            AssertColor(new Rgba32(0, 255, 0), decoded[outputWidth / 2, bottom]);
            AssertColor(new Rgba32(0, 0, 255), decoded[left, outputHeight / 2]);
            AssertColor(new Rgba32(255, 255, 0), decoded[right, outputHeight / 2]);
            AssertColor(new Rgba32(255, 0, 255), decoded[left, top]);
            AssertColor(new Rgba32(0, 255, 255), decoded[right, top]);
            AssertColor(new Rgba32(255, 255, 255), decoded[left, bottom]);
            AssertColor(new Rgba32(128, 0, 128), decoded[right, bottom]);
            AssertColor(new Rgba32(32, 32, 32), decoded[outputWidth / 2, outputHeight / 2]);
            Assert.Equal(original, File.ReadAllBytes(path));
            Assert.Equal(new[] { path }, Directory.GetFileSystemEntries(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static Image<Rgba32> Pattern(int width, int height)
    {
        var image = new Image<Rgba32>(width, height);
        var borderWidth = width / 8;
        var borderHeight = height / 8;
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var left = x < borderWidth;
                var right = x >= width - borderWidth;
                var top = y < borderHeight;
                var bottom = y >= height - borderHeight;
                image[x, y] = (left, right, top, bottom) switch
                {
                    (true, _, true, _) => new Rgba32(255, 0, 255),
                    (_, true, true, _) => new Rgba32(0, 255, 255),
                    (true, _, _, true) => new Rgba32(255, 255, 255),
                    (_, true, _, true) => new Rgba32(128, 0, 128),
                    (_, _, true, _) => new Rgba32(255, 0, 0),
                    (_, _, _, true) => new Rgba32(0, 255, 0),
                    (true, _, _, _) => new Rgba32(0, 0, 255),
                    (_, true, _, _) => new Rgba32(255, 255, 0),
                    _ => new Rgba32(32, 32, 32)
                };
            }
        return image;
    }

    // Compare broad color bands, not byte/pixel identity from a lossy encoder.
    private static void AssertColor(Rgba32 expected, Rgba32 actual)
    {
        Assert.InRange(Math.Abs(expected.R - actual.R), 0, 60);
        Assert.InRange(Math.Abs(expected.G - actual.G), 0, 60);
        Assert.InRange(Math.Abs(expected.B - actual.B), 0, 60);
    }
}

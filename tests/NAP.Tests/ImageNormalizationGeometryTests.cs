using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class ImageNormalizationGeometryTests
{
    [Theory]
    [InlineData(1586, 992, 1600, 1000, 1592, 995, 3, 1, 3, 2)]
    [InlineData(1600, 1000, 1600, 1000, 1600, 1000, 0, 0, 0, 0)]
    [InlineData(1920, 1200, 1600, 1000, 1920, 1200, 0, 0, 0, 0)]
    [InlineData(1598, 1000, 1600, 1000, 1600, 1000, 1, 0, 1, 0)]
    [InlineData(1599, 1000, 1600, 1000, 1600, 1000, 0, 0, 1, 0)]
    [InlineData(1600, 999, 1600, 1000, 1600, 1000, 0, 0, 0, 1)]
    [InlineData(799, 1000, 768, 960, 800, 1000, 0, 0, 1, 0)]
    [InlineData(1499, 1000, 300, 200, 1500, 1000, 0, 0, 1, 0)]
    [InlineData(1000, 999, 500, 500, 1000, 1000, 0, 0, 0, 1)]
    [InlineData(100, 103, 101, 103, 101, 103, 0, 0, 1, 0)]
    public void ComputesMinimalExactCanvasAndDeterministicMargins(int w, int h, int targetW, int targetH, int cw, int ch, int l, int t, int r, int b)
    {
        var rule = Rule(targetW, targetH); var source = new PngImageInfo(w, h, 8, 6, 0);
        var geometry = ImageNormalizationGeometry.Calculate(source, rule);
        Assert.Equal(new(w, h, cw, ch, l, t, r, b), geometry);
        Assert.Equal((long)cw * targetH, (long)ch * targetW);
        Assert.True(cw >= w && ch >= h);
        var divisor = Gcd(targetW, targetH);
        Assert.True(cw - targetW / divisor < w || ch - targetH / divisor < h);
    }

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(1586, 300)]
    [InlineData(20, 13)]
    public void LargeOrGranularAdaptationsRequireArtisticIntervention(int w, int h) =>
        Assert.Equal("normalization_growth_limit", Assert.Throws<ImageNormalizationException>(() => ImageNormalizationGeometry.Calculate(new(w, h, 8, 2, 0), Rule(1600, 1000))).Code);

    [Theory]
    [InlineData(68, 100, false)]
    [InlineData(69, 100, true)]
    [InlineData(100, 37, false)]
    [InlineData(100, 38, true)]
    public void LimitsUseExactIntegerComparison(int area, int axis, bool accepted)
    {
        var policy = new ImageNormalizationPolicy { MaxAddedAreaBasisPoints = area, MaxAxisGrowthBasisPoints = axis };
        if (accepted) Assert.Equal(1592, ImageNormalizationGeometry.Calculate(new(1586, 992, 8, 2, 0), Rule(1600, 1000), policy).CanvasWidth);
        else Assert.Equal("normalization_growth_limit", Assert.Throws<ImageNormalizationException>(() => ImageNormalizationGeometry.Calculate(new(1586, 992, 8, 2, 0), Rule(1600, 1000), policy)).Code);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(501)]
    [InlineData(int.MaxValue)]
    public void PolicyCannotAuthorizeLargeGrowth(int growth) => Assert.Throws<ArgumentOutOfRangeException>(() =>
        ImageNormalizationGeometry.Calculate(new(1586, 992, 8, 2, 0), Rule(1600, 1000), new() { MaxAddedAreaBasisPoints = growth }));

    [Theory]
    [InlineData(int.MaxValue, int.MaxValue)]
    [InlineData(int.MaxValue, 1)]
    [InlineData(1, int.MaxValue)]
    public void ExtremeInputsStopBeforeArithmeticOrAllocation(int w, int h) => Assert.Equal("normalization_pixel_limit",
        Assert.Throws<ImageNormalizationException>(() => ImageNormalizationGeometry.Calculate(new(w, h, 16, 6, 0), Rule(16383, 16382))).Code);

    [Fact]
    public void CoprimeTargetCannotAllocateHugeMinimalCanvas() => Assert.Equal("normalization_canvas_limit",
        Assert.Throws<ImageNormalizationException>(() => ImageNormalizationGeometry.Calculate(new(1000, 1000, 8, 2, 0), Rule(16383, 16382))).Code);
    [Fact]
    public void CanvasPixelBudgetIncludesAddedPixels() => Assert.Equal("normalization_canvas_limit",
        Assert.Throws<ImageNormalizationException>(() => ImageNormalizationGeometry.Calculate(new(1598, 1000, 8, 2, 0), Rule(1600, 1000), new() { MaxCanvasPixels = 1_599_999 })).Code);
    [Theory][InlineData(0, 1)][InlineData(1, 0)][InlineData(-1, 1)]
    public void NonPositiveInputsAreRejected(int w, int h) => Assert.Throws<ArgumentOutOfRangeException>(() => ImageNormalizationGeometry.Calculate(new(w, h, 8, 2, 0), Rule(4, 5)));
    internal static ImageConversionRule Rule(int w = 1600, int h = 1000) => new(ImageConversionKind.PngToWebp, "master", w, h, 90);
    private static int Gcd(int a, int b) { while (b != 0) (a, b) = (b, a % b); return a; }
}

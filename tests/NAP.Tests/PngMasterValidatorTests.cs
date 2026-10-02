using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class PngMasterValidatorTests
{
    [Theory]
    [InlineData(4, 5, true)]
    [InlineData(1024, 1280, true)]
    [InlineData(1536, 1920, true)]
    [InlineData(768, 960, true)]
    [InlineData(1024, 1536, false)]
    public void RealPng_ReportsDimensionsAndExactRatio(int width, int height, bool expectedRatio)
    {
        using var stream = new MemoryStream(Encode(CreateImage(width, height)));

        var result = new PngMasterValidator().Validate(stream);

        Assert.True(result.IsValid);
        Assert.Equal(PngValidationStatus.Valid, result.Status);
        var info = Assert.IsType<PngImageInfo>(result.ImageInfo);
        Assert.Equal(width, info.Width);
        Assert.Equal(height, info.Height);
        Assert.Equal(8, info.BitDepth);
        Assert.Equal(6, info.ColorType);
        Assert.Equal(0, info.InterlaceMethod);
        Assert.Equal(expectedRatio, info.HasAspectRatio(4, 5));
        Assert.Null(result.Reason);
        Assert.True(stream.CanRead); // Caller retains ownership.
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0, 2)]
    [InlineData(0, 4)]
    [InlineData(0, 8)]
    [InlineData(0, 16)]
    [InlineData(2, 8)]
    [InlineData(2, 16)]
    [InlineData(3, 1)]
    [InlineData(3, 2)]
    [InlineData(3, 4)]
    [InlineData(3, 8)]
    [InlineData(4, 8)]
    [InlineData(4, 16)]
    [InlineData(6, 8)]
    [InlineData(6, 16)]
    public void LegalColorDepthCombinations_AreAccepted(byte colorType, byte bitDepth)
    {
        var result = Validate(Encode(CreateImage(colorType: colorType, bitDepth: bitDepth)));

        Assert.True(result.IsValid);
        Assert.Equal(colorType, result.ImageInfo!.ColorType);
        Assert.Equal(bitDepth, result.ImageInfo.BitDepth);
    }

    [Fact]
    public void RealAdam7Image_IsAcceptedAndReportsInterlace()
    {
        var result = Validate(Encode(CreateImage(interlace: 1)));

        Assert.True(result.IsValid);
        Assert.Equal(1, result.ImageInfo!.InterlaceMethod);
    }

    [Theory]
    [InlineData("missing_ihdr")]
    [InlineData("ihdr_not_first")]
    [InlineData("duplicate_ihdr")]
    [InlineData("short_ihdr")]
    [InlineData("long_ihdr")]
    [InlineData("missing_idat")]
    [InlineData("empty_image_data")]
    [InlineData("missing_iend")]
    [InlineData("nonempty_iend")]
    [InlineData("duplicate_iend")]
    [InlineData("nonconsecutive_idat")]
    [InlineData("illegal_chunk_name")]
    [InlineData("reserved_chunk_bit")]
    public void InvalidChunkStructure_IsRejected(string error)
    {
        var chunks = CreateImage();
        switch (error)
        {
            case "missing_ihdr": chunks.RemoveAt(0); break;
            case "ihdr_not_first": chunks.Insert(0, TextChunk()); break;
            case "duplicate_ihdr": chunks.Insert(1, chunks[0]); break;
            case "short_ihdr": chunks[0] = chunks[0] with { Data = chunks[0].Data[..12] }; break;
            case "long_ihdr": chunks[0] = chunks[0] with { Data = [.. chunks[0].Data, 0] }; break;
            case "missing_idat": chunks.RemoveAll(chunk => chunk.Type == "IDAT"); break;
            case "empty_image_data": chunks[1] = new("IDAT", []); break;
            case "missing_iend": chunks.RemoveAt(chunks.Count - 1); break;
            case "nonempty_iend": chunks[^1] = new("IEND", [1]); break;
            case "duplicate_iend": chunks.Add(new("IEND", [])); break;
            case "nonconsecutive_idat": chunks.Insert(2, TextChunk()); chunks.Insert(3, new("IDAT", [])); break;
            case "illegal_chunk_name": chunks.Insert(1, new("te1t", [])); break;
            case "reserved_chunk_bit": chunks.Insert(1, new("text", [])); break;
            default: throw new InvalidOperationException(error);
        }

        AssertInvalid(Validate(Encode(chunks)));
    }

    [Theory]
    [InlineData("zero_width")]
    [InlineData("zero_height")]
    [InlineData("oversized_width")]
    [InlineData("oversized_height")]
    [InlineData("invalid_color")]
    [InlineData("invalid_rgba_depth")]
    [InlineData("invalid_gray_depth")]
    [InlineData("invalid_indexed_depth")]
    [InlineData("compression")]
    [InlineData("filter")]
    [InlineData("interlace")]
    public void InvalidIhdr_WithCorrectCrc_IsRejected(string error)
    {
        var chunks = CreateImage();
        var ihdr = chunks[0].Data;
        switch (error)
        {
            case "zero_width": BinaryPrimitives.WriteUInt32BigEndian(ihdr, 0); break;
            case "zero_height": BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), 0); break;
            case "oversized_width": BinaryPrimitives.WriteUInt32BigEndian(ihdr, 0x80000000); break;
            case "oversized_height": BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), uint.MaxValue); break;
            case "invalid_color": ihdr[9] = 1; break;
            case "invalid_rgba_depth": ihdr[8] = 4; break;
            case "invalid_gray_depth": ihdr[8] = 3; ihdr[9] = 0; break;
            case "invalid_indexed_depth": ihdr[8] = 16; ihdr[9] = 3; break;
            case "compression": ihdr[10] = 1; break;
            case "filter": ihdr[11] = 1; break;
            case "interlace": ihdr[12] = 2; break;
            default: throw new InvalidOperationException(error);
        }

        AssertInvalid(Validate(Encode(chunks)));
    }

    [Theory]
    [InlineData("IHDR")]
    [InlineData("IDAT")]
    [InlineData("IEND")]
    [InlineData("tEXt")]
    public void CrcOfEveryChunkType_IsChecked(string chunkType)
    {
        var chunks = CreateImage();
        chunks.Insert(1, TextChunk());
        var bytes = Encode(chunks);
        var offset = 8;
        foreach (var chunk in chunks)
        {
            if (chunk.Type == chunkType)
            {
                bytes[offset + 8 + chunk.Data.Length] ^= 1;
                break;
            }
            offset += 12 + chunk.Data.Length;
        }

        var result = Validate(bytes);

        AssertInvalid(result);
        Assert.Contains("CRC", result.Reason);
    }

    [Theory]
    [InlineData("acTL")]
    [InlineData("fcTL")]
    [InlineData("fdAT")]
    [InlineData("ABCD")]
    public void AnimationAndUnknownCriticalChunks_AreUnsupported(string chunkType)
    {
        var chunks = CreateImage();
        var data = chunkType switch
        {
            "acTL" => new byte[] { 0, 0, 0, 1, 0, 0, 0, 0 },
            "fcTL" => new byte[26],
            "fdAT" => new byte[4],
            _ => Array.Empty<byte>()
        };
        chunks.Insert(1, new(chunkType, data));

        var result = Validate(Encode(chunks));

        Assert.False(result.IsValid);
        Assert.Equal(PngValidationStatus.UnsupportedFeature, result.Status);
        Assert.Null(result.ImageInfo);
        Assert.NotNull(result.Reason);
    }

    [Theory]
    [InlineData("missing_palette")]
    [InlineData("duplicate_palette")]
    [InlineData("palette_after_idat")]
    [InlineData("empty_palette")]
    [InlineData("palette_not_triplets")]
    [InlineData("palette_too_large")]
    [InlineData("palette_exceeds_depth")]
    [InlineData("palette_on_grayscale")]
    [InlineData("palette_on_gray_alpha")]
    public void InvalidPaletteRules_AreRejected(string error)
    {
        var chunks = CreateImage(colorType: 3, bitDepth: 1);
        switch (error)
        {
            case "missing_palette": chunks.RemoveAt(1); break;
            case "duplicate_palette": chunks.Insert(2, chunks[1]); break;
            case "palette_after_idat": (chunks[1], chunks[2]) = (chunks[2], chunks[1]); break;
            case "empty_palette": chunks[1] = new("PLTE", []); break;
            case "palette_not_triplets": chunks[1] = new("PLTE", [0, 0]); break;
            case "palette_too_large": chunks[1] = new("PLTE", new byte[771]); break;
            case "palette_exceeds_depth": chunks[1] = new("PLTE", new byte[9]); break;
            case "palette_on_grayscale": chunks[0].Data[9] = 0; break;
            case "palette_on_gray_alpha": chunks[0].Data[9] = 4; chunks[0].Data[8] = 8; break;
            default: throw new InvalidOperationException(error);
        }

        AssertInvalid(Validate(Encode(chunks)));
    }

    [Fact]
    public void ConsecutiveSplitIdatAndAncillaryChunks_AreAccepted()
    {
        var chunks = CreateImage();
        var data = chunks[1].Data;
        chunks[1] = new("IDAT", data[..3]);
        chunks.Insert(2, new("IDAT", []));
        chunks.Insert(3, new("IDAT", data[3..]));
        chunks.Insert(1, TextChunk());
        chunks.Insert(chunks.Count - 1, new("vpAg", [1, 2, 3])); // Legal private ancillary chunk.

        Assert.True(Validate(Encode(chunks)).IsValid);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EmptyIdatBeforeOrAfterData_IsAccepted(bool emptyFirst)
    {
        var chunks = CreateImage();
        chunks.Insert(emptyFirst ? 1 : 2, new("IDAT", []));

        var result = Validate(Encode(chunks));

        Assert.True(result.IsValid);
        Assert.Equal(PngValidationStatus.Valid, result.Status);
        Assert.NotNull(result.ImageInfo);
    }

    [Fact]
    public void MultipleEmptyIdatChunks_WithoutAggregateData_AreRejected()
    {
        var chunks = CreateImage();
        chunks[1] = new("IDAT", []);
        chunks.Insert(2, new("IDAT", []));

        AssertInvalid(Validate(Encode(chunks)));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(6)]
    public void OptionalPaletteOnTruecolor_IsAccepted(byte colorType)
    {
        var chunks = CreateImage(colorType: colorType);
        chunks.Insert(1, new("PLTE", [0, 0, 0]));

        Assert.True(Validate(Encode(chunks)).IsValid);
    }

    [Fact]
    public void AnyTruncationOfAValidPng_IsInvalid()
    {
        var png = Encode(CreateImage());

        for (var length = 0; length < png.Length; length++)
        {
            AssertInvalid(Validate(png[..length]));
        }
    }

    [Fact]
    public void WrongSignatureAndTrailingData_AreRejected()
    {
        var png = Encode(CreateImage());
        var wrongSignature = (byte[])png.Clone();
        wrongSignature[0] = 0;

        AssertInvalid(Validate(wrongSignature));
        AssertInvalid(Validate([.. png, 0]));
        AssertInvalid(Validate([.. png, .. png]));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HugeDeclaredChunkWithoutPayload_IsRejectedWithBoundedReads(bool seekable)
    {
        var png = Encode(CreateImage());
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(33), int.MaxValue); // First IDAT length.
        using var stream = new ShortReadStream(png, seekable);

        AssertInvalid(new PngMasterValidator().Validate(stream));
        Assert.True(stream.LargestRead <= 8192);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(33), uint.MaxValue);
        AssertInvalid(Validate(png));
    }

    [Fact]
    public void NonSeekableShortReads_ProcessLargeAncillaryChunkInBoundedBlocks()
    {
        var chunks = CreateImage();
        chunks.Insert(1, new("tEXt", Encoding.ASCII.GetBytes("Comment\0" + new string('x', 100_000))));
        using var stream = new ShortReadStream(Encode(chunks), seekable: false);

        Assert.True(new PngMasterValidator().Validate(stream).IsValid);
        Assert.True(stream.LargestRead <= 8192);
        Assert.True(stream.CanRead);
    }

    [Fact]
    public void FileContentControlsValidationAndTheFileIsUnchanged()
    {
        var root = Directory.CreateTempSubdirectory("nap-png-tests-").FullName;
        try
        {
            var png = Encode(CreateImage());
            var imagePath = Path.Combine(root, "image.dat");
            File.WriteAllBytes(imagePath, png);
            var textPath = Path.Combine(root, "fake.png");
            File.WriteAllText(textPath, "This is text, not PNG.");
            var textBytes = File.ReadAllBytes(textPath);

            Assert.True(new PngMasterValidator().Validate(imagePath).IsValid);
            AssertInvalid(new PngMasterValidator().Validate(textPath));
            Assert.Equal(png, File.ReadAllBytes(imagePath));
            Assert.Equal(textBytes, File.ReadAllBytes(textPath));
            Assert.Equal(2, Directory.EnumerateFileSystemEntries(root).Count());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RatioIsExactAndSafeAtIntegerLimits()
    {
        var info = new PngImageInfo(int.MaxValue, int.MaxValue, 8, 6, 0);

        Assert.True(info.HasAspectRatio(int.MaxValue, int.MaxValue));
        Assert.False(info.HasAspectRatio(int.MaxValue, int.MaxValue - 1));
        Assert.False(info.HasAspectRatio(0, 5));
        Assert.False(info.HasAspectRatio(4, -5));
    }

    [Fact]
    public void StructurallyIntactIdat_IsNotClaimedToBeDecoded()
    {
        var chunks = CreateImage();
        chunks[1] = new("IDAT", [1, 2, 3]); // Deliberately invalid zlib, with valid chunk CRC.

        Assert.True(Validate(Encode(chunks)).IsValid); // Documents the structural-only contract.
    }

    private static PngValidationResult Validate(byte[] png)
    {
        using var stream = new MemoryStream(png);
        return new PngMasterValidator().Validate(stream);
    }

    private static void AssertInvalid(PngValidationResult result)
    {
        Assert.False(result.IsValid);
        Assert.Equal(PngValidationStatus.Invalid, result.Status);
        Assert.Null(result.ImageInfo);
        Assert.NotNull(result.Reason);
    }

    // Generates real filtered scanlines and zlib-compressed pixels, including Adam7 pass layout.
    private static List<Chunk> CreateImage(int width = 4, int height = 5, byte colorType = 6,
        byte bitDepth = 8, byte interlace = 0)
    {
        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)height);
        ihdr[8] = bitDepth;
        ihdr[9] = colorType;
        ihdr[12] = interlace;
        var channels = colorType switch { 0 or 3 => 1, 2 => 3, 4 => 2, 6 => 4, _ => throw new ArgumentException() };
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            var passes = interlace == 0
                ? new[] { (0, 0, 1, 1) }
                : new[] { (0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4),
                    (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2) };
            foreach (var (x, y, stepX, stepY) in passes)
            {
                if (width <= x || height <= y) { continue; }
                var passWidth = (width - x + stepX - 1) / stepX;
                var passHeight = (height - y + stepY - 1) / stepY;
                var row = new byte[checked((passWidth * channels * bitDepth + 7) / 8 + 1)];
                for (var line = 0; line < passHeight; line++)
                {
                    zlib.Write(row); // Filter 0 and zero-valued pixels (palette index 0 for indexed images).
                }
            }
        }
        var chunks = new List<Chunk> { new("IHDR", ihdr) };
        if (colorType == 3) { chunks.Add(new("PLTE", [0, 0, 0])); }
        chunks.Add(new("IDAT", compressed.ToArray()));
        chunks.Add(new("IEND", []));
        return chunks;
    }

    private static Chunk TextChunk() => new("tEXt", Encoding.ASCII.GetBytes("Comment\0NAP test"));

    private static byte[] Encode(IEnumerable<Chunk> chunks)
    {
        using var output = new MemoryStream();
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var number = new byte[4];
        foreach (var chunk in chunks)
        {
            BinaryPrimitives.WriteUInt32BigEndian(number, (uint)chunk.Data.Length);
            output.Write(number);
            var type = Encoding.ASCII.GetBytes(chunk.Type);
            output.Write(type);
            output.Write(chunk.Data);
            // Independent bitwise CRC oracle; never calls the production CRC helper.
            uint crc = uint.MaxValue;
            foreach (var value in type.Concat(chunk.Data))
            {
                crc ^= value;
                for (var bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xedb88320;
                }
            }
            BinaryPrimitives.WriteUInt32BigEndian(number, ~crc);
            output.Write(number);
        }
        return output.ToArray();
    }

    private sealed record Chunk(string Type, byte[] Data);

    private sealed class ShortReadStream(byte[] bytes, bool seekable) : Stream
    {
        private readonly MemoryStream _source = new(bytes);
        public int LargestRead { get; private set; }
        public override bool CanRead => _source.CanRead;
        public override bool CanSeek => seekable;
        public override bool CanWrite => false;
        public override long Length => seekable ? _source.Length : throw new NotSupportedException();
        public override long Position
        {
            get => seekable ? _source.Position : throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            LargestRead = Math.Max(LargestRead, buffer.Length);
            return _source.Read(buffer[..Math.Min(buffer.Length, 7)]);
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) { _source.Dispose(); }
            base.Dispose(disposing);
        }
    }
}

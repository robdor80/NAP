using System.Globalization;
using System.Reflection;
using System.Text;
using NAP.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace NAP.Tests;

public sealed class Sha256HasherTests : IDisposable
{
    private const string EmptyHex = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
    private const string AbcHex = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
    private readonly string _root = Directory.CreateTempSubdirectory("nap-sha256-").FullName;
    private readonly Sha256Hasher _hasher = new();

    [Fact]
    public void PublicContractIsSealedStatelessWithExactlyPathAndStreamCompute()
    {
        var type = typeof(Sha256Hasher);
        Assert.True(type.IsSealed);
        Assert.Empty(type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));
        Assert.Empty(Assert.Single(type.GetConstructors()).GetParameters());
        var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly);
        Assert.Equal(2, methods.Length);
        foreach (var (parameterType, name) in new[] { (typeof(string), "path"), (typeof(Stream), "stream") })
        {
            var method = Assert.Single(methods, m => Assert.Single(m.GetParameters()).ParameterType == parameterType);
            Assert.Equal("Compute", method.Name);
            Assert.Equal(typeof(Sha256Digest), method.ReturnType);
            Assert.Equal(name, Assert.Single(method.GetParameters()).Name);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    public void InvalidPathUsesThePathParameter(string? path)
    {
        var exception = Assert.ThrowsAny<ArgumentException>(() => _hasher.Compute(path!));
        Assert.Equal("path", exception.ParamName);
        if (path is null) Assert.IsType<ArgumentNullException>(exception);
        else Assert.IsType<ArgumentException>(exception);
    }

    [Fact]
    public void NullAndUnreadableStreamsUseTheStreamParameter()
    {
        Assert.Equal("stream", Assert.Throws<ArgumentNullException>(() => _hasher.Compute((Stream)null!)).ParamName);
        var disposed = new MemoryStream();
        disposed.Dispose();
        Assert.False(disposed.CanRead);
        Assert.Equal("stream", Assert.Throws<ArgumentException>(() => _hasher.Compute(disposed)).ParamName);
    }

    [Theory]
    [InlineData("", EmptyHex)]
    [InlineData("abc", AbcHex)]
    public void PublishedEmptyAndAbcVectorsAgreeForFilesAndCallerOwnedStreams(string text, string expected)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        var path = Write("vector.bin", bytes);
        using var stream = new MemoryStream(bytes, writable: false);
        var fileDigest = _hasher.Compute(path);
        var streamDigest = _hasher.Compute(stream);
        Assert.Equal(expected, fileDigest.Hex);
        Assert.Equal(expected, streamDigest.Hex);
        Assert.Equal(fileDigest, streamDigest);
        Assert.True(stream.CanRead);
        Assert.Equal(stream.Length, stream.Position);
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal(new[] { path }, Directory.GetFileSystemEntries(_root));
        // An exclusive reopen confirms the path overload disposed its internal handle.
        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
    }

    [Fact]
    public void IdentityDependsOnlyOnBytesAcrossNamesDirectoriesAndRepeatedCalls()
    {
        byte[] bytes = [0, 1, 2, 255, 0, 128, 42];
        var left = Directory.CreateDirectory(Path.Combine(_root, "left")).FullName;
        var right = Directory.CreateDirectory(Path.Combine(_root, "right")).FullName;
        var firstPath = Path.Combine(left, "source.png");
        var secondPath = Path.Combine(right, "unrelated_name.data");
        File.WriteAllBytes(firstPath, bytes);
        File.WriteAllBytes(secondPath, bytes);
        var changed = (byte[])bytes.Clone();
        changed[3] ^= 1;
        var changedPath = Write("changed.bin", changed);
        var before = Directory.GetFileSystemEntries(_root, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        var digest = _hasher.Compute(firstPath);
        Assert.Equal(digest, _hasher.Compute(firstPath));
        Assert.Equal(digest, _hasher.Compute(secondPath));
        Assert.NotEqual(digest, _hasher.Compute(changedPath));
        AssertCanonical(digest);
        Assert.Equal(bytes, File.ReadAllBytes(firstPath));
        Assert.Equal(bytes, File.ReadAllBytes(secondPath));
        Assert.Equal(changed, File.ReadAllBytes(changedPath));
        Assert.Equal(before, Directory.GetFileSystemEntries(_root, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal));
    }

    [Fact]
    public void AllByteValuesMatchAnIndependentFixedReferenceDigest()
    {
        var bytes = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        const string expected = "40aff2e9d2d8922e47afd4648e6967497158785fbd1da870e7110266bf944880";
        using var stream = new MemoryStream(bytes, writable: false);
        Assert.Equal(expected, _hasher.Compute(stream).Hex);
        Assert.Equal(expected, _hasher.Compute(Write("binary.bin", bytes)).Hex);
    }

    [Fact]
    public void UnicodeTextIsHashedAsExactEncodingBytes()
    {
        const string text = "á漢🙂";
        using var utf8 = new MemoryStream(Encoding.UTF8.GetBytes(text), writable: false);
        using var utf16 = new MemoryStream(Encoding.Unicode.GetBytes(text), writable: false);
        var digest = _hasher.Compute(utf8);
        Assert.Equal("81fc34be1b45b013df4a9dbbfe2a14ccbfc75cfced1825c1210643f74029ab7d", digest.Hex);
        Assert.NotEqual(digest, _hasher.Compute(utf16));
    }

    [Fact]
    public void CurrentPositionToEofIsNotRewoundRestoredOrOwnedByHasher()
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("prefixabc"), writable: false);
        stream.Position = "prefix".Length;
        Assert.Equal(AbcHex, _hasher.Compute(stream).Hex);
        Assert.True(stream.CanRead);
        Assert.Equal(stream.Length, stream.Position);
        Assert.Equal(EmptyHex, _hasher.Compute(stream).Hex);
        Assert.Equal(stream.Length, stream.Position);
        stream.Position = 6;
        Assert.Equal(AbcHex, _hasher.Compute(stream).Hex);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(17)]
    public void NonseekableShortReadsDoNotRequireLengthOrPositionAndLeaveStreamOpen(int maximumRead)
    {
        using var stream = new NonseekableReadStream(Encoding.ASCII.GetBytes("abc"), maximumRead);
        Assert.False(stream.CanSeek);
        Assert.Throws<NotSupportedException>(() => stream.Length);
        Assert.Throws<NotSupportedException>(() => stream.Position);
        Assert.Equal(AbcHex, _hasher.Compute(stream).Hex);
        Assert.Equal(3, stream.BytesRead);
        Assert.True(stream.CanRead);
        Assert.Equal(EmptyHex, _hasher.Compute(stream).Hex);
    }

    [Fact]
    public void SignificantGeneratedNonseekableStreamIsCompletelyConsumedWithoutInputMaterialization()
    {
        // 8 MiB of repeating 0x00..0xFF, followed by 0x00..0x24.
        // Fixed reference generated independently of this test, not through the hasher under test.
        using var stream = new NonseekableReadStream(8 * 1024 * 1024 + 37, 8191);
        Assert.Equal("f00eade3ed8b6feb217878ca6f173eef322aae8057341f91e48dbf634217abe7", _hasher.Compute(stream).Hex);
        Assert.Equal(8 * 1024 * 1024 + 37, stream.BytesRead);
        Assert.True(stream.ReadCalls > 1);
        Assert.True(stream.CanRead);
        Assert.Equal(EmptyHex, _hasher.Compute(stream).Hex);
    }

    [Fact]
    public void MissingFilesAndParentsPropagateOperationalExceptions()
    {
        Assert.Throws<FileNotFoundException>(() => _hasher.Compute(Path.Combine(_root, "missing.bin")));
        Assert.Throws<DirectoryNotFoundException>(() => _hasher.Compute(Path.Combine(_root, "missing_parent", "source.bin")));
    }

    [Fact]
    public void ReadFailuresPropagateTheOriginalExceptionAndLeaveCallerStreamOpen()
    {
        var failure = new IOException("Controlled read failure.");
        using var stream = new NonseekableReadStream(failure);
        Assert.Same(failure, Assert.Throws<IOException>(() => _hasher.Compute(stream)));
        Assert.True(stream.CanRead);
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("ar-SA")]
    public void GeneratedHexIsCanonicalUnderDifferentCultures(string cultureName)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            using var stream = new MemoryStream(Encoding.ASCII.GetBytes("abc"), writable: false);
            Assert.Equal(AbcHex, _hasher.Compute(stream).ToString());
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ValidatedPackageSourceCanBeHashedWithoutChangingItsContractsOrContents(bool nimroel)
    {
        var profile = UniverseProfileLoader.Load(nimroel ? UniverseProfileLoaderTests.ConfigPath : UniverseProfileV4LoaderTests.FixturePath);
        var type = nimroel ? "portrait" : "image_asset";
        var productionProfile = nimroel ? "portrait_npc" : "image_profile";
        using var fixture = new PackageSemanticTestFixture(profile, type + "_example_001", type, productionProfile);
        var package = Assert.IsType<ValidatedAssetPackage>(fixture.ValidateReadOnly().Package);
        var source = package.FilesByRole[nimroel ? "master" : "source_image"];
        fixture.AssertReadOnly(() =>
        {
            var digest = _hasher.Compute(source);
            AssertCanonical(digest);
            Assert.Equal(digest, _hasher.Compute(source));
            using var stream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            Assert.Equal(digest, _hasher.Compute(stream));
        });
    }

    [Fact]
    public void ProducedWebpBytesCanBeHashedInMemoryWithoutOutputPersistence()
    {
        var path = Path.Combine(_root, "source.png");
        using (var image = new Image<Rgba32>(4, 5)) image.SaveAsPng(path);
        var original = File.ReadAllBytes(path);
        var result = new PortraitPngToWebpConverter().Convert(path, new PortraitConversionSettings(4, 5, 90, 20));
        Assert.True(result.IsConverted);
        var bytes = result.Image!.ToArray();
        using var stream = new MemoryStream(bytes, writable: false);
        var digest = _hasher.Compute(stream);
        AssertCanonical(digest);
        Assert.True(stream.CanRead);
        stream.Position = 0;
        Assert.Equal(digest, _hasher.Compute(stream));
        Assert.Equal(bytes, result.Image.ToArray());
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal(new[] { path }, Directory.GetFileSystemEntries(_root));
    }

    private static void AssertCanonical(Sha256Digest digest)
    {
        Assert.Equal(64, digest.Hex.Length);
        Assert.All(digest.Hex, character => Assert.True(character is >= '0' and <= '9' or >= 'a' and <= 'f'));
    }

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    // Read-only stream with forbidden seek/Length/Position and optional short reads/failure.
    // Generated mode emits a deterministic sequence without allocating the complete input.
    private sealed class NonseekableReadStream : Stream
    {
        private readonly byte[]? _bytes;
        private readonly long _size;
        private readonly int _maximumRead;
        private readonly IOException? _failure;
        private bool _disposed;
        internal long BytesRead { get; private set; }
        internal int ReadCalls { get; private set; }
        internal NonseekableReadStream(byte[] bytes, int maximumRead) { _bytes = bytes; _size = bytes.Length; _maximumRead = maximumRead; }
        internal NonseekableReadStream(long size, int maximumRead) { _size = size; _maximumRead = maximumRead; }
        internal NonseekableReadStream(IOException failure) { _failure = failure; _maximumRead = 1; }
        public override bool CanRead => !_disposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            if (_failure is not null) throw _failure;
            ReadCalls++;
            var count = (int)Math.Min(Math.Min(buffer.Length, _maximumRead), _size - BytesRead);
            for (var index = 0; index < count; index++)
                buffer[index] = _bytes is null ? (byte)(BytesRead + index) : _bytes[(int)BytesRead + index];
            BytesRead += count;
            return count;
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { _disposed = true; base.Dispose(disposing); }
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}

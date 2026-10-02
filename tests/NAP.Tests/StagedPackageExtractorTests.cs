using System.IO.Compression;
using System.Text;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class StagedPackageExtractorTests
{
    [Fact]
    public async Task ValidZip_ExtractsFilesAndPreservesStagedZip()
    {
        using var context = new TestContext();
        var zip = context.CreateZip("asset.zip", ("one.txt", "one"), ("two.txt", "two"));
        var zipBytes = File.ReadAllBytes(zip);

        var result = await new StagedPackageExtractor().ExtractAsync(zip, context.ExtractionRoot);

        Assert.Equal(StagedPackageExtractionStatus.Extracted, result.Status);
        Assert.Equal(zip, result.StagedZipPath);
        Assert.Equal(Path.Combine(context.ExtractionRoot, "asset"), result.FinalPath);
        Assert.Equal("one", File.ReadAllText(Path.Combine(result.FinalPath!, "one.txt")));
        Assert.Equal("two", File.ReadAllText(Path.Combine(result.FinalPath!, "two.txt")));
        Assert.Equal(zipBytes, File.ReadAllBytes(zip));
        AssertNoTemporaryDirectory(context.ExtractionRoot);
    }

    [Fact]
    public async Task NestedEntries_PreserveNormalStructure()
    {
        using var context = new TestContext();
        var zip = context.CreateZip("asset.zip", ("folder/", null), ("folder/nested/file.txt", "data"));

        var result = await new StagedPackageExtractor().ExtractAsync(zip, context.ExtractionRoot);

        Assert.Equal(StagedPackageExtractionStatus.Extracted, result.Status);
        Assert.Equal("data", File.ReadAllText(Path.Combine(result.FinalPath!, "folder", "nested", "file.txt")));
    }

    [Fact]
    public async Task EmptyZip_PublishesEmptyDirectory()
    {
        using var context = new TestContext();
        var zip = context.CreateZip("empty.zip");

        var result = await new StagedPackageExtractor().ExtractAsync(zip, context.ExtractionRoot);

        Assert.Equal(StagedPackageExtractionStatus.Extracted, result.Status);
        Assert.Empty(Directory.EnumerateFileSystemEntries(result.FinalPath!));
        AssertNoTemporaryDirectory(context.ExtractionRoot);
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("..\\evil.txt")]
    [InlineData("../../evil.txt")]
    [InlineData("folder/../../../evil.txt")]
    [InlineData("/absolute/path.txt")]
    [InlineData("\\absolute\\path.txt")]
    [InlineData("C:\\evil.txt")]
    [InlineData("C:/evil.txt")]
    [InlineData("folder\\..\\evil.txt")]
    [InlineData("\\\\server\\share\\evil.txt")]
    [InlineData("C:evil.txt")]
    [InlineData("file.txt:stream")]
    [InlineData("folder./file.txt")]
    [InlineData("folder /file.txt")]
    [InlineData("NUL.txt")]
    [InlineData("COM¹.txt")]
    [InlineData("CON .txt")]
    [InlineData("CONOUT$")]
    [InlineData("folder//file.txt")]
    public async Task UnsafePath_IsRejectedWithoutWritingOutsideRoot(string entryName)
    {
        using var context = new TestContext();
        var zip = context.CreateZip("asset.zip", ("safe.txt", "safe"), (entryName, "evil"));
        var outsidePath = Path.Combine(context.Root, "evil.txt");

        var result = await new StagedPackageExtractor().ExtractAsync(zip, context.ExtractionRoot);

        Assert.Equal(StagedPackageExtractionStatus.Rejected, result.Status);
        Assert.NotNull(result.Reason);
        Assert.False(File.Exists(outsidePath));
        Assert.False(Directory.Exists(Path.Combine(context.ExtractionRoot, "asset")));
        AssertNoTemporaryDirectory(context.ExtractionRoot);
    }

    [Fact]
    public async Task ExistingFinalDirectory_IsCollisionAndUntouched()
    {
        using var context = new TestContext();
        var zip = context.CreateZip("asset.zip", ("new.txt", "new"));
        var final = Directory.CreateDirectory(Path.Combine(context.ExtractionRoot, "asset")).FullName;
        var existing = Path.Combine(final, "existing.txt");
        File.WriteAllText(existing, "keep");

        var result = await new StagedPackageExtractor().ExtractAsync(zip, context.ExtractionRoot);

        Assert.Equal(StagedPackageExtractionStatus.Collision, result.Status);
        Assert.Null(result.FinalPath);
        Assert.Equal("keep", File.ReadAllText(existing));
        Assert.False(File.Exists(Path.Combine(final, "new.txt")));
        AssertNoTemporaryDirectory(context.ExtractionRoot);
    }

    [Fact]
    public async Task CaseInsensitiveFinalDirectoryCollision_IsRejectedOnLinuxToo()
    {
        using var context = new TestContext();
        var zip = context.CreateZip("asset.zip", ("new.txt", "new"));
        Directory.CreateDirectory(Path.Combine(context.ExtractionRoot, "ASSET"));

        var result = await new StagedPackageExtractor().ExtractAsync(zip, context.ExtractionRoot);

        Assert.Equal(StagedPackageExtractionStatus.Collision, result.Status);
        AssertNoTemporaryDirectory(context.ExtractionRoot);
    }

    [Fact]
    public async Task UnrelatedExtractionRootContent_IsPreserved()
    {
        using var context = new TestContext();
        var unrelated = Path.Combine(context.ExtractionRoot, "keep.txt");
        File.WriteAllText(unrelated, "keep");
        var zip = context.CreateZip("asset.zip", ("file.txt", "data"));

        var result = await new StagedPackageExtractor().ExtractAsync(zip, context.ExtractionRoot);

        Assert.Equal(StagedPackageExtractionStatus.Extracted, result.Status);
        Assert.Equal("keep", File.ReadAllText(unrelated));
    }

    [Theory]
    [InlineData("Data/File.txt", "data/file.txt")]
    [InlineData("file.txt", "FILE.TXT")]
    [InlineData("folder/file.txt", "FOLDER/FILE.TXT")]
    [InlineData("Data/one.txt", "data/two.txt")]
    [InlineData("folder\\file.txt", "folder/file.txt")]
    public async Task CaseInsensitiveEntryCollision_IsRejected(string first, string second)
    {
        using var context = new TestContext();
        var zip = context.CreateZip("asset.zip", (first, "one"), (second, "two"));

        var result = await new StagedPackageExtractor().ExtractAsync(zip, context.ExtractionRoot);

        Assert.Equal(StagedPackageExtractionStatus.Rejected, result.Status);
        Assert.False(Directory.Exists(Path.Combine(context.ExtractionRoot, "asset")));
        AssertNoTemporaryDirectory(context.ExtractionRoot);
    }

    [Theory]
    [InlineData("file", "file/child.txt")]
    [InlineData("folder/child.txt", "folder")]
    [InlineData("folder/", "folder")]
    public async Task FileDirectoryCollision_IsRejected(string first, string second)
    {
        using var context = new TestContext();
        var zip = context.CreateZip("asset.zip", (first, first.EndsWith('/') ? null : "one"),
            (second, second.EndsWith('/') ? null : "two"));

        var result = await new StagedPackageExtractor().ExtractAsync(zip, context.ExtractionRoot);

        Assert.Equal(StagedPackageExtractionStatus.Rejected, result.Status);
        AssertNoTemporaryDirectory(context.ExtractionRoot);
    }

    [Fact]
    public async Task CorruptZip_DoesNotPublishFinalDirectory()
    {
        using var context = new TestContext();
        var zip = Path.Combine(context.Root, "asset.zip");
        File.WriteAllText(zip, "this is not a zip archive");

        var result = await new StagedPackageExtractor().ExtractAsync(zip, context.ExtractionRoot);

        Assert.Equal(StagedPackageExtractionStatus.InvalidArchive, result.Status);
        Assert.NotNull(result.Reason);
        Assert.False(Directory.Exists(Path.Combine(context.ExtractionRoot, "asset")));
        AssertNoTemporaryDirectory(context.ExtractionRoot);
    }

    [Fact]
    public async Task CorruptEntryAfterEarlierFile_CleansOperationTemporaryDirectory()
    {
        using var context = new TestContext();
        var zip = context.CreateZip("asset.zip", ("first.txt", "valid"),
            ("second.txt", new string('x', 200)));
        var bytes = File.ReadAllBytes(zip);
        var secondHeader = FindNthLocalHeader(bytes, 2);
        var nameLength = BitConverter.ToUInt16(bytes, secondHeader + 26);
        var extraLength = BitConverter.ToUInt16(bytes, secondHeader + 28);
        var compressedStart = secondHeader + 30 + nameLength + extraLength;
        Array.Fill(bytes, (byte)0xFF, compressedStart, 4);
        File.WriteAllBytes(zip, bytes);

        var result = await new StagedPackageExtractor().ExtractAsync(zip, context.ExtractionRoot);

        Assert.Equal(StagedPackageExtractionStatus.InvalidArchive, result.Status);
        Assert.False(Directory.Exists(Path.Combine(context.ExtractionRoot, "asset")));
        AssertNoTemporaryDirectory(context.ExtractionRoot);
    }

    [Theory]
    [InlineData(1, 100, 100, "first.txt", "second.txt")]
    [InlineData(10, 2, 100, "large.txt", null)]
    [InlineData(10, 100, 3, "first.txt", "second.txt")]
    public async Task Limits_AreEnforced(int maxEntries, long maxEntryBytes, long maxTotalBytes,
        string first, string? second)
    {
        using var context = new TestContext();
        var zip = second is null
            ? context.CreateZip("asset.zip", (first, "1234"))
            : context.CreateZip("asset.zip", (first, "12"), (second, "34"));
        var options = new StagedPackageExtractionOptions
        {
            MaxEntries = maxEntries,
            MaxEntryBytes = maxEntryBytes,
            MaxTotalBytes = maxTotalBytes
        };

        var result = await new StagedPackageExtractor(options).ExtractAsync(zip, context.ExtractionRoot);

        Assert.Equal(StagedPackageExtractionStatus.Rejected, result.Status);
        Assert.False(Directory.Exists(Path.Combine(context.ExtractionRoot, "asset")));
        AssertNoTemporaryDirectory(context.ExtractionRoot);
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(1, 1, -1)]
    public void InvalidOptions_AreRejected(int entries, long entryBytes, long totalBytes)
    {
        var options = new StagedPackageExtractionOptions
        {
            MaxEntries = entries,
            MaxEntryBytes = entryBytes,
            MaxTotalBytes = totalBytes
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => new StagedPackageExtractor(options));
    }

    [Fact]
    public async Task Cancellation_DoesNotPublishOrLeaveTemporaryDirectory()
    {
        using var context = new TestContext();
        var zip = context.CreateZip("asset.zip", ("file.txt", "data"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new StagedPackageExtractor().ExtractAsync(zip, context.ExtractionRoot, cancellation.Token));

        Assert.False(Directory.Exists(Path.Combine(context.ExtractionRoot, "asset")));
        AssertNoTemporaryDirectory(context.ExtractionRoot);
    }

    [Fact]
    public async Task DeclaredSymlink_IsRejected()
    {
        using var context = new TestContext();
        var zip = Path.Combine(context.Root, "asset.zip");
        using (var file = File.Create(zip))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("link");
            entry.ExternalAttributes = 0xA000 << 16;
            using var writer = new StreamWriter(entry.Open());
            writer.Write("../outside");
        }

        var result = await new StagedPackageExtractor().ExtractAsync(zip, context.ExtractionRoot);

        Assert.Equal(StagedPackageExtractionStatus.Rejected, result.Status);
        AssertNoTemporaryDirectory(context.ExtractionRoot);
    }

    [Fact]
    public async Task DuplicateEntry_CannotOverwriteAnExtractedFile()
    {
        using var context = new TestContext();
        var zip = context.CreateZip("asset.zip", ("file.txt", "one"), ("file.txt", "two"));

        var result = await new StagedPackageExtractor().ExtractAsync(zip, context.ExtractionRoot);

        Assert.Equal(StagedPackageExtractionStatus.Rejected, result.Status);
        Assert.False(Directory.Exists(Path.Combine(context.ExtractionRoot, "asset")));
    }

    [Fact]
    public async Task ExistingFinalFile_IsCollisionAndUntouched()
    {
        using var context = new TestContext();
        var zip = context.CreateZip("asset.zip", ("file.txt", "data"));
        var final = Path.Combine(context.ExtractionRoot, "asset");
        File.WriteAllText(final, "keep");

        var result = await new StagedPackageExtractor().ExtractAsync(zip, context.ExtractionRoot);

        Assert.Equal(StagedPackageExtractionStatus.Collision, result.Status);
        Assert.Null(result.FinalPath);
        Assert.Equal("keep", File.ReadAllText(final));
        AssertNoTemporaryDirectory(context.ExtractionRoot);
    }

    [Fact]
    public async Task MissingExtractionRoot_IsCreated()
    {
        using var context = new TestContext();
        var zip = context.CreateZip("asset.zip", ("file.txt", "data"));
        var root = Path.Combine(context.ExtractionRoot, "new", "nested");

        var result = await new StagedPackageExtractor().ExtractAsync(zip, root);

        Assert.Equal(StagedPackageExtractionStatus.Extracted, result.Status);
        Assert.Equal("data", File.ReadAllText(Path.Combine(result.FinalPath!, "file.txt")));
    }

    [Theory]
    [InlineData(0x1000 << 16)] // FIFO
    [InlineData(0x6000 << 16)] // Block device
    [InlineData(0x400)] // Windows reparse point
    [InlineData(0x10)] // Directory attributes on a file entry
    public async Task SpecialEntryTypes_AreRejected(int attributes)
    {
        using var context = new TestContext();
        var zip = context.CreateZip("asset.zip", ("special", "data"));
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update))
        {
            archive.Entries[0].ExternalAttributes = attributes;
        }

        var result = await new StagedPackageExtractor().ExtractAsync(zip, context.ExtractionRoot);

        Assert.Equal(StagedPackageExtractionStatus.Rejected, result.Status);
        Assert.Null(result.FinalPath);
        AssertNoTemporaryDirectory(context.ExtractionRoot);
    }

    [Fact]
    public async Task RootThroughJunction_IsRejectedWithoutChangingTarget()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        using var context = new TestContext();
        var zip = context.CreateZip("asset.zip", ("file.txt", "data"));
        var target = Directory.CreateDirectory(Path.Combine(context.Root, "target")).FullName;
        var keep = Path.Combine(target, "keep.txt");
        File.WriteAllText(keep, "keep");
        var link = Path.Combine(context.Root, "junction");
        // Windows junctions do not require developer mode or symlink privileges.
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c mklink /J \"{link}\" \"{target}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        })!;
        await process.WaitForExitAsync();
        Assert.Equal(0, process.ExitCode);
        try
        {
            var result = await new StagedPackageExtractor().ExtractAsync(zip, Path.Combine(link, "nested"));

            Assert.Equal(StagedPackageExtractionStatus.Rejected, result.Status);
            Assert.Equal("keep", File.ReadAllText(keep));
            Assert.False(Directory.Exists(Path.Combine(target, "nested")));
        }
        finally
        {
            Directory.Delete(link); // Remove only the junction, never its target.
        }
    }

    [Fact]
    public async Task CancellationDuringExtraction_CleansOnlyItsTemporaryDirectory()
    {
        using var context = new TestContext();
        var zip = Path.Combine(context.Root, "asset.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        using (var entry = archive.CreateEntry("large.bin").Open())
        {
            var block = new byte[1024 * 1024];
            for (var i = 0; i < 128; i++)
            {
                entry.Write(block);
            }
        }
        var zipBytes = File.ReadAllBytes(zip);
        var unrelated = Directory.CreateDirectory(Path.Combine(context.ExtractionRoot, ".unrelated.extracting")).FullName;
        File.WriteAllText(Path.Combine(unrelated, "keep.txt"), "keep");
        using var cancellation = new CancellationTokenSource();
        var extraction = new StagedPackageExtractor().ExtractAsync(zip, context.ExtractionRoot, cancellation.Token);
        var observedPartial = false;
        while (!extraction.IsCompleted)
        {
            observedPartial = Directory.EnumerateDirectories(context.ExtractionRoot, ".nap-*.extracting")
                .Any(path => new FileInfo(Path.Combine(path, "large.bin")) is { Exists: true, Length: > 0 });
            if (observedPartial)
            {
                cancellation.Cancel();
                break;
            }
            await Task.Delay(1);
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => extraction);
        Assert.True(observedPartial);
        Assert.False(Directory.Exists(Path.Combine(context.ExtractionRoot, "asset")));
        Assert.Empty(Directory.EnumerateDirectories(context.ExtractionRoot, ".nap-*.extracting"));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(unrelated, "keep.txt")));
        Assert.Equal(zipBytes, File.ReadAllBytes(zip));
    }

    [Theory]
    [InlineData(4, 100)]
    [InlineData(100, 4)]
    public async Task UnderstatedEntryLength_CannotPublishTruncatedContent(long maxEntryBytes, long maxTotalBytes)
    {
        using var context = new TestContext();
        var zip = context.CreateZip("asset.zip", ("file.txt", new string('x', 200)));
        var bytes = File.ReadAllBytes(zip);
        var centralHeader = -1;
        for (var i = 0; i <= bytes.Length - 4; i++)
        {
            if (bytes[i] == 0x50 && bytes[i + 1] == 0x4B && bytes[i + 2] == 0x01 && bytes[i + 3] == 0x02)
            {
                centralHeader = i;
                break;
            }
        }
        Assert.True(centralHeader >= 0);
        BitConverter.GetBytes(1U).CopyTo(bytes, centralHeader + 24);
        File.WriteAllBytes(zip, bytes);
        var unrelated = Path.Combine(context.ExtractionRoot, "keep.txt");
        File.WriteAllText(unrelated, "keep");
        var extractor = new StagedPackageExtractor(new StagedPackageExtractionOptions
        {
            MaxEntryBytes = maxEntryBytes,
            MaxTotalBytes = maxTotalBytes
        });

        var result = await extractor.ExtractAsync(zip, context.ExtractionRoot);

        Assert.Equal(StagedPackageExtractionStatus.InvalidArchive, result.Status);
        Assert.Contains("CRC", result.Reason);
        Assert.Null(result.FinalPath);
        AssertNoTemporaryDirectory(context.ExtractionRoot);
        Assert.Equal("keep", File.ReadAllText(unrelated));
        Assert.Equal(bytes, File.ReadAllBytes(zip));
    }

    [Fact]
    public async Task StoredEntryWithCorruptPayload_IsRejectedByCrc()
    {
        using var context = new TestContext();
        var zip = Path.Combine(context.Root, "asset.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        using (var stream = archive.CreateEntry("file.txt", CompressionLevel.NoCompression).Open())
        {
            stream.Write(Encoding.UTF8.GetBytes("original"));
        }
        var bytes = File.ReadAllBytes(zip);
        var header = FindNthLocalHeader(bytes, 1);
        var dataOffset = header + 30 + BitConverter.ToUInt16(bytes, header + 26) +
            BitConverter.ToUInt16(bytes, header + 28);
        bytes[dataOffset] ^= 1;
        File.WriteAllBytes(zip, bytes);

        var result = await new StagedPackageExtractor().ExtractAsync(zip, context.ExtractionRoot);

        Assert.Equal(StagedPackageExtractionStatus.InvalidArchive, result.Status);
        Assert.Contains("CRC", result.Reason);
        Assert.Null(result.FinalPath);
        AssertNoTemporaryDirectory(context.ExtractionRoot);
    }

    [Fact]
    public async Task Zip64EndRecords_AreSupported()
    {
        using var context = new TestContext();
        var zip = context.CreateZip("asset.zip", ("file.txt", "data"));
        var bytes = File.ReadAllBytes(zip);
        var end = bytes.Length - 22;
        using (var output = new MemoryStream())
        using (var writer = new BinaryWriter(output))
        {
            writer.Write(bytes, 0, end);
            writer.Write(0x06064b50U);
            writer.Write(44UL);
            writer.Write((ushort)45);
            writer.Write((ushort)45);
            writer.Write(0U);
            writer.Write(0U);
            writer.Write(1UL);
            writer.Write(1UL);
            writer.Write((ulong)BitConverter.ToUInt32(bytes, end + 12));
            writer.Write((ulong)BitConverter.ToUInt32(bytes, end + 16));
            writer.Write(0x07064b50U);
            writer.Write(0U);
            writer.Write((ulong)end);
            writer.Write(1U);
            Array.Fill(bytes, (byte)0xff, end + 8, 12);
            writer.Write(bytes, end, 22);
            File.WriteAllBytes(zip, output.ToArray());
        }

        var result = await new StagedPackageExtractor().ExtractAsync(zip, context.ExtractionRoot);

        Assert.Equal(StagedPackageExtractionStatus.Extracted, result.Status);
        Assert.Equal("data", File.ReadAllText(Path.Combine(result.FinalPath!, "file.txt")));
    }

    [Fact]
    public async Task ZipWithArchiveComment_ExtractsCorrectly()
    {
        using var context = new TestContext();
        var zip = context.CreateZip("asset.zip", ("file.txt", "data"));
        var bytes = File.ReadAllBytes(zip);
        var comment = Encoding.UTF8.GetBytes("NAP test comment");
        BitConverter.GetBytes((ushort)comment.Length).CopyTo(bytes, bytes.Length - 2);
        File.WriteAllBytes(zip, bytes.Concat(comment).ToArray());

        var result = await new StagedPackageExtractor().ExtractAsync(zip, context.ExtractionRoot);

        Assert.Equal(StagedPackageExtractionStatus.Extracted, result.Status);
    }

    private static void AssertNoTemporaryDirectory(string extractionRoot) =>
        Assert.Empty(Directory.EnumerateDirectories(extractionRoot, "*.extracting", SearchOption.TopDirectoryOnly));

    private static int FindNthLocalHeader(byte[] bytes, int occurrence)
    {
        for (var i = 0; i <= bytes.Length - 4; i++)
        {
            if (bytes[i] == 0x50 && bytes[i + 1] == 0x4B && bytes[i + 2] == 0x03 && bytes[i + 3] == 0x04 &&
                --occurrence == 0)
            {
                return i;
            }
        }

        throw new InvalidOperationException("ZIP local entry header was not found.");
    }

    private sealed class TestContext : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("nap-extract-tests-").FullName;
        public string ExtractionRoot { get; }

        public TestContext()
        {
            ExtractionRoot = Directory.CreateDirectory(Path.Combine(Root, "extraction")).FullName;
        }

        public string CreateZip(string name, params (string Path, string? Content)[] entries)
        {
            var zip = Path.Combine(Root, name);
            using var file = File.Create(zip);
            using var archive = new ZipArchive(file, ZipArchiveMode.Create);
            foreach (var (path, content) in entries)
            {
                var entry = archive.CreateEntry(path);
                if (content is not null)
                {
                    using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                    writer.Write(content);
                }
            }
            return zip;
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}

using System.Buffers.Binary;
using NAP.Core;
using Xunit;
using static NAP.Tests.AdversarialZipFactory;

namespace NAP.Tests;

public sealed class AdversarialZipTests
{
    // Inventory: previous tests cover dot-dot/backslash escape, absolute/UNC/ADS paths,
    // case/slash duplicate paths, file/parent collisions, CRC, declared limits,
    // cancellation, symlink/FIFO/block/reparse entries, junction roots and valid ZIP64.
    // These cases add missing segment/name classes, archive structure and local mapping.
    [Theory]
    [InlineData("./file")]
    [InlineData("folder/../file")]
    [InlineData("folder/..\\outside.txt")]
    [InlineData("folder\\/file")]
    [InlineData("file.")]
    [InlineData("file ")]
    [InlineData("folder/\u0000file")]
    [InlineData("folder/\u0001file")]
    [InlineData("folder/\u007ffile")]
    [InlineData("file<name")]
    [InlineData("file>name")]
    [InlineData("file\"name")]
    [InlineData("file|name")]
    [InlineData("file?name")]
    [InlineData("file*name")]
    public async Task MissingUnsafeSegmentClasses_AreRejected(string name)
    {
        using var context = new Context();
        await context.AssertFailure(Create(Text("first.txt"), Text(name)), StagedPackageExtractionStatus.Rejected);
    }

    public static IEnumerable<object[]> ReservedNames()
    {
        foreach (var name in new[] { "CON", "PRN", "AUX", "NUL", "con.txt", "NUL.png", "com1.data", "lpt9.txt" })
            yield return [name];
        for (var number = 1; number <= 9; number++)
        {
            yield return [$"COM{number}"];
            yield return [$"LPT{number}"];
        }
    }

    [Theory]
    [MemberData(nameof(ReservedNames))]
    public async Task ReservedDeviceNameFamilies_AreRejectedPortably(string name)
    {
        using var context = new Context();
        await context.AssertFailure(Create(Text("first.txt"), Text("nested/" + name)), StagedPackageExtractionStatus.Rejected);
    }

    [Theory]
    [InlineData("empty_name")]
    [InlineData("directory_with_data")]
    [InlineData("unix_char_device")]
    [InlineData("unix_socket")]
    [InlineData("directory_marked_regular")]
    public async Task MissingEntryTypeAndEmptyNameCases_AreRejected(string anomaly)
    {
        using var context = new Context();
        var entry = anomaly switch
        {
            "empty_name" => Text(""),
            "directory_with_data" => Text("directory/"),
            "unix_char_device" => Text("special", attributes: 0x2000 << 16),
            "unix_socket" => Text("special", attributes: 0xC000 << 16),
            "directory_marked_regular" => new Entry("directory/", [], 0x8000 << 16),
            _ => throw new ArgumentException(nameof(anomaly))
        };
        await context.AssertFailure(Create(Text("first.txt"), entry), StagedPackageExtractionStatus.Rejected);
    }

    [Fact]
    public async Task ImplicitDirectoryThenMatchingExplicitDirectory_IsAccepted()
    {
        using var context = new Context();
        var fixture = Create(Text("directory/file.txt"), new Entry("directory/", []));
        await context.AssertSuccess(fixture, "directory/file.txt", "data");
    }

    [Fact]
    public async Task ImplicitDirectoryThenCaseDifferentExplicitDirectory_IsRejected()
    {
        using var context = new Context();
        await context.AssertFailure(Create(Text("directory/file.txt"), new Entry("DIRECTORY/", [])), StagedPackageExtractionStatus.Rejected);
    }

    [Theory]
    [InlineData("local_signature")]
    [InlineData("local_name_length")]
    [InlineData("local_extra_length")]
    [InlineData("central_signature")]
    [InlineData("central_name_length")]
    [InlineData("local_offset")]
    [InlineData("local_offset_one_byte_remaining")]
    [InlineData("local_offset_three_bytes_remaining")]
    [InlineData("compression_method")]
    [InlineData("end_signature")]
    [InlineData("end_comment_length")]
    [InlineData("conflicting_counts")]
    [InlineData("central_offset")]
    public async Task ArchiveStructureCorruption_IsInvalid(string anomaly)
    {
        using var context = new Context();
        var fixture = Create(Text("first.txt"), Text("second.txt"));
        var bytes = fixture.Bytes;
        var local = fixture.LocalHeaders[1];
        var central = fixture.CentralHeaders[1];
        switch (anomaly)
        {
            case "local_signature": bytes[local] ^= 1; break;
            case "local_name_length": U16(bytes, local + 26, ushort.MaxValue); break;
            case "local_extra_length": U16(bytes, local + 28, ushort.MaxValue); break;
            case "central_signature": bytes[central] ^= 1; break;
            case "central_name_length": U16(bytes, central + 28, ushort.MaxValue); break;
            case "local_offset": U32(bytes, central + 42, (uint)bytes.Length); break;
            case "local_offset_one_byte_remaining": U32(bytes, central + 42, (uint)bytes.Length - 1); break;
            case "local_offset_three_bytes_remaining": U32(bytes, central + 42, (uint)bytes.Length - 3); break;
            case "compression_method": U16(bytes, local + 8, 99); U16(bytes, central + 10, 99); break;
            case "end_signature": bytes[fixture.EndOffset] ^= 1; break;
            case "end_comment_length": U16(bytes, fixture.EndOffset + 20, 1); break;
            case "conflicting_counts": U16(bytes, fixture.EndOffset + 8, 1); break;
            case "central_offset": U32(bytes, fixture.EndOffset + 16, (uint)bytes.Length); break;
        }
        await context.AssertFailure(fixture, StagedPackageExtractionStatus.InvalidArchive);
    }

    [Theory]
    [InlineData("local_header")]
    [InlineData("central_header")]
    [InlineData("end_record")]
    public async Task TruncatedStructures_AreInvalid(string structure)
    {
        using var context = new Context();
        var fixture = Create(Text("first.txt"));
        var cut = structure switch
        {
            "local_header" => fixture.LocalHeaders[0] + 12,
            "central_header" => fixture.CentralHeaders[0] + 20,
            "end_record" => fixture.EndOffset + 10,
            _ => throw new ArgumentException(nameof(structure))
        };
        await context.AssertFailure(fixture with { Bytes = fixture.Bytes[..cut] }, StagedPackageExtractionStatus.InvalidArchive);
    }

    [Theory]
    [InlineData("locator_signature")]
    [InlineData("record_offset")]
    [InlineData("record_signature")]
    [InlineData("conflicting_counts")]
    public async Task MalformedZip64Metadata_IsInvalid(string anomaly)
    {
        using var context = new Context();
        var fixture = WithZip64(Create(Text("first.txt")));
        var locator = fixture.EndOffset - 20;
        var record = locator - 56;
        switch (anomaly)
        {
            case "locator_signature": fixture.Bytes[locator] ^= 1; break;
            case "record_offset": BinaryPrimitives.WriteUInt64LittleEndian(fixture.Bytes.AsSpan(locator + 8), ulong.MaxValue); break;
            case "record_signature": fixture.Bytes[record] ^= 1; break;
            case "conflicting_counts": BinaryPrimitives.WriteUInt64LittleEndian(fixture.Bytes.AsSpan(record + 24), 2); break;
        }
        await context.AssertFailure(fixture, StagedPackageExtractionStatus.InvalidArchive);
    }

    [Fact]
    public async Task SplitArchiveIndicator_IsRejectedByExistingPolicy()
    {
        using var context = new Context();
        var fixture = Create(Text("first.txt"));
        U16(fixture.Bytes, fixture.EndOffset + 4, 1);
        await context.AssertFailure(fixture, StagedPackageExtractionStatus.Rejected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LargeDeclaredLength_IsRejectedWithoutAllocatingThatLength(bool totalLimit)
    {
        using var context = new Context();
        var fixture = Create(Text("first.txt"), Text("second.txt"));
        U32(fixture.Bytes, fixture.CentralHeaders[1] + 24, uint.MaxValue - 1);
        var options = new StagedPackageExtractionOptions
        { MaxEntryBytes = totalLimit ? long.MaxValue : 32, MaxTotalBytes = totalLimit ? 32 : long.MaxValue };
        await context.AssertFailure(fixture, StagedPackageExtractionStatus.Rejected, options);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StoredBytesExceedingUnderstatedLength_CannotPublish(bool totalLimit)
    {
        using var context = new Context();
        var fixture = Create(Text("first.txt"), Text("second.txt", new string('x', 512)));
        U32(fixture.Bytes, fixture.CentralHeaders[1] + 24, 1);
        var options = new StagedPackageExtractionOptions
        { MaxEntryBytes = totalLimit ? 1024 : 32, MaxTotalBytes = totalLimit ? 32 : 1024 };
        await context.AssertFailure(fixture, StagedPackageExtractionStatus.Rejected, options);
    }

    [Fact]
    public async Task ValidCompressedContentWithNoPackageMetadata_RemainsOpaque()
    {
        using var context = new Context();
        await context.AssertSuccess(Create(Text("arbitrary.bin", "opaque data", deflated: true)), "arbitrary.bin", "opaque data");
    }

    private static void U16(byte[] bytes, int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);
    private static void U32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);

    private sealed class Context : IDisposable
    {
        private string Root { get; } = Directory.CreateTempSubdirectory("nap-adversarial-").FullName;
        private string ExtractionRoot { get; }
        private string ZipPath => Path.Combine(Root, "staged.zip");

        internal Context()
        {
            ExtractionRoot = Directory.CreateDirectory(Path.Combine(Root, "extraction")).FullName;
            File.WriteAllText(Path.Combine(Root, "outside.txt"), "outside sentinel");
            File.WriteAllText(Path.Combine(ExtractionRoot, "keep.txt"), "root sentinel");
            var unrelated = Directory.CreateDirectory(Path.Combine(ExtractionRoot, ".nap-00000000000000000000000000000000.extracting")).FullName;
            File.WriteAllText(Path.Combine(unrelated, "keep.txt"), "unrelated temporary sentinel");
        }

        internal async Task AssertFailure(Fixture fixture, StagedPackageExtractionStatus expected,
            StagedPackageExtractionOptions? options = null)
        {
            File.WriteAllBytes(ZipPath, fixture.Bytes);
            var before = SnapshotFiles();
            var directories = SnapshotDirectories();
            var result = await new StagedPackageExtractor(options).ExtractAsync(ZipPath, ExtractionRoot);
            Assert.Equal(expected, result.Status);
            Assert.Null(result.FinalPath);
            Assert.False(Directory.Exists(Path.Combine(ExtractionRoot, "staged")));
            Assert.Equal(directories, SnapshotDirectories()); // No new temporaries, final directory or escaped directories.
            AssertFilesUnchanged(before); // Staged ZIP and every sentinel, including outside ExtractionRoot.
            Assert.Equal(ZipPath, result.StagedZipPath);
            var issue = Assert.IsType<NapIssue>(NapIssueMapper.Map(result));
            Assert.Equal(expected == StagedPackageExtractionStatus.Rejected ? NapIssueCodes.ZipRejected : NapIssueCodes.ZipInvalidArchive, issue.Code);
            Assert.Equal(NapIssueSeverity.Error, issue.Severity);
            Assert.Equal(NapIssueDisposition.Stop, issue.Disposition);
            Assert.True(issue.StopsProcessing);
            Assert.Equal(ZipPath, issue.SubjectPath);
            Assert.NotNull(result.Reason);
            Assert.Equal(result.Reason, issue.Detail);
        }

        internal async Task AssertSuccess(Fixture fixture, string relativePath, string expectedContent)
        {
            File.WriteAllBytes(ZipPath, fixture.Bytes);
            var before = SnapshotFiles();
            var result = await new StagedPackageExtractor().ExtractAsync(ZipPath, ExtractionRoot);
            Assert.Equal(StagedPackageExtractionStatus.Extracted, result.Status);
            Assert.Equal(expectedContent, File.ReadAllText(Path.Combine(result.FinalPath!, relativePath)));
            Assert.Null(NapIssueMapper.Map(result));
            foreach (var (path, bytes) in before)
                Assert.Equal(bytes, File.ReadAllBytes(path));
            Assert.Single(Directory.EnumerateDirectories(ExtractionRoot, "*.extracting")); // Only the preexisting unrelated directory.
        }

        private Dictionary<string, byte[]> SnapshotFiles() => Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);
        private string[] SnapshotDirectories() => Directory.EnumerateDirectories(Root, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();
        private void AssertFilesUnchanged(Dictionary<string, byte[]> before)
        {
            var after = SnapshotFiles();
            Assert.Equal(before.Keys.OrderBy(path => path, StringComparer.Ordinal), after.Keys.OrderBy(path => path, StringComparer.Ordinal));
            foreach (var (path, bytes) in before)
                Assert.Equal(bytes, after[path]);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}

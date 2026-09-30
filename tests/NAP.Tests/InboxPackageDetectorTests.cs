using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class InboxPackageDetectorTests
{
    [Fact]
    public void Detect_EmptyInbox_ReturnsNoCandidates()
    {
        var inboxPath = CreateTemporaryDirectory();

        try
        {
            var candidates = new InboxPackageDetector().Detect(inboxPath);

            Assert.Empty(candidates);
        }
        finally
        {
            DeleteTemporaryDirectory(inboxPath);
        }
    }

    [Fact]
    public void Detect_ZipFile_ReturnsOneCandidate()
    {
        var inboxPath = CreateTemporaryDirectory();
        var zipPath = Path.Combine(inboxPath, "asset.zip");
        File.WriteAllText(zipPath, "not opened by detection");

        try
        {
            var candidates = new InboxPackageDetector().Detect(inboxPath);

            var candidate = Assert.Single(candidates);
            Assert.Equal("asset.zip", candidate.FileName);
            Assert.Equal(Path.GetFullPath(zipPath), candidate.FullPath);
        }
        finally
        {
            DeleteTemporaryDirectory(inboxPath);
        }
    }

    [Fact]
    public void Detect_MultipleZipFiles_ReturnsAllCandidates()
    {
        var inboxPath = CreateTemporaryDirectory();
        var fileNames = new[] { "first.zip", "second.zip", "third.zip" };
        foreach (var fileName in fileNames)
        {
            File.WriteAllText(Path.Combine(inboxPath, fileName), fileName);
        }

        try
        {
            var candidates = new InboxPackageDetector().Detect(inboxPath);

            Assert.Equal(fileNames, candidates.Select(candidate => candidate.FileName));
        }
        finally
        {
            DeleteTemporaryDirectory(inboxPath);
        }
    }

    [Fact]
    public void Detect_ZipExtension_IsCaseInsensitive()
    {
        var inboxPath = CreateTemporaryDirectory();
        var fileNames = new[] { "asset.zip", "package.ZIP", "bundle.Zip" };
        foreach (var fileName in fileNames)
        {
            File.WriteAllText(Path.Combine(inboxPath, fileName), fileName);
        }

        try
        {
            var candidates = new InboxPackageDetector().Detect(inboxPath);

            var expectedNames = fileNames
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(name => name, StringComparer.Ordinal);

            Assert.Equal(expectedNames, candidates.Select(candidate => candidate.FileName));
        }
        finally
        {
            DeleteTemporaryDirectory(inboxPath);
        }
    }

    [Fact]
    public void Detect_IgnoresNonZipFiles()
    {
        var inboxPath = CreateTemporaryDirectory();
        File.WriteAllText(Path.Combine(inboxPath, "notes.txt"), "ignored");
        File.WriteAllText(Path.Combine(inboxPath, "asset.png"), "ignored");
        File.WriteAllText(Path.Combine(inboxPath, "asset.zip"), "candidate");

        try
        {
            var candidates = new InboxPackageDetector().Detect(inboxPath);

            var candidate = Assert.Single(candidates);
            Assert.Equal("asset.zip", candidate.FileName);
        }
        finally
        {
            DeleteTemporaryDirectory(inboxPath);
        }
    }

    [Fact]
    public void Detect_DoesNotInspectSubdirectories()
    {
        var inboxPath = CreateTemporaryDirectory();
        var nestedPath = Directory.CreateDirectory(Path.Combine(inboxPath, "nested")).FullName;
        File.WriteAllText(Path.Combine(nestedPath, "nested.zip"), "ignored");

        try
        {
            var candidates = new InboxPackageDetector().Detect(inboxPath);

            Assert.Empty(candidates);
        }
        finally
        {
            DeleteTemporaryDirectory(inboxPath);
        }
    }

    [Fact]
    public void Detect_ReturnsCandidatesInDeterministicNameOrder()
    {
        var inboxPath = CreateTemporaryDirectory();
        foreach (var fileName in new[] { "zeta.zip", "Alpha.zip", "beta.zip" })
        {
            File.WriteAllText(Path.Combine(inboxPath, fileName), fileName);
        }

        try
        {
            var candidates = new InboxPackageDetector().Detect(inboxPath);

            Assert.Equal(new[] { "Alpha.zip", "beta.zip", "zeta.zip" }, candidates.Select(candidate => candidate.FileName));
        }
        finally
        {
            DeleteTemporaryDirectory(inboxPath);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Detect_RejectsEmptyOrWhitespacePath(string inboxPath)
    {
        var exception = Assert.Throws<ArgumentException>(() => new InboxPackageDetector().Detect(inboxPath));

        Assert.Contains("path", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Detect_RejectsNullPath()
    {
        Assert.Throws<ArgumentNullException>(() => new InboxPackageDetector().Detect(null!));
    }

    [Fact]
    public void Detect_RejectsMissingDirectory()
    {
        var temporaryRoot = CreateTemporaryDirectory();
        var missingInboxPath = Path.Combine(temporaryRoot, "missing-inbox");

        try
        {
            var exception = Assert.Throws<DirectoryNotFoundException>(() => new InboxPackageDetector().Detect(missingInboxPath));

            Assert.Contains("Inbox directory does not exist", exception.Message);
            Assert.False(Directory.Exists(missingInboxPath));
        }
        finally
        {
            DeleteTemporaryDirectory(temporaryRoot);
        }
    }

    [Fact]
    public void Detect_DoesNotModifyOrDeleteExistingFiles()
    {
        var inboxPath = CreateTemporaryDirectory();
        var zipPath = Path.Combine(inboxPath, "asset.zip");
        var textPath = Path.Combine(inboxPath, "notes.txt");
        File.WriteAllText(zipPath, "zip candidate contents");
        File.WriteAllText(textPath, "notes contents");

        try
        {
            _ = new InboxPackageDetector().Detect(inboxPath);

            Assert.True(File.Exists(zipPath));
            Assert.True(File.Exists(textPath));
            Assert.Equal("zip candidate contents", File.ReadAllText(zipPath));
            Assert.Equal("notes contents", File.ReadAllText(textPath));
        }
        finally
        {
            DeleteTemporaryDirectory(inboxPath);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        return Directory.CreateTempSubdirectory("nap-detector-tests-").FullName;
    }

    private static void DeleteTemporaryDirectory(string path)
    {
        Directory.Delete(path, recursive: true);
    }
}

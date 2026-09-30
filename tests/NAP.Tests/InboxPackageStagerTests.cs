using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class InboxPackageStagerTests
{
    [Fact]
    public async Task StageAsync_ReadyPackage_CopiesToNewStagingDirectory()
    {
        var temporaryRoot = CreateTemporaryDirectory();
        var inboxPath = Directory.CreateDirectory(Path.Combine(temporaryRoot, "inbox")).FullName;
        var stagingPath = Path.Combine(temporaryRoot, "staging");
        var sourcePath = CreateFile(inboxPath, "asset.zip", "original package contents");

        try
        {
            var result = await CreateStager().StageAsync(CreateCandidate(sourcePath), stagingPath);

            Assert.Equal(InboxPackageStagingStatus.Staged, result.Status);
            Assert.True(result.IsStaged);
            Assert.True(Directory.Exists(stagingPath));
            Assert.Equal(Path.Combine(Path.GetFullPath(stagingPath), "asset.zip"), result.FinalStagedPath);
            Assert.Equal("asset.zip", Path.GetFileName(result.FinalStagedPath));
            Assert.True(File.Exists(sourcePath));
            Assert.Equal("original package contents", File.ReadAllText(sourcePath));
            Assert.Equal("original package contents", File.ReadAllText(result.FinalStagedPath));
            Assert.Equal(new FileInfo(sourcePath).Length, new FileInfo(result.FinalStagedPath).Length);
            AssertNoPartialFiles(stagingPath);
        }
        finally
        {
            DeleteTemporaryDirectory(temporaryRoot);
        }
    }

    [Fact]
    public async Task StageAsync_ExistingDestination_ReturnsCollisionWithoutOverwrite()
    {
        var temporaryRoot = CreateTemporaryDirectory();
        var inboxPath = Directory.CreateDirectory(Path.Combine(temporaryRoot, "inbox")).FullName;
        var stagingPath = Directory.CreateDirectory(Path.Combine(temporaryRoot, "staging")).FullName;
        var sourcePath = CreateFile(inboxPath, "asset.zip", "source contents");
        var existingStagedPath = CreateFile(stagingPath, "asset.zip", "existing staged contents");

        try
        {
            var result = await CreateStager().StageAsync(CreateCandidate(sourcePath), stagingPath);

            Assert.Equal(InboxPackageStagingStatus.Collision, result.Status);
            Assert.False(result.IsStaged);
            Assert.Equal("existing staged contents", File.ReadAllText(existingStagedPath));
            Assert.Equal("source contents", File.ReadAllText(sourcePath));
            AssertNoPartialFiles(stagingPath);
        }
        finally
        {
            DeleteTemporaryDirectory(temporaryRoot);
        }
    }

    [Fact]
    public async Task StageAsync_MissingCandidate_ReturnsNotReadyWithoutFinalFile()
    {
        var temporaryRoot = CreateTemporaryDirectory();
        var stagingPath = Path.Combine(temporaryRoot, "staging");
        var missingSourcePath = Path.Combine(temporaryRoot, "inbox", "missing.zip");

        try
        {
            var result = await CreateStager().StageAsync(CreateCandidate(missingSourcePath), stagingPath);

            Assert.Equal(InboxPackageStagingStatus.NotReady, result.Status);
            Assert.Equal(InboxPackageReadinessStatus.Missing, result.ReadinessStatus);
            Assert.False(File.Exists(result.FinalStagedPath));
        }
        finally
        {
            DeleteTemporaryDirectory(temporaryRoot);
        }
    }

    [Fact]
    public async Task StageAsync_InUseCandidate_ReturnsNotReadyWithoutCopy()
    {
        var temporaryRoot = CreateTemporaryDirectory();
        var inboxPath = Directory.CreateDirectory(Path.Combine(temporaryRoot, "inbox")).FullName;
        var stagingPath = Path.Combine(temporaryRoot, "staging");
        var sourcePath = CreateFile(inboxPath, "asset.zip", "source contents");

        try
        {
            using var lockStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.None);

            var result = await CreateStager().StageAsync(CreateCandidate(sourcePath), stagingPath);

            Assert.Equal(InboxPackageStagingStatus.NotReady, result.Status);
            Assert.Equal(InboxPackageReadinessStatus.InUse, result.ReadinessStatus);
            Assert.False(File.Exists(result.FinalStagedPath));
        }
        finally
        {
            DeleteTemporaryDirectory(temporaryRoot);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task StageAsync_RejectsEmptyOrWhitespaceStagingRoot(string stagingRoot)
    {
        var temporaryRoot = CreateTemporaryDirectory();
        var sourcePath = CreateFile(temporaryRoot, "asset.zip", "source contents");

        try
        {
            await Assert.ThrowsAsync<ArgumentException>(
                () => CreateStager().StageAsync(CreateCandidate(sourcePath), stagingRoot));
        }
        finally
        {
            DeleteTemporaryDirectory(temporaryRoot);
        }
    }

    [Fact]
    public async Task StageAsync_RejectsNullStagingRoot()
    {
        var temporaryRoot = CreateTemporaryDirectory();
        var sourcePath = CreateFile(temporaryRoot, "asset.zip", "source contents");

        try
        {
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => CreateStager().StageAsync(CreateCandidate(sourcePath), null!));
        }
        finally
        {
            DeleteTemporaryDirectory(temporaryRoot);
        }
    }

    [Fact]
    public async Task StageAsync_UsesSourceFileNameInsteadOfCandidateFileName()
    {
        var temporaryRoot = CreateTemporaryDirectory();
        var inboxPath = Directory.CreateDirectory(Path.Combine(temporaryRoot, "inbox")).FullName;
        var stagingPath = Directory.CreateDirectory(Path.Combine(temporaryRoot, "staging")).FullName;
        var sourcePath = CreateFile(inboxPath, "asset.zip", "source contents");
        var manipulatedCandidate = new InboxPackageCandidate("..\\outside.zip", sourcePath);
        var outsidePath = Path.Combine(temporaryRoot, "outside.zip");

        try
        {
            var result = await CreateStager().StageAsync(manipulatedCandidate, stagingPath);

            Assert.Equal(InboxPackageStagingStatus.Staged, result.Status);
            Assert.Equal(Path.Combine(stagingPath, "asset.zip"), result.FinalStagedPath);
            Assert.False(File.Exists(outsidePath));
        }
        finally
        {
            DeleteTemporaryDirectory(temporaryRoot);
        }
    }

    [Fact]
    public async Task StageAsync_PreservesOtherStagingFiles()
    {
        var temporaryRoot = CreateTemporaryDirectory();
        var inboxPath = Directory.CreateDirectory(Path.Combine(temporaryRoot, "inbox")).FullName;
        var stagingPath = Directory.CreateDirectory(Path.Combine(temporaryRoot, "staging")).FullName;
        var sourcePath = CreateFile(inboxPath, "asset.zip", "source contents");
        var unrelatedPath = CreateFile(stagingPath, "keep.txt", "keep this file");

        try
        {
            var result = await CreateStager().StageAsync(CreateCandidate(sourcePath), stagingPath);

            Assert.Equal(InboxPackageStagingStatus.Staged, result.Status);
            Assert.True(File.Exists(unrelatedPath));
            Assert.Equal("keep this file", File.ReadAllText(unrelatedPath));
        }
        finally
        {
            DeleteTemporaryDirectory(temporaryRoot);
        }
    }

    [Fact]
    public async Task StageAsync_CancelledBeforeCopy_DoesNotCreateFinalOrPartialFile()
    {
        var temporaryRoot = CreateTemporaryDirectory();
        var inboxPath = Directory.CreateDirectory(Path.Combine(temporaryRoot, "inbox")).FullName;
        var stagingPath = Directory.CreateDirectory(Path.Combine(temporaryRoot, "staging")).FullName;
        var sourcePath = CreateFile(inboxPath, "asset.zip", "source contents");
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => CreateStager().StageAsync(CreateCandidate(sourcePath), stagingPath, cancellationSource.Token));

            Assert.False(File.Exists(Path.Combine(stagingPath, "asset.zip")));
            AssertNoPartialFiles(stagingPath);
        }
        finally
        {
            DeleteTemporaryDirectory(temporaryRoot);
        }
    }

    private static InboxPackageStager CreateStager()
    {
        var readinessChecker = new InboxPackageReadinessChecker(new InboxPackageReadinessOptions
        {
            RequiredSamples = 2,
            SampleInterval = TimeSpan.Zero
        });

        return new InboxPackageStager(readinessChecker);
    }

    private static InboxPackageCandidate CreateCandidate(string sourcePath)
    {
        return new InboxPackageCandidate(Path.GetFileName(sourcePath), sourcePath);
    }

    private static string CreateTemporaryDirectory()
    {
        return Directory.CreateTempSubdirectory("nap-stager-tests-").FullName;
    }

    private static string CreateFile(string directoryPath, string fileName, string contents)
    {
        var filePath = Path.Combine(directoryPath, fileName);
        File.WriteAllText(filePath, contents);
        return filePath;
    }

    private static void AssertNoPartialFiles(string directoryPath)
    {
        Assert.Empty(Directory.EnumerateFiles(directoryPath, "*.partial", SearchOption.TopDirectoryOnly));
    }

    private static void DeleteTemporaryDirectory(string path)
    {
        Directory.Delete(path, recursive: true);
    }
}

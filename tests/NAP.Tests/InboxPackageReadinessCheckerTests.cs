using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class InboxPackageReadinessCheckerTests
{
    [Fact]
    public async Task CheckAsync_StableFile_ReturnsReady()
    {
        var temporaryRoot = CreateTemporaryDirectory();
        var filePath = CreateFile(temporaryRoot, "asset.zip", "stable contents");

        try
        {
            var result = await CreateChecker(TimeSpan.Zero).CheckAsync(CreateCandidate(filePath));

            Assert.Equal(InboxPackageReadinessStatus.Ready, result.Status);
            Assert.True(result.IsReady);
        }
        finally
        {
            DeleteTemporaryDirectory(temporaryRoot);
        }
    }

    [Fact]
    public async Task CheckAsync_MissingFile_ReturnsMissing()
    {
        var temporaryRoot = CreateTemporaryDirectory();
        var filePath = Path.Combine(temporaryRoot, "missing.zip");

        try
        {
            var result = await CreateChecker(TimeSpan.Zero).CheckAsync(CreateCandidate(filePath));

            Assert.Equal(InboxPackageReadinessStatus.Missing, result.Status);
            Assert.False(result.IsReady);
        }
        finally
        {
            DeleteTemporaryDirectory(temporaryRoot);
        }
    }

    [Fact]
    public async Task CheckAsync_FileSizeChanges_ReturnsChanging()
    {
        var temporaryRoot = CreateTemporaryDirectory();
        var filePath = CreateFile(temporaryRoot, "asset.zip", "initial");

        try
        {
            var changeTask = AppendAfterDelayAsync(filePath, TimeSpan.FromMilliseconds(50));

            var result = await CreateChecker(TimeSpan.FromMilliseconds(200)).CheckAsync(CreateCandidate(filePath));
            await changeTask;

            Assert.Equal(InboxPackageReadinessStatus.Changing, result.Status);
        }
        finally
        {
            DeleteTemporaryDirectory(temporaryRoot);
        }
    }

    [Fact]
    public async Task CheckAsync_LastWriteTimeChanges_ReturnsChanging()
    {
        var temporaryRoot = CreateTemporaryDirectory();
        var filePath = CreateFile(temporaryRoot, "asset.zip", "stable contents");
        var originalLastWriteTime = File.GetLastWriteTimeUtc(filePath);

        try
        {
            var changeTask = SetLastWriteTimeAfterDelayAsync(
                filePath,
                originalLastWriteTime.AddMinutes(1),
                TimeSpan.FromMilliseconds(50));

            var result = await CreateChecker(TimeSpan.FromMilliseconds(200)).CheckAsync(CreateCandidate(filePath));
            await changeTask;

            Assert.Equal(InboxPackageReadinessStatus.Changing, result.Status);
        }
        finally
        {
            DeleteTemporaryDirectory(temporaryRoot);
        }
    }

    [Fact]
    public async Task CheckAsync_ExclusivelyOpenedFile_ReturnsInUse()
    {
        var temporaryRoot = CreateTemporaryDirectory();
        var filePath = CreateFile(temporaryRoot, "asset.zip", "stable contents");

        try
        {
            using var lockStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None);

            var result = await CreateChecker(TimeSpan.Zero).CheckAsync(CreateCandidate(filePath));

            Assert.Equal(InboxPackageReadinessStatus.InUse, result.Status);
        }
        finally
        {
            DeleteTemporaryDirectory(temporaryRoot);
        }
    }

    [Fact]
    public async Task CheckAsync_DoesNotModifyMoveOrDeleteFile()
    {
        var temporaryRoot = CreateTemporaryDirectory();
        var filePath = CreateFile(temporaryRoot, "asset.zip", "original contents");

        try
        {
            _ = await CreateChecker(TimeSpan.Zero).CheckAsync(CreateCandidate(filePath));

            Assert.True(File.Exists(filePath));
            Assert.Equal("original contents", File.ReadAllText(filePath));
            Assert.Single(Directory.EnumerateFiles(temporaryRoot));
        }
        finally
        {
            DeleteTemporaryDirectory(temporaryRoot);
        }
    }

    [Fact]
    public void Constructor_RejectsFewerThanTwoSamples()
    {
        var options = new InboxPackageReadinessOptions
        {
            RequiredSamples = 1,
            SampleInterval = TimeSpan.Zero
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => new InboxPackageReadinessChecker(options));
    }

    [Fact]
    public void Constructor_RejectsNegativeSampleInterval()
    {
        var options = new InboxPackageReadinessOptions
        {
            RequiredSamples = 2,
            SampleInterval = TimeSpan.FromMilliseconds(-1)
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => new InboxPackageReadinessChecker(options));
    }

    [Fact]
    public async Task CheckAsync_CancellationIsObserved()
    {
        var temporaryRoot = CreateTemporaryDirectory();
        var filePath = CreateFile(temporaryRoot, "asset.zip", "stable contents");
        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        try
        {
            var checker = CreateChecker(TimeSpan.FromSeconds(10));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => checker.CheckAsync(CreateCandidate(filePath), cancellationSource.Token));
        }
        finally
        {
            DeleteTemporaryDirectory(temporaryRoot);
        }
    }

    [Fact]
    public async Task CheckAsync_RejectsNullCandidate()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => CreateChecker(TimeSpan.Zero).CheckAsync(null!));
    }

    private static InboxPackageReadinessChecker CreateChecker(TimeSpan sampleInterval)
    {
        return new InboxPackageReadinessChecker(new InboxPackageReadinessOptions
        {
            RequiredSamples = 2,
            SampleInterval = sampleInterval
        });
    }

    private static InboxPackageCandidate CreateCandidate(string fullPath)
    {
        return new InboxPackageCandidate(Path.GetFileName(fullPath), fullPath);
    }

    private static string CreateTemporaryDirectory()
    {
        return Directory.CreateTempSubdirectory("nap-readiness-tests-").FullName;
    }

    private static string CreateFile(string directoryPath, string fileName, string contents)
    {
        var filePath = Path.Combine(directoryPath, fileName);
        File.WriteAllText(filePath, contents);
        return filePath;
    }

    private static async Task AppendAfterDelayAsync(string filePath, TimeSpan delay)
    {
        await Task.Delay(delay);
        await File.AppendAllTextAsync(filePath, " more");
    }

    private static async Task SetLastWriteTimeAfterDelayAsync(string filePath, DateTime value, TimeSpan delay)
    {
        await Task.Delay(delay);
        File.SetLastWriteTimeUtc(filePath, value);
    }

    private static void DeleteTemporaryDirectory(string path)
    {
        Directory.Delete(path, recursive: true);
    }
}

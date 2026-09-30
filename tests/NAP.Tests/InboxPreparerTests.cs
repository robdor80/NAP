using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class InboxPreparerTests
{
    [Fact]
    public void Prepare_CreatesAndReturnsNormalizedPath()
    {
        var temporaryRoot = CreateTemporaryDirectory();
        var inboxPath = Path.Combine(temporaryRoot, "inbox", ".", "incoming");

        try
        {
            var preparedPath = new InboxPreparer().Prepare(inboxPath);

            Assert.Equal(Path.GetFullPath(inboxPath), preparedPath);
            Assert.True(Directory.Exists(preparedPath));
        }
        finally
        {
            DeleteTemporaryDirectory(temporaryRoot);
        }
    }

    [Fact]
    public void Prepare_PreservesExistingDirectory()
    {
        var temporaryRoot = CreateTemporaryDirectory();
        var inboxPath = Path.Combine(temporaryRoot, "inbox");
        Directory.CreateDirectory(inboxPath);
        var existingFile = Path.Combine(inboxPath, "existing.txt");
        File.WriteAllText(existingFile, "existing content");

        try
        {
            var preparedPath = new InboxPreparer().Prepare(inboxPath);

            Assert.Equal(Path.GetFullPath(inboxPath), preparedPath);
            Assert.True(Directory.Exists(preparedPath));
            Assert.Equal("existing content", File.ReadAllText(existingFile));
        }
        finally
        {
            DeleteTemporaryDirectory(temporaryRoot);
        }
    }

    [Fact]
    public void Prepare_IsIdempotent()
    {
        var temporaryRoot = CreateTemporaryDirectory();
        var inboxPath = Path.Combine(temporaryRoot, "inbox");

        try
        {
            var preparer = new InboxPreparer();

            var firstPath = preparer.Prepare(inboxPath);
            var secondPath = preparer.Prepare(inboxPath);

            Assert.Equal(firstPath, secondPath);
            Assert.True(Directory.Exists(secondPath));
        }
        finally
        {
            DeleteTemporaryDirectory(temporaryRoot);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Prepare_RejectsEmptyOrWhitespacePath(string inboxPath)
    {
        var exception = Assert.Throws<ArgumentException>(() => new InboxPreparer().Prepare(inboxPath));

        Assert.Contains("path", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Prepare_RejectsNullPath()
    {
        Assert.Throws<ArgumentNullException>(() => new InboxPreparer().Prepare(null!));
    }

    private static string CreateTemporaryDirectory()
    {
        return Directory.CreateTempSubdirectory("nap-inbox-tests-").FullName;
    }

    private static void DeleteTemporaryDirectory(string path)
    {
        Directory.Delete(path, recursive: true);
    }
}

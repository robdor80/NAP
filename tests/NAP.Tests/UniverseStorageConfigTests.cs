using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class UniverseStorageConfigTests
{
    [Fact]
    public void AbsoluteRoots_DeriveOperationalPathsWithoutCreatingDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), "nap-universe-" + Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(root, "workspace");
        var production = Path.Combine(root, "production");
        var archive = Path.Combine(root, "archive");
        var id = new UniverseId("future_universe");
        Assert.False(Directory.Exists(root));

        var config = new UniverseStorageConfig(id, workspace, production, archive);

        Assert.Same(id, config.UniverseId);
        Assert.Equal(workspace, config.WorkspaceRoot);
        Assert.Equal(production, config.ProductionRoot);
        Assert.Equal(archive, config.ArchiveRoot);
        Assert.Equal(Path.Combine(workspace, "inbox"), config.InboxRoot);
        Assert.Equal(Path.Combine(workspace, "staging"), config.StagingRoot);
        Assert.Equal(Path.Combine(workspace, "state"), config.StateRoot);
        Assert.Equal(Path.Combine(workspace, "cache"), config.CacheRoot);
        Assert.Equal(Path.Combine(workspace, "state", "AssetCatalog.db"), config.CatalogPath);
        Assert.False(Directory.Exists(root));
        Assert.False(File.Exists(config.CatalogPath));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("workspace")]
    [InlineData("./workspace")]
    [InlineData("../workspace")]
    [InlineData("C:workspace")]
    public void EveryRoot_RejectsMissingOrRelativePath(string? invalidRoot)
    {
        var id = new UniverseId("nimroel");
        var absolute = Path.Combine(Path.GetTempPath(), "nap-universe-tests");
        Assert.ThrowsAny<ArgumentException>(() => new UniverseStorageConfig(id, invalidRoot!, absolute, absolute));
        Assert.ThrowsAny<ArgumentException>(() => new UniverseStorageConfig(id, absolute, invalidRoot!, absolute));
        Assert.ThrowsAny<ArgumentException>(() => new UniverseStorageConfig(id, absolute, absolute, invalidRoot!));
    }

    [Fact]
    public void WindowsRootedButNotFullyQualifiedPath_IsRejected()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var absolute = Path.Combine(Path.GetTempPath(), "nap-universe-tests");
        Assert.Throws<ArgumentException>(() => new UniverseStorageConfig(new UniverseId("nimroel"),
            "\\workspace", absolute, absolute));
    }

    [Fact]
    public void FullyQualifiedRoots_AreLexicallyNormalized()
    {
        var basePath = Path.Combine(Path.GetTempPath(), "nap-universe-tests");
        var input = Path.Combine(basePath, "unused", "..", "workspace");
        var config = new UniverseStorageConfig(new UniverseId("nimroel"), input, input, input);
        Assert.Equal(Path.GetFullPath(input), config.WorkspaceRoot);
        Assert.Equal(Path.GetFullPath(input), config.ProductionRoot);
        Assert.Equal(Path.GetFullPath(input), config.ArchiveRoot);
    }

    [Fact]
    public void NullUniverse_IsRejected()
    {
        var absolute = Path.GetTempPath();
        Assert.Throws<ArgumentNullException>(() => new UniverseStorageConfig(null!, absolute, absolute, absolute));
    }
}

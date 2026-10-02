using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class UniverseContextTests
{
    [Fact]
    public void EqualUniverseValues_CreateExplicitContext()
    {
        var profile = new UniverseProfile(new UniverseId("nimroel"), "Nimroel");
        var storage = CreateStorage(new UniverseId("nimroel"));
        var context = new UniverseContext(profile, storage);
        Assert.Same(profile, context.Profile);
        Assert.Same(storage, context.Storage);
        Assert.Equal(new UniverseId("nimroel"), context.Id);
    }

    [Fact]
    public void ProfileAndStorageOfDifferentUniverses_AreRejectedImmediately()
    {
        var profile = new UniverseProfile(new UniverseId("nimroel"), "Nimroel");
        var storage = CreateStorage(new UniverseId("star_trek"));
        Assert.Throws<ArgumentException>(() => new UniverseContext(profile, storage));
    }

    [Fact]
    public void NullProfileOrStorage_IsRejected()
    {
        var id = new UniverseId("nimroel");
        Assert.Throws<ArgumentNullException>(() => new UniverseContext(null!, CreateStorage(id)));
        Assert.Throws<ArgumentNullException>(() => new UniverseContext(new UniverseProfile(id, "Nimroel"), null!));
    }

    [Fact]
    public void IndependentContexts_KeepTheirOwnIdentityAndRoots()
    {
        var firstId = new UniverseId("nimroel");
        var secondId = new UniverseId("star_trek");
        var first = new UniverseContext(new UniverseProfile(firstId, "Nimroel"), CreateStorage(firstId));
        var second = new UniverseContext(new UniverseProfile(secondId, "Star Trek"), CreateStorage(secondId));
        Assert.Equal(firstId, first.Id);
        Assert.Equal(secondId, second.Id);
        Assert.NotEqual(first.Storage.WorkspaceRoot, second.Storage.WorkspaceRoot);
        Assert.NotEqual(first.Storage.CatalogPath, second.Storage.CatalogPath);
        Assert.NotEqual(first.Storage.ProductionRoot, second.Storage.ProductionRoot);
        Assert.NotEqual(first.Storage.ArchiveRoot, second.Storage.ArchiveRoot);
    }

    private static UniverseStorageConfig CreateStorage(UniverseId id)
    {
        var root = Path.Combine(Path.GetTempPath(), "nap-context-tests", id.Value);
        return new UniverseStorageConfig(id, Path.Combine(root, "workspace"),
            Path.Combine(root, "production"), Path.Combine(root, "archive"));
    }
}

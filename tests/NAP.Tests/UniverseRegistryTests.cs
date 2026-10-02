using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class UniverseRegistryTests
{
    [Fact]
    public void Registry_PreservesOrderAndTakesReadOnlySnapshot()
    {
        var first = new UniverseProfile(new UniverseId("nimroel"), "Nimroel");
        var second = new UniverseProfile(new UniverseId("star_trek"), "Star Trek");
        UniverseProfile[] source = [second, first];
        var registry = new UniverseRegistry(source);
        source[0] = first;

        Assert.Equal(new[] { second, first }, registry.Profiles);
        var exposed = Assert.IsAssignableFrom<IList<UniverseProfile>>(registry.Profiles);
        Assert.True(exposed.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => exposed[0] = first);
        Assert.Throws<NotSupportedException>(() => exposed.Add(first));
        Assert.Throws<NotSupportedException>(() => exposed.Clear());
        Assert.True(registry.TryGet(new UniverseId("star_trek"), out var found));
        Assert.Same(second, found);
        Assert.Equal(new[] { second, first }, registry.Profiles);
    }

    [Fact]
    public void Lookup_UsesUniverseValueAndReturnsNullForUnknownId()
    {
        var profile = new UniverseProfile(new UniverseId("nimroel"), "Human name with spaces");
        var registry = new UniverseRegistry([profile]);
        Assert.True(registry.TryGet(new UniverseId("nimroel"), out var found));
        Assert.Same(profile, found);
        Assert.False(registry.TryGet(new UniverseId("future_universe"), out var missing));
        Assert.Null(missing);
    }

    [Fact]
    public void DuplicateIds_AreRejectedEvenWithDifferentDisplayNames()
    {
        var first = new UniverseProfile(new UniverseId("nimroel"), "First");
        var second = new UniverseProfile(new UniverseId("nimroel"), "Second");
        Assert.Throws<ArgumentException>(() => new UniverseRegistry([first, second]));
    }

    [Fact]
    public void NullInputProfileOrLookupId_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new UniverseRegistry(null!));
        Assert.Throws<ArgumentException>(() => new UniverseRegistry([null!]));
        Assert.Throws<ArgumentNullException>(() => new UniverseRegistry([]).TryGet(null!, out _));
    }

    [Fact]
    public void EmptyRegistry_IsValid()
    {
        var registry = new UniverseRegistry([]);
        Assert.Empty(registry.Profiles);
        Assert.False(registry.TryGet(new UniverseId("nimroel"), out var profile));
        Assert.Null(profile);
    }
}

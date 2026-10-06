using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class AssetContentFingerprintTests
{
    [Fact]
    public void PublicContractIsASealedRecordWithExactConstructorAndOnlyTwoGetOnlyProperties()
    {
        var type = typeof(AssetContentFingerprint);
        Assert.True(type.IsPublic);
        Assert.True(type.IsSealed);
        Assert.NotNull(type.GetMethod("<Clone>$"));
        var parameters = Assert.Single(type.GetConstructors()).GetParameters();
        Assert.Equal(new[] { typeof(UniverseAssetKey), typeof(Sha256Digest) }, parameters.Select(p => p.ParameterType));
        Assert.Equal(new[] { "assetKey", "digest" }, parameters.Select(p => p.Name));
        var properties = type.GetProperties().OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "AssetKey", "Digest" }, properties.Select(p => p.Name));
        Assert.Equal(new[] { typeof(UniverseAssetKey), typeof(Sha256Digest) }, properties.Select(p => p.PropertyType));
        Assert.All(properties, p => Assert.Null(p.SetMethod));
    }

    [Fact]
    public void ImmutableInputsAreRetainedWithoutCopies()
    {
        var key = new UniverseAssetKey(new UniverseId("nimroel"), "portrait_example_001");
        var digest = new Sha256Digest(new string('a', 64));
        var fingerprint = new AssetContentFingerprint(key, digest);
        Assert.Same(key, fingerprint.AssetKey);
        Assert.Same(digest, fingerprint.Digest);
    }

    [Theory]
    [InlineData(true, true, "assetKey")]
    [InlineData(true, false, "assetKey")]
    [InlineData(false, true, "digest")]
    public void NullArgumentsHaveExactParametersAndAssetKeyIsValidatedFirst(bool nullKey, bool nullDigest, string parameter)
    {
        var key = new UniverseAssetKey(new UniverseId("nimroel"), "portrait_example_001");
        var digest = new Sha256Digest(new string('a', 64));
        Assert.Equal(parameter, Assert.Throws<ArgumentNullException>(() =>
            new AssetContentFingerprint(nullKey ? null! : key, nullDigest ? null! : digest)).ParamName);
    }

    [Fact]
    public void IndependentEquivalentInputsGiveValueEqualityOperatorsAndCoherentHashCodes()
    {
        var first = Fingerprint("nimroel", "portrait_example_001", 'a');
        var same = Fingerprint("nimroel", "portrait_example_001", 'a');
        Assert.NotSame(first.AssetKey, same.AssetKey);
        Assert.NotSame(first.Digest, same.Digest);
        Assert.True(first.Equals(same));
        Assert.True(first.Equals((object)same));
        Assert.True(first == same);
        Assert.False(first != same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.Single(new HashSet<AssetContentFingerprint> { first, same });
        Assert.False(first.Equals(null));
    }

    [Theory]
    [InlineData("nimroel", "portrait_other_002", 'a')]
    [InlineData("nimroel", "portrait_example_001", 'b')]
    [InlineData("test_universe", "portrait_example_001", 'a')]
    public void EachIdentityOrDigestDifferenceChangesValueEquality(string universe, string assetId, char hash)
    {
        var first = Fingerprint("nimroel", "portrait_example_001", 'a');
        var different = Fingerprint(universe, assetId, hash);
        Assert.False(first.Equals(different));
        Assert.False(first == different);
        Assert.True(first != different);
    }

    [Fact]
    public void RelationEnumHasExactlyFourPublicValuesInContractOrder()
    {
        var type = typeof(AssetContentRelation);
        Assert.True(type.IsPublic);
        Assert.True(type.IsEnum);
        Assert.Equal(new[] { "Distinct", "SameAssetSameContent", "SameAssetDifferentContent", "DifferentAssetSameContent" },
            Enum.GetNames<AssetContentRelation>());
        Assert.Equal(new[] { 0, 1, 2, 3 }, Enum.GetValues<AssetContentRelation>().Select(value => (int)value));
    }

    private static AssetContentFingerprint Fingerprint(string universe, string assetId, char hash) =>
        new(new UniverseAssetKey(new UniverseId(universe), assetId), new Sha256Digest(new string(hash, 64)));
}

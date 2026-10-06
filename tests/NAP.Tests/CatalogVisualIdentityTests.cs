using System.Text;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class CatalogVisualIdentityTests
{
    [Fact]
    public void ScalarsNestedObjectsArraysAndEscapedPathsAreExactAndDeterministic()
    {
        var source = Encoding.UTF8.GetBytes("{\"Eye\":\"green-grey\",\"number\":1.00e+2,\"bool\":true,\"null\":null,\"obj\":{\"hair\":\"Brown\"},\"a/b\":1,\"a\":{\"b\":\"1\"},\"~\":false,\"\":\"empty key\",\"array\":[\"x\",null,{},[],{\"v\":2},[false]]}");
        var original = (byte[])source.Clone(); var traits = CatalogVisualIdentity.Extract(source);
        Assert.Equal(11, traits.Count); Assert.Equal(traits.OrderBy(t => t.Key, StringComparer.Ordinal), traits);
        Assert.Contains(new CatalogVisualTrait("/Eye", "green-grey", CatalogTraitType.String), traits);
        Assert.Contains(new CatalogVisualTrait("/number", "1.00e+2", CatalogTraitType.Number), traits);
        Assert.Contains(new CatalogVisualTrait("/bool", "true", CatalogTraitType.Boolean), traits);
        Assert.Contains(new CatalogVisualTrait("/obj/hair", "Brown", CatalogTraitType.String), traits);
        Assert.Contains(new CatalogVisualTrait("/a~1b", "1", CatalogTraitType.Number), traits);
        Assert.Contains(new CatalogVisualTrait("/a/b", "1", CatalogTraitType.String), traits);
        Assert.Contains(new CatalogVisualTrait("/~0", "false", CatalogTraitType.Boolean), traits);
        Assert.Contains(new CatalogVisualTrait("/", "empty key", CatalogTraitType.String), traits);
        Assert.Contains(new CatalogVisualTrait("/array/4/v", "2", CatalogTraitType.Number), traits);
        Assert.Contains(new CatalogVisualTrait("/array/5/0", "false", CatalogTraitType.Boolean), traits);
        Assert.DoesNotContain(traits, t => t.Key == "/null" || t.Key == "/array/1"); Assert.Equal(original, source);
        Assert.Equal(traits, CatalogVisualIdentity.Extract(source));
    }
    [Theory]
    [InlineData("{\"a\":1,\"a\":2}")] [InlineData("{\"x\":{\"a\":1,\"a\":2}}")]
    [InlineData("{\"a\":1,\"\\u0061\":2}")] [InlineData("{")] [InlineData("[]")] [InlineData("null")]
    public void AmbiguousOrInvalidJsonNeverInventsTraits(string json)
    { CatalogTestFixture.Stop(Assert.Throws<CatalogException>(() => CatalogVisualIdentity.Extract(Encoding.UTF8.GetBytes(json))), NapIssueCodes.CatalogSourceInvalid); }
    [Theory]
    [InlineData("{}")] [InlineData("{\"a\":null,\"b\":[],\"c\":{}}")]
    public void NullsAndEmptyContainersHaveNoScalarTraits(string json) => Assert.Empty(CatalogVisualIdentity.Extract(Encoding.UTF8.GetBytes(json)));
    [Fact]
    public void CasingAndUnicodeValuesRemainDistinctWithoutSynonyms()
    {
        var traits = CatalogVisualIdentity.Extract(Encoding.UTF8.GetBytes("{\"A\":\"Green-grey\",\"a\":\"green-grey\",\"é\":\"é\"}"));
        Assert.Equal(3, traits.Count); Assert.NotEqual(traits.Single(t => t.Key == "/A").Value, traits.Single(t => t.Key == "/a").Value);
        Assert.Equal("é", traits.Single(t => t.Key == "/é").Value);
    }
}

using System.Globalization;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class Sha256DigestTests
{
    [Fact]
    public void PublicContractIsAnImmutableSealedRecordWithOnlyHexAndExactConstructor()
    {
        var type = typeof(Sha256Digest);
        Assert.True(type.IsSealed);
        Assert.NotNull(type.GetMethod("<Clone>$"));
        var parameter = Assert.Single(Assert.Single(type.GetConstructors()).GetParameters());
        Assert.Equal(typeof(string), parameter.ParameterType);
        Assert.Equal("hex", parameter.Name);
        var property = Assert.Single(type.GetProperties());
        Assert.Equal("Hex", property.Name);
        Assert.Equal(typeof(string), property.PropertyType);
        Assert.Null(property.SetMethod);
        Assert.Equal(type, type.GetMethod("ToString", Type.EmptyTypes)!.DeclaringType);
    }

    [Theory]
    [InlineData("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")]
    public void CanonicalLowercaseAsciiHexIsPreservedExactly(string hex)
    {
        var digest = new Sha256Digest(hex);
        Assert.Same(hex, digest.Hex);
        Assert.Equal(hex, digest.ToString());
    }

    [Fact]
    public void NullUsesArgumentNullExceptionAndExactParameter() =>
        Assert.Equal("hex", Assert.Throws<ArgumentNullException>(() => new Sha256Digest(null!)).ParamName);

    public static IEnumerable<object[]> InvalidRepresentations()
    {
        var valid = new string('a', 64);
        foreach (var hex in new[]
        {
            "", " ", "\t\r\n", new string('a', 63), new string('a', 65), new string('A', 64),
            "A" + valid[1..], "g" + valid[1..], "z" + valid[1..], "é" + valid[1..],
            "漢" + valid[1..], "０" + valid[1..], "0x" + valid, "0x" + valid[2..],
            " " + valid, valid + " ", valid + "\n", valid[..63] + "\n", valid[..63] + "\t",
            valid[..31] + "-" + valid[32..], valid[..31] + ":" + valid[32..], valid[..31] + " " + valid[32..]
        }) yield return [hex];
    }

    [Theory]
    [MemberData(nameof(InvalidRepresentations))]
    public void NoncanonicalRepresentationsAreRejectedWithoutNormalization(string hex) =>
        Assert.Equal("hex", Assert.Throws<ArgumentException>(() => new Sha256Digest(hex)).ParamName);

    [Fact]
    public void EqualityOperatorsAndHashCodeUseTheCanonicalValue()
    {
        var first = new Sha256Digest(new string('a', 64));
        var same = new Sha256Digest(new string('a', 64));
        var different = new Sha256Digest(new string('b', 64));
        Assert.NotSame(first, same);
        Assert.True(first.Equals(same));
        Assert.True(first.Equals((object)same));
        Assert.True(first == same);
        Assert.False(first != same);
        Assert.False(first.Equals(different));
        Assert.False(first == different);
        Assert.True(first != different);
        Assert.False(first.Equals(null));
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.Equal(2, new HashSet<Sha256Digest> { first, same, different }.Count);
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("ar-SA")]
    public void AsciiValidationAndValueFormattingAreCultureIndependent(string cultureName)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            var hex = new string('f', 64);
            Assert.Equal(hex, new Sha256Digest(hex).ToString());
            Assert.Throws<ArgumentException>(() => new Sha256Digest(new string('F', 64)));
            Assert.Throws<ArgumentException>(() => new Sha256Digest(new string('٠', 64)));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}

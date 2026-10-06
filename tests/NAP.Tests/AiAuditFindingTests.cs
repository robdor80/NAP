using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class AiAuditFindingTests
{
    [Fact]
    public void ExactSealedRecordContractRetainsValuesAndValueEquality()
    {
        var type = typeof(AiAuditFinding);
        Assert.True(type.IsPublic); Assert.True(type.IsSealed); Assert.NotNull(type.GetMethod("<Clone>$"));
        var parameters = Assert.Single(type.GetConstructors()).GetParameters();
        Assert.Equal(new[] { "code", "severity", "message" }, parameters.Select(p => p.Name));
        Assert.Equal(new[] { typeof(string), typeof(AiAuditFindingSeverity), typeof(string) }, parameters.Select(p => p.ParameterType));
        var properties = type.GetProperties().OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "Code", "Message", "Severity" }, properties.Select(p => p.Name));
        Assert.All(properties, p => Assert.Null(p.SetMethod));
        var first = AiAuditTestData.Finding();
        Assert.Equal(first, AiAuditTestData.Finding());
        Assert.Equal(first.GetHashCode(), AiAuditTestData.Finding().GetHashCode());
        Assert.True(first == AiAuditTestData.Finding());
        Assert.NotEqual(first, AiAuditTestData.Finding(AiAuditFindingSeverity.Error));
        Assert.Equal("destination_incoherent", first.Code);
        Assert.Equal("Review destination.", first.Message);
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData(" ")] [InlineData("Bad_code")]
    [InlineData("a__b")] [InlineData("a-b")] [InlineData("1_code")] [InlineData("code_")]
    [InlineData("../code")] [InlineData("C:\\path")] [InlineData("/home/path")] [InlineData("código")]
    public void InvalidCodeIsRejected(string? code) => Assert.Equal("code",
        Assert.Throws<ArgumentException>(() => new AiAuditFinding(code!, AiAuditFindingSeverity.Warning, "message")).ParamName);

    [Fact]
    public void NamingLengthBoundaryIsEnforced()
    {
        Assert.Equal(new string('a', 64), new AiAuditFinding(new string('a', 64), AiAuditFindingSeverity.Warning, "message").Code);
        Assert.Throws<ArgumentException>(() => new AiAuditFinding(new string('a', 65), AiAuditFindingSeverity.Warning, "message"));
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData(" \t\n")]
    public void MissingMessageIsRejected(string? message) => Assert.ThrowsAny<ArgumentException>(() => new AiAuditFinding("code", AiAuditFindingSeverity.Warning, message!));

    [Fact]
    public void MessageMaximumIsExactAndTextIsNotNormalized()
    {
        var message = " " + new string('é', 998) + " ";
        Assert.Same(message, new AiAuditFinding("code", AiAuditFindingSeverity.Error, message).Message);
        Assert.Equal("message", Assert.Throws<ArgumentException>(() => new AiAuditFinding("code", AiAuditFindingSeverity.Warning, new string('a', 1001))).ParamName);
    }

    [Theory]
    [InlineData(-1)] [InlineData(2)] [InlineData(int.MaxValue)]
    public void InvalidSeverityIsRejected(int severity) => Assert.Equal("severity",
        Assert.Throws<ArgumentOutOfRangeException>(() => new AiAuditFinding("code", (AiAuditFindingSeverity)severity, "message")).ParamName);

    [Fact]
    public void EnumVocabularyHasOnlyExplicitValues()
    {
        Assert.Equal(new[] { AiAuditDecision.Pass, AiAuditDecision.Warning, AiAuditDecision.Fail }, Enum.GetValues<AiAuditDecision>());
        Assert.Equal(new[] { 0, 1, 2 }, Enum.GetValues<AiAuditDecision>().Select(value => (int)value));
        Assert.Equal(new[] { AiAuditFindingSeverity.Warning, AiAuditFindingSeverity.Error }, Enum.GetValues<AiAuditFindingSeverity>());
        Assert.Equal(new[] { 0, 1 }, Enum.GetValues<AiAuditFindingSeverity>().Select(value => (int)value));
    }
}

using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class AiAuditIssueFactTests
{
    [Fact]
    public void ExactSealedRecordApiContainsOnlyCodeSeverityDisposition()
    {
        var type = typeof(AiAuditIssueFact);
        Assert.True(type.IsPublic); Assert.True(type.IsSealed); Assert.NotNull(type.GetMethod("<Clone>$"));
        Assert.Equal(new[] { "code", "severity", "disposition" }, Assert.Single(type.GetConstructors()).GetParameters().Select(p => p.Name));
        var properties = type.GetProperties().OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "Code", "Disposition", "Severity" }, properties.Select(p => p.Name));
        Assert.All(properties, p => Assert.Null(p.SetMethod));
        var fact = new AiAuditIssueFact("code", NapIssueSeverity.Warning, NapIssueDisposition.Continue);
        Assert.Equal(fact, new AiAuditIssueFact("code", NapIssueSeverity.Warning, NapIssueDisposition.Continue));
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData(" ")] [InlineData("Bad")]
    [InlineData("a__b")] [InlineData("../path")] [InlineData("C:\\path")]
    public void CodeRequiresNamingV1(string? code) => Assert.Throws<ArgumentException>(() => new AiAuditIssueFact(code!, NapIssueSeverity.Info, NapIssueDisposition.Continue));

    [Theory]
    [InlineData(-1, 0, "severity")] [InlineData(3, 0, "severity")]
    [InlineData(0, -1, "disposition")] [InlineData(0, 2, "disposition")]
    public void EnumsAreGuarded(int severity, int disposition, string parameter) => Assert.Equal(parameter,
        Assert.Throws<ArgumentOutOfRangeException>(() => new AiAuditIssueFact("code", (NapIssueSeverity)severity, (NapIssueDisposition)disposition)).ParamName);

    [Fact]
    public void SeverityAndDispositionRemainIndependent()
    {
        foreach (var severity in Enum.GetValues<NapIssueSeverity>())
            foreach (var disposition in Enum.GetValues<NapIssueDisposition>())
            {
                var fact = new AiAuditIssueFact("code", severity, disposition);
                Assert.Equal(severity, fact.Severity); Assert.Equal(disposition, fact.Disposition);
            }
    }
}

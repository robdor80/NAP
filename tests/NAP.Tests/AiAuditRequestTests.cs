using System.Reflection;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class AiAuditRequestTests
{
    [Fact]
    public void ExactPublicGetOnlyApiAndNoPublicConstructorPreventRawPlanProviderAccess()
    {
        var type = typeof(AiAuditRequest);
        Assert.True(type.IsPublic); Assert.True(type.IsSealed); Assert.Empty(type.GetConstructors());
        var properties = type.GetProperties().OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "AssetKey", "AssetType", "Classification", "DestinationRelativeDirectory", "InputRoles", "ProductionProfile", "ValidationIssues" }, properties.Select(p => p.Name));
        Assert.Equal(new[] { typeof(UniverseAssetKey), typeof(string), typeof(IReadOnlyDictionary<string, string>), typeof(string),
            typeof(IReadOnlyList<string>), typeof(string), typeof(IReadOnlyList<AiAuditIssueFact>) }, properties.Select(p => p.PropertyType));
        Assert.All(properties, p => Assert.Null(p.SetMethod));
        var method = Assert.Single(typeof(IAiAuditClient).GetMethods());
        Assert.True(typeof(IAiAuditClient).IsInterface);
        Assert.Equal("AuditAsync", method.Name);
        Assert.Equal(typeof(Task<AiAuditReport>), method.ReturnType);
        Assert.Equal(new[] { typeof(AiAuditRequest), typeof(CancellationToken) }, method.GetParameters().Select(p => p.ParameterType));
        Assert.True(method.GetParameters()[1].IsOptional);
    }

    [Fact]
    public void ClassificationRolesAndIssuesAreDefensiveOrdinalReadOnlySnapshots()
    {
        var classification = new Dictionary<string, string> { ["culture"] = "example", ["Culture"] = "other" };
        var roles = new[] { "prompt", "png_master" };
        var issues = new List<AiAuditIssueFact> { new("z_issue", NapIssueSeverity.Warning, NapIssueDisposition.Continue) };
        var key = new UniverseAssetKey(new UniverseId("test_universe"), AiAuditTestData.AssetId);
        var request = AiAuditTestData.Construct<AiAuditRequest>(key, "portrait", "portrait_npc", classification, roles, AiAuditTestData.Relative, issues);
        classification.Clear(); roles[0] = "changed"; issues.Clear();
        Assert.Same(key, request.AssetKey);
        Assert.Equal("example", request.Classification["culture"]);
        Assert.Equal("other", request.Classification["Culture"]);
        Assert.False(request.Classification.ContainsKey("CULTURE"));
        Assert.Equal(new[] { "png_master", "prompt" }, request.InputRoles);
        Assert.Equal("z_issue", Assert.Single(request.ValidationIssues).Code);
        var dictionary = Assert.IsAssignableFrom<IDictionary<string, string>>(request.Classification);
        Assert.True(dictionary.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => dictionary.Clear());
        Assert.Throws<NotSupportedException>(() => dictionary["culture"] = "changed");
        var roleList = Assert.IsAssignableFrom<IList<string>>(request.InputRoles);
        Assert.True(roleList.IsReadOnly); Assert.Throws<NotSupportedException>(() => roleList.Clear());
        var issueList = Assert.IsAssignableFrom<IList<AiAuditIssueFact>>(request.ValidationIssues);
        Assert.True(issueList.IsReadOnly); Assert.Throws<NotSupportedException>(() => issueList.Clear());
    }

    [Theory]
    [InlineData("/home/assets")]
    [InlineData("C:\\Users\\assets")]
    [InlineData("D:/assets")]
    [InlineData("\\\\server\\share")]
    [InlineData("../assets")]
    [InlineData("assets/../other")]
    [InlineData("./assets")]
    [InlineData("assets//other")]
    [InlineData("assets/")]
    [InlineData("assets\nother")]
    public void BuilderCannotSendAbsoluteOrTraversalDestinationsEvenFromForgedInternalPlan(string relative) =>
        Assert.Throws<ArgumentException>(() => new AiAuditRequestBuilder().Build(AiAuditTestData.Plan(relative: relative), new NapIssueReport([])));

    [Fact]
    public void ClientExceptionHasSafeMessageAndOptionalInnerConstructor()
    {
        Assert.True(typeof(AiAuditClientException).IsSealed);
        var inner = new InvalidDataException("safe local diagnostic");
        Assert.Equal("safe", new AiAuditClientException("safe").Message);
        var exception = new AiAuditClientException("safe", inner);
        Assert.Same(inner, exception.InnerException);
        Assert.Equal("safe", exception.Message);
        Assert.DoesNotContain(AiAuditTestData.Key, exception.Message);
    }
}

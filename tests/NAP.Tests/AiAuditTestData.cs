using System.Reflection;
using System.Text.Json;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

internal static class AiAuditTestData
{
    internal const string Key = "TEST_SECRET_KEY_DO_NOT_LEAK";
    internal const string AssetId = "portrait_example_001";
    internal const string Relative = "assets/portrait_example_001";
    internal static readonly string[] PrivateSentinels = ["SECRET_PACKAGE", "SECRET_MANIFEST", "SECRET_SOURCE",
        "SECRET_PRODUCTION", "SECRET_FULL_DESTINATION", "SECRET_ARCHIVE", "SECRET_INBOX", "SECRET_STAGING", "SECRET_STATE", "SECRET_CACHE",
        "SECRET_MESSAGE", "SECRET_SUBJECT", "SECRET_DETAIL", Key];

    internal static ProcessingPlan Plan(Dictionary<string, string>? classification = null,
        Dictionary<string, string>? inputs = null, string relative = Relative)
    {
        var key = new UniverseAssetKey(new UniverseId("test_universe"), AssetId);
        var destination = Construct<ProductionAssetDestination>(key, @"D:\SECRET_PRODUCTION", relative, @"D:\SECRET_FULL_DESTINATION");
        return Construct<ProcessingPlan>(key, "portrait", "portrait_npc", classification ?? new() { ["culture"] = "example" },
            @"C:\SECRET_PACKAGE", @"C:\SECRET_MANIFEST", inputs ?? new() { ["prompt"] = @"C:\SECRET_SOURCE\prompt.md", ["png_master"] = "/home/SECRET_SOURCE/master.png" }, destination);
    }

    internal static AiAuditRequest Request() => new AiAuditRequestBuilder().Build(Plan(), new NapIssueReport([]));
    internal static AiAuditFinding Finding(AiAuditFindingSeverity severity = AiAuditFindingSeverity.Warning) => new("destination_incoherent", severity, "Review destination.");
    internal static string ReportJson(string decision = "PASS", string summary = "Coherent.", string findings = "[]") =>
        "{\"schema_version\":1,\"decision\":" + JsonSerializer.Serialize(decision) + ",\"summary\":" + JsonSerializer.Serialize(summary) + ",\"findings\":" + findings + "}";
    internal static string FindingJson(string severity = "WARNING", string code = "destination_incoherent", string message = "Review.") =>
        "{\"code\":" + JsonSerializer.Serialize(code) + ",\"severity\":" + JsonSerializer.Serialize(severity) + ",\"message\":" + JsonSerializer.Serialize(message) + "}";
    internal static string Outer(string report) => JsonSerializer.Serialize(new
    {
        status = "completed", steps = new[] { new { type = "model_output", content = new[] { new { type = "text", text = report } } } }
    });
    internal static T Construct<T>(params object[] args) => Assert.IsType<T>(Assert.Single(typeof(T).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)).Invoke(args));
    internal static void AssertNoPrivateFacts(string text)
    {
        foreach (var sentinel in PrivateSentinels) Assert.DoesNotContain(sentinel, text);
        foreach (var name in new[] { "PackageRoot", "ManifestPath", "ProductionRoot", "FullDirectoryPath", "ArchiveRoot", "InboxRoot", "StagingRoot", "StateRoot", "CacheRoot", "JobId", "timestamp", "hash", "api_key", "hostname", "username" })
            Assert.DoesNotContain(name, text, StringComparison.OrdinalIgnoreCase);
    }
    internal static void AssertStatelessApi(Type type, string methodName, Type result, params Type[] parameters)
    {
        Assert.True(type.IsPublic); Assert.True(type.IsSealed);
        Assert.Empty(Assert.Single(type.GetConstructors()).GetParameters());
        Assert.Empty(type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));
        var method = Assert.Single(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        Assert.Equal(methodName, method.Name); Assert.Equal(result, method.ReturnType);
        Assert.Equal(parameters, method.GetParameters().Select(p => p.ParameterType));
    }
}

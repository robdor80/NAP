using System.IO.Compression;
using System.Text;
using System.Text.Json;
using NAP.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace NAP.Tests;

internal sealed class PreparationTestFixture : IDisposable
{
    internal readonly string Root = Path.Combine(Path.GetTempPath(), "nap-preparation-" + Guid.NewGuid().ToString("N"));
    internal UniverseStorageConfig Roots { get; }
    internal UniverseContext Context { get; }
    internal AutomationQueueStore Queue { get; }
    internal DurablePreparationService Service { get; }
    internal readonly TestClock Clock = new();
    private Dictionary<string, string> _foreign = [];
    internal static readonly InboxAdmissionOptions Fast = new() { StabilitySamples = 2, SampleInterval = TimeSpan.Zero, MinFreeBytes = 0 };
    internal PreparationTestFixture(string universe = "test_universe", string assetType = "portrait", string profile = "portrait_npc", int width = 40, int height = 50)
    {
        Roots = new(new(universe), Path.Combine(Root, "workspace"), Path.Combine(Root, "production"), Path.Combine(Root, "archive"));
        foreach (var path in new[] { Roots.WorkspaceRoot, Roots.InboxRoot, Roots.StagingRoot, Roots.ProductionRoot, Roots.ArchiveRoot }) Directory.CreateDirectory(path);
        var json = JsonSerializer.Serialize(new
        {
            schema_version = 4, universe_id = universe, display_name = "Synthetic", classification_dimensions = new[] { "kind" },
            asset_rules = new[] { new { asset_type = assetType, production_profile = profile, allowed_classification = new[] { "kind" }, required_classification = Array.Empty<string>(),
                package_files = new object[] { new { role = "master", suffix = "", extension = ".png", required = true, content_validator = "png_master" },
                    new { role = "brief", suffix = "_brief", extension = ".txt", required = true, content_validator = (string?)null } },
                routing = new { segments = new object[] { new { literal = "assets" }, new { asset_id = true } } },
                conversion = new { kind = "png_to_webp", source_role = "master", output_width = width, output_height = height, webp_quality = 90 } } }
        });
        Context = new(UniverseProfileLoader.Load(new MemoryStream(Encoding.UTF8.GetBytes(json))), Roots);
        Queue = new(Roots); Service = new(Context, Queue, Clock);
        foreach (var path in new[] { Roots.ProductionRoot, Roots.ArchiveRoot }) File.WriteAllText(Path.Combine(path, "foreign.txt"), "unrelated synthetic sentinel");
        FreezeForeign();
    }
    internal string Zip(string name = "input.zip", string asset = "portrait_example_001", int width = 40, int height = 50,
        string brief = "synthetic brief", int timestamp = 0, int seed = 1, bool invalidPng = false)
    {
        var rule = Context.Profile.AssetRules.Single(); var path = Path.Combine(Roots.InboxRoot, name);
        using var file = new FileStream(path, FileMode.Create, FileAccess.ReadWrite);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        Add(asset + "_manifest.json", Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { schema_version = 2, universe_id = Context.Id.Value, asset_id = asset,
            asset_type = rule.AssetType, production_profile = rule.ProductionProfile, classification = new { kind = "synthetic" } })));
        Add(asset + ".png", invalidPng ? Encoding.UTF8.GetBytes("not PNG") : Png(width, height, seed));
        Add(asset + "_brief.txt", Encoding.UTF8.GetBytes(brief)); return path;
        void Add(string entryName, byte[] bytes)
        { var entry = archive.CreateEntry(entryName); entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(timestamp); using var output = entry.Open(); output.Write(bytes); }
    }
    internal static byte[] Png(int width, int height, int seed)
    {
        using var image = new Image<Rgba32>(width, height);
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) image[x, y] = new((byte)(x * 3 + seed), (byte)(y * 5 + seed), (byte)(x + y + seed), 255);
        using var output = new MemoryStream(); image.SaveAsPng(output); return output.ToArray();
    }
    internal InboxAdmissionResult Admit(AutomationQueueOwner owner, string? path = null)
    { var result = new InboxAdmissionService(Context, Queue, Fast).Admit(owner, path ?? Zip()); Assert.Equal(InboxAdmissionCode.Admitted, result.Code); return result; }
    internal string Journal(JobId job) => Path.Combine(Roots.StateRoot, job.Value + ".json");
    internal ProfileNormalizationPolicy Normalization(int area = 300, int axis = 300, bool operational = true) => new(
        Context.Profile.AssetRules.Single().AssetType, Context.Profile.AssetRules.Single().ProductionProfile,
        new(Context.Profile.AssetRules.Single().Conversion!.OutputWidth, Context.Profile.AssetRules.Single().Conversion!.OutputHeight),
        new() { MaxAddedAreaBasisPoints = area, MaxAxisGrowthBasisPoints = axis, MaxInputPixels = 2_000_000, MaxCanvasPixels = 2_100_000,
            MaxFileBytes = 4 * 1024 * 1024, MaxPackageBytes = 8 * 1024 * 1024, MaxPackageEntries = 32 },
        128L * 1024 * 1024, "nearest-edge-canvas-v1", operational);
    internal StoredAutomationPolicy Policy(AutomationQueueOwner owner, ProfileNormalizationPolicy? normalization = null,
        int requests = 10, int attempts = 3, decimal cost = 10, bool authorizePreparation = false, bool allowDuplicate = false, int retry = 5, int circuitThreshold = 2,
        AutomationOperations operations = AutomationOperations.Admit | AutomationOperations.Normalize | AutomationOperations.Audit)
    {
        var definition = new AutomationPolicyDefinition("synthetic_policy", 1, Roots, operations,
            new(128L * 1024 * 1024, 512L * 1024 * 1024, 100), new(requests, attempts, 1, cost), normalization is null ? [] : [normalization]);
        var policy = Queue.SavePolicy(owner, definition);
        policy = Queue.RecordAuthorization(owner, policy.Reference, policy.Revision, new("synthetic_authorization", Context.Id, policy.Reference, "synthetic_responsible", DateTimeOffset.UtcNow));
        Service.State.RegisterGrant(owner, policy, new("synthetic_terms", 1, policy.Reference, policy.Authorization!.AuthorizationId,
            policy.Authorization.Actor, DateTimeOffset.UtcNow, "synthetic_provider", "synthetic_model", 1,
            Enumerable.Repeat(retry, attempts - 1).ToArray(), circuitThreshold, 60, allowDuplicate));
        if (authorizePreparation) policy = Queue.SetPolicyState(owner, policy.Reference, policy.Revision, AutomationPolicyState.PreparationOnly);
        return policy;
    }
    internal EffectiveRuleSnapshot ExceptionalRule(int width, int height)
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(UniverseProfileSnapshotRenderer.Render(Context.Profile))!;
        json["asset_rules"]![0]!["conversion"]!["output_width"] = width; json["asset_rules"]![0]!["conversion"]!["output_height"] = height;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json.ToJsonString()));
        var rule = Context.Profile.AssetRules.Single(); return EffectiveRuleSnapshot.Create(Context.Id, 1, rule.AssetType, rule.ProductionProfile,
            UniverseProfileSnapshotRenderer.Render(UniverseProfileLoader.Load(stream)));
    }
    internal void FreezeForeign() => _foreign = Foreign();
    private Dictionary<string, string> Foreign() => new[] { Roots.ArchiveRoot, Roots.ProductionRoot }.SelectMany(p => Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories))
        .Concat(File.Exists(Roots.CatalogPath) ? new[] { Roots.CatalogPath } : [])
        .OrderBy(p => p, StringComparer.Ordinal).ToDictionary(p => p, p => new Sha256Hasher().Compute(p).Hex);
    internal void AssertUntouched()
    {
        Assert.Equal(JsonSerializer.Serialize(_foreign), JsonSerializer.Serialize(Foreign()));
        if (!_foreign.ContainsKey(Roots.CatalogPath)) Assert.False(File.Exists(Roots.CatalogPath));
        Assert.DoesNotContain(Directory.EnumerateFiles(Roots.ProductionRoot, "*", SearchOption.AllDirectories), p => p.EndsWith(".webp", StringComparison.Ordinal));
        Assert.False(Queue.CurrentPolicy().IsExecutionAuthorized);
    }
    public void Dispose() => Directory.Delete(Root, true);
    internal sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        internal void Advance(int seconds) => _now = _now.AddSeconds(seconds);
    }
    internal sealed class SimulatedCrash : Exception;
    internal static void Hook(DurablePreparationService service, Action<string> action) => typeof(DurablePreparationService)
        .GetProperty("Checkpoint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(service, action);
}

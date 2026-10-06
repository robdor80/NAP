using NAP.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace NAP.Tests;

internal sealed class ProductionTestFixture : IDisposable
{
    internal readonly PackageSemanticTestFixture Source;
    internal UniverseContext Context => Source.Context;
    internal string Root => Source.Root;
    internal ValidatedAssetPackage Package { get; private set; } = null!;
    internal ProcessingPlan Processing { get; private set; } = null!;
    internal JobId ProductionJob { get; private set; } = null!;
    internal string Master => Package.FilesByRole[Package.AssetRule.Conversion!.SourceRole];
    internal static AiAuditReport Pass => ArchiveTestFixture.Pass;
    internal const long Budget = 1_000_000;

    internal ProductionTestFixture(bool nimroel = false, string universe = "future_world", int width = 8, int height = 10, int quality = 73,
        bool extraRole = false, bool conversion = true)
    {
        var rules = new List<AssetPackageFileRule> { new("original", "", ".png", true, "png_master"),
            new("prompt", "_prompt", ".md", true), new("info", "_info", ".md", true),
            new("identity", "_visual_identity", ".json", true) };
        if (extraRole) rules.Add(new("future_notes", "_notes", ".txt", true));
        var profile = nimroel ? null : new UniverseProfile(new UniverseId(universe), "Future world", [],
            [new UniverseAssetRule("emblem", "painted_icon", [], [], rules,
                new AssetRoutingRule([AssetRouteSegment.Literal("icons"), AssetRouteSegment.Literal("approved"), AssetRouteSegment.AssetId()]),
                conversion ? new ImageConversionRule(ImageConversionKind.PngToWebp, "original", width, height, quality) : null)]);
        Source = new PackageSemanticTestFixture(profile, nimroel ? ArchiveTestFixture.NimroelAsset : "emblem_example_001",
            nimroel ? "portrait" : "emblem", nimroel ? "portrait_npc" : "painted_icon");
        try
        {
            if (nimroel)
            {
                Source.Manifest = Source.Manifest with { Classification = new() { ["culture"] = "norgard", ["location"] = "treskal", ["role"] = "farmer", ["sex"] = "male" } };
                Source.WriteManifest();
            }
            var master = Source.Context.Profile.AssetRules.Single(r => r.AssetType == Source.Manifest.AssetType && r.ProductionProfile == Source.Manifest.ProductionProfile).PackageFiles.Single(f => f.ContentValidator == "png_master");
            WritePng(Source.PathFor(master), width, height);
            foreach (var root in new[] { Context.Storage.WorkspaceRoot, Context.Storage.ProductionRoot, Context.Storage.ArchiveRoot,
                         Context.Storage.StateRoot, Context.Storage.InboxRoot, Context.Storage.StagingRoot, Context.Storage.CacheRoot }) Directory.CreateDirectory(root);
            Refresh();
            ProductionJob = Job(JobState.Audited);
        }
        catch { Source.Dispose(); throw; }
    }

    internal void Refresh()
    {
        var validation = new PackageSemanticValidator().Validate(Source.PackageRoot, Context);
        Assert.True(validation.IsValid, string.Join("; ", validation.Issues.Issues.Select(i => i.Message)));
        Package = validation.Package!;
        var repository = new ProductionRepositoryValidator().Validate(Context).Repository!;
        Processing = new ProcessingPlanBuilder().Build(Package, repository, new ProductionDestinationResolver().Resolve(Package, repository));
    }

    internal ArchiveMasterPlan ArchivePlan() => new ArchiveMasterPlanner(Context).Plan(Package, Processing);
    internal ArchiveMasterResult Archive() => new ArchiveMasterExecutor(Context).Execute(ArchivePlan(), Pass);
    internal ProductionAssetPlan Plan(ArchiveMasterResult? archive = null, long budget = Budget, JobId? jobId = null) => new ProductionAssetPlanner(Context).Plan(Package, Processing, archive ?? Archive(), budget, jobId ?? ProductionJob);
    internal ProductionAssetResult Execute(ProductionAssetPlan plan) => new ProductionAssetExecutor(Context).Execute(plan, Pass);
    internal JobId Job(JobState state = JobState.Planned)
    {
        var id = JobId.Create(); var store = new JobStateStore(Context); store.Create(id);
        if (state == JobState.Failed) { store.Transition(id, state); return id; }
        for (var next = JobState.Staged; next <= state; next++) store.Transition(id, next);
        return id;
    }
    internal void Prepare(ProductionAssetPlan plan, int count)
    {
        using var jobLease = (IDisposable)Invoke(null, "ExecutionMutex", "Acquire", "Job", Context.Storage.StateRoot, plan.JobId!.Value)!;
        using var productionLease = (IDisposable)Invoke(null, "ExecutionMutex", "Acquire", "Production", Context.Storage.ProductionRoot, "")!;
        var evidence = Invoke(null, "ProductionExecutionEvidence", "Begin", Context, plan, jobLease)!;
        Invoke(null, "ProductionPaths", "Destination", Context, plan.RelativeDirectory, true);
        var executor = new ProductionAssetExecutor(Context);
        foreach (var file in plan.Files.Take(count))
        {
            Invoke(executor, "ProductionAssetExecutor", "WriteFile", plan, file);
            Invoke(evidence, "ProductionExecutionEvidence", "Record", file, jobLease);
        }
    }
    internal static byte[] Bytes(ProductionAssetFile file) => file.Kind == ProductionAssetFileKind.GeneratedWebp ? file.ToArray() : File.ReadAllBytes(file.SourcePath!);
    internal static void WritePng(string path, int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) image[x, y] = new Rgba32((byte)(x * 31), (byte)(y * 23), (byte)((x + y) * 17));
        image.SaveAsPng(path);
    }
    internal static void Stop(ProductionStorageException exception, string code) => Assert.Contains(exception.Issues.Issues,
        i => i.Code == code && i.Severity == NapIssueSeverity.Error && i.Disposition == NapIssueDisposition.Stop);
    internal static object? Invoke(object? target, string type, string method, params object[] args) => ArchiveTestFixture.Invoke(target, ArchiveTestFixture.CoreType(type), method, args);
    internal static UniverseContext WithProduction(UniverseContext context, string root) => new(context.Profile,
        new UniverseStorageConfig(context.Id, context.Storage.WorkspaceRoot, root, context.Storage.ArchiveRoot));
    public void Dispose() => Source.Dispose();
}

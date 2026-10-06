using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

internal sealed class ArchiveTestFixture : IDisposable
{
    internal const string NimroelAsset = "portrait_treskal_farmer_male_040";
    internal readonly PackageSemanticTestFixture PackageFixture;
    internal UniverseContext Context => PackageFixture.Context;
    internal string Root => PackageFixture.Root;
    internal ValidatedAssetPackage Package { get; private set; }
    internal ProcessingPlan Processing { get; private set; }
    internal ArchiveMasterIndexStore Store => new(Context);
    internal string LockPath => Path.Combine(Context.Storage.ArchiveRoot, "_nap", "archive.lock");
    internal JobId Job { get; } = JobId.Create();
    internal static AiAuditReport Pass => new(AiAuditDecision.Pass, "Coherent archive operation.", []);

    internal ArchiveTestFixture(UniverseProfile? profile = null, string assetId = NimroelAsset)
    {
        PackageFixture = new PackageSemanticTestFixture(profile, assetId);
        Package = null!;
        Processing = null!;
        try
        {
            if (profile is null)
            {
                PackageFixture.Manifest = PackageFixture.Manifest with
                {
                    Classification = new() { ["culture"] = "norgard", ["location"] = "treskal", ["role"] = "farmer", ["sex"] = "male" }
                };
                PackageFixture.WriteManifest();
            }
            foreach (var path in new[] { Context.Storage.ArchiveRoot, Context.Storage.WorkspaceRoot, Context.Storage.ProductionRoot,
                         Context.Storage.StateRoot, Context.Storage.InboxRoot, Context.Storage.StagingRoot, Context.Storage.CacheRoot })
            {
                Directory.CreateDirectory(path);
                File.WriteAllText(Path.Combine(path, "sentinel.txt"), "protected fixture sentinel");
            }
            var states = new JobStateStore(Context);
            states.Create(Job);
            foreach (var state in new[] { JobState.Staged, JobState.Validated, JobState.Planned, JobState.Audited }) states.Transition(Job, state);
            Refresh();
        }
        catch
        {
            PackageFixture.Dispose();
            throw;
        }
    }

    internal void Refresh()
    {
        var validation = new PackageSemanticValidator().Validate(PackageFixture.PackageRoot, Context);
        Assert.True(validation.IsValid, string.Join("; ", validation.Issues.Issues.Select(i => i.Message)));
        Package = validation.Package!;
        var repository = new ProductionRepositoryValidator().Validate(Context).Repository!;
        var destination = new ProductionDestinationResolver().Resolve(Package, repository);
        Processing = new ProcessingPlanBuilder().Build(Package, repository, destination);
    }

    internal ArchiveMasterPlan Plan() => new ArchiveMasterPlanner(Context).Plan(Package, Processing);
    internal ArchiveMasterResult Execute(ArchiveMasterPlan? plan = null) => new ArchiveMasterExecutor(Context).Execute(plan ?? Plan(), Pass);

    internal void PrepareFinals(ArchiveMasterPlan plan, int count)
    {
        Directory.CreateDirectory(plan.DestinationDirectory);
        foreach (var file in plan.Files.Take(count)) File.WriteAllBytes(file.DestinationPath, File.ReadAllBytes(file.SourcePath));
    }

    internal void WriteIndex(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Store.IndexPath)!);
        File.WriteAllText(Store.IndexPath, json);
    }

    internal static UniverseProfile Generic(string universe = "other_universe", params AssetPackageFileRule[] files) =>
        new(new UniverseId(universe), "Other", [],
            [new UniverseAssetRule("portrait", "portrait_npc", [], [], files.Length == 0 ?
                [new AssetPackageFileRule("original_png", "", ".png", true, "png_master"), new AssetPackageFileRule("notes", "_notes", ".txt", true)] : files,
                new AssetRoutingRule([AssetRouteSegment.Literal("originals"), AssetRouteSegment.AssetId()]))]);

    internal static object? Invoke(object? target, Type type, string method, params object[] args)
    {
        try { return type.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)!.Invoke(target, args); }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    internal static Type CoreType(string name) => typeof(ArchiveMasterPlanner).Assembly.GetType("NAP.Core." + name)!;

    // Test-only deterministic lease revocation while Publish serializes entries. No timing/OS rename assumptions.
    internal static ArchiveMasterIndex RevokeLeaseAfterEntries(ArchiveMasterIndex index, IDisposable lease)
    {
        typeof(ArchiveMasterIndex).GetField("<Entries>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(index, new RevokingEntries(index.Entries, lease));
        return index;
    }

    private sealed class RevokingEntries(IReadOnlyList<ArchiveMasterIndexEntry> entries, IDisposable lease) : IReadOnlyList<ArchiveMasterIndexEntry>
    {
        public int Count => entries.Count;
        public ArchiveMasterIndexEntry this[int index] => entries[index];
        public IEnumerator<ArchiveMasterIndexEntry> GetEnumerator()
        {
            foreach (var entry in entries) yield return entry;
            lease.Dispose();
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    internal static Dictionary<string, string> Snapshot(string root)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!Directory.Exists(root)) return result;
        Visit(root);
        return result;
        void Visit(string path)
        {
            foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos())
            {
                var relative = Path.GetRelativePath(root, entry.FullName);
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) result.Add(relative, "reparse");
                else if (entry is DirectoryInfo) { result.Add(relative, "directory"); Visit(entry.FullName); }
                else result.Add(relative, $"{new FileInfo(entry.FullName).Length}:{entry.LastWriteTimeUtc.Ticks}:{new Sha256Hasher().Compute(entry.FullName).Hex}");
            }
        }
    }

    internal static void AssertSnapshot(Dictionary<string, string> expected, string root) =>
        Assert.Equal(expected.OrderBy(p => p.Key, StringComparer.Ordinal), Snapshot(root).OrderBy(p => p.Key, StringComparer.Ordinal));

    internal static void Junction(string link, string target)
    {
        if (!OperatingSystem.IsWindows()) { Directory.CreateSymbolicLink(link, target); return; }
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe", Arguments = $"/c mklink /J \"{link}\" \"{target}\"", UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        })!;
        Assert.True(process.WaitForExit(10000));
        Assert.Equal(0, process.ExitCode);
    }

    internal static string ValidIndex(string universe = "nimroel", string? entries = null) =>
        "{\"schema_version\":1,\"universe_id\":" + JsonSerializer.Serialize(universe) + ",\"entries\":" + (entries ?? "[]") + "}";

    internal static string ValidEntry(string id = NimroelAsset) =>
        "{\"asset_id\":" + JsonSerializer.Serialize(id) + ",\"asset_type\":\"portrait\",\"master_sha256\":\"" + new string('a', 64) +
        "\",\"master_size_bytes\":80,\"relative_directory\":\"originals/" + id + "\",\"verified\":true}";

    internal static void AssertStop(ArchiveStorageException exception, string code) => Assert.Contains(exception.Issues.Issues,
        issue => issue.Code == code && issue.Severity == NapIssueSeverity.Error && issue.Disposition == NapIssueDisposition.Stop);

    public void Dispose() => PackageFixture.Dispose();
}

using System.Diagnostics;
using System.Reflection;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

internal sealed class GitProductionFixture : IDisposable
{
    internal ProductionTestFixture Production { get; }
    internal UniverseContext Context => Production.Context;
    internal string Root => Context.Storage.ProductionRoot;
    internal string Remote => Path.Combine(Production.Root, "remote.git");
    internal GitProductionService Service { get; } = new();
    internal GitProductionInspector Inspector { get; } = new();
    internal GitProductionFixture(string universe = "future_world", bool sha256 = false, bool synchronized = true)
    {
        Production = new(universe: universe);
        Enable(Service); Enable(Inspector);
        Run(Root, "init", "--initial-branch=main", "--object-format=" + (sha256 ? "sha256" : "sha1"));
        Run(Root, "config", "user.name", "NAP temporary test"); Run(Root, "config", "user.email", "nap@example.invalid");
        Run(Root, "config", "core.autocrlf", "false");
        File.WriteAllText(Path.Combine(Root, "README.md"), "base\n");
        Run(Root, "add", "--", "README.md"); Run(Root, "commit", "-m", "base");
        Run(Root, "init", "--bare", "--initial-branch=main", "--object-format=" + (sha256 ? "sha256" : "sha1"), Remote);
        Run(Root, "remote", "add", "origin", Remote);
        if (synchronized) Run(Root, "push", "-u", "origin", "main");
        else { Run(Root, "config", "branch.main.remote", "origin"); Run(Root, "config", "branch.main.merge", "refs/heads/main"); }
    }
    internal ProductionAssetResult Produce() => Production.Execute(Production.Plan());
    internal ProductionAssetResult ProduceAdditional(string assetId = "emblem_other_002")
    {
        using var source = new PackageSemanticTestFixture(Context.Profile, assetId, "emblem", "painted_icon");
        ProductionTestFixture.WritePng(source.PathFor(Context.Profile.AssetRules[0].PackageFiles.Single(p => p.Role == "original")), 8, 10);
        var package = new PackageSemanticValidator().Validate(source.PackageRoot, Context).Package!;
        var repository = new ProductionRepositoryValidator().Validate(Context).Repository!;
        var processing = new ProcessingPlanBuilder().Build(package, repository, new ProductionDestinationResolver().Resolve(package, repository));
        var archive = new ArchiveMasterExecutor(Context).Execute(new ArchiveMasterPlanner(Context).Plan(package, processing), ProductionTestFixture.Pass);
        var plan = new ProductionAssetPlanner(Context).Plan(package, processing, archive, ProductionTestFixture.Budget, Production.Job(JobState.Audited));
        return new ProductionAssetExecutor(Context).Execute(plan, ProductionTestFixture.Pass);
    }
    internal GitCommitPlan Plan(ProductionAssetResult? result = null, string message = "Publish verified asset") => Service.PrepareCommit(Context, [result ?? Produce()], message);
    internal string Git(params string[] args) => Run(Root, args);
    internal string RemoteGit(params string[] args) => Run(Remote, args);
    internal string ReceiptPath(GitOperationId id) => Path.Combine(Context.Storage.StateRoot, "git-production", id.Value + ".json");
    internal void Observe(Action<string> observer) => BackupTestSupport.Set(Service, "Observer", observer);
    internal void ObserveCommands(Action<string> observer) => BackupTestSupport.Set(BackupTestSupport.Get(Service, "Git"), "Observer", observer);
    internal void RemoteAdvance()
    {
        var writer = Path.Combine(Production.Root, "remote-writer-" + Guid.NewGuid().ToString("N"));
        Run(Root, "clone", Remote, writer); Run(writer, "config", "user.name", "Other writer"); Run(writer, "config", "user.email", "other@example.invalid");
        File.WriteAllText(Path.Combine(writer, "remote.txt"), "other operation\n"); Run(writer, "add", "--", "remote.txt");
        Run(writer, "commit", "-m", "remote advanced"); Run(writer, "push", "origin", "main");
    }
    internal static void Enable(object target) => BackupTestSupport.Set(BackupTestSupport.Get(target, "Git"), "AllowLocalTestTransport", true);
    internal static GitProductionException Stop(Action action, string? code = null)
    {
        var e = Assert.Throws<GitProductionException>(action); Assert.True(e.Issues.ShouldStop);
        if (code is not null) Assert.Contains(e.Issues.Issues, i => i.Code == code);
        Assert.All(e.Issues.Issues, i => { Assert.Null(i.SubjectPath); Assert.Null(i.Detail); }); return e;
    }
    internal static string Run(string root, params string[] args)
    {
        // Generic fixture runner is test-only, never a Core API. All writes target validated temporary fixture roots.
        var full = Path.GetFullPath(root); var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        Assert.StartsWith(temp, full, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        var info = new ProcessStartInfo("git") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "-c", "core.autocrlf=false", "-c", "commit.gpgSign=false", "-c", "core.hooksPath=/dev/null", "-C", root }.Concat(args)) info.ArgumentList.Add(a);
        foreach (var key in info.Environment.Keys.Where(k => k.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToArray()) info.Environment.Remove(key);
        info.Environment["GIT_CONFIG_NOSYSTEM"] = "1"; info.Environment["GIT_CONFIG_GLOBAL"] = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
        info.Environment["GIT_TERMINAL_PROMPT"] = "0"; info.Environment["GIT_ALLOW_PROTOCOL"] = "file";
        using var p = Process.Start(info)!; var output = p.StandardOutput.ReadToEndAsync(); var errors = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(30000)) { p.Kill(true); Assert.Fail("Test Git timed out."); }
        Task.WaitAll(output, errors); Assert.True(p.ExitCode == 0, errors.Result); return output.Result.TrimEnd('\r', '\n');
    }
    internal static T Invoke<T>(string type, string method, params object[] args)
    {
        try { return (T)typeof(AssetCatalog).Assembly.GetType("NAP.Core." + type)!.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args)!; }
        catch (TargetInvocationException e) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException!).Throw(); throw; }
    }
    public void Dispose()
    {
        var full = Path.GetFullPath(Production.Root); var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        Assert.StartsWith(temp, full, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        foreach (var file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0) File.SetAttributes(file, FileAttributes.Normal);
        Production.Dispose();
    }
}

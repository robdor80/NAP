using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

internal static class BackupTestSupport
{
    internal static void Set(object target, string name, object value) => target.GetType().GetProperty(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    internal static object Get(object target, string name) => target.GetType().GetProperty(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    internal static IDisposable Lock(UniverseContext c, string scope)
    {
        var type = typeof(AssetCatalog).Assembly.GetType("NAP.Core.ExecutionMutex")!;
        return (IDisposable)type.GetMethod("Acquire", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null,
            [scope, scope == "Catalog" ? c.Storage.StateRoot : c.Storage.ProductionRoot, scope == "Catalog" ? "AssetCatalog.db" : ""])!;
    }
    internal static FileStream ArchiveLock(UniverseContext c)
    {
        var path = Path.Combine(c.Storage.ArchiveRoot, "_nap", "archive.lock"); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    internal static BackupException Stop(Action action, string? code = null)
    {
        var e = Assert.Throws<BackupException>(action); Assert.True(e.Issues.ShouldStop);
        if (code is not null) Assert.Contains(e.Issues.Issues, i => i.Code == code);
        Assert.All(e.Issues.Issues, i => { Assert.Null(i.SubjectPath); Assert.Null(i.Detail); }); return e;
    }
    internal static string Namespace(BackupKind k) => k switch
    { BackupKind.Database => "NAP_DATABASE_BACKUPS", BackupKind.RepositorySnapshot => "NAP_REPOSITORY_SNAPSHOTS", _ => "NAP_GIT_BUNDLES" };
    internal static string DirectoryPath(UniverseContext c, BackupHistoryEntry e) => Path.Combine(c.Storage.ArchiveRoot, Namespace(e.Kind), e.BackupId.Value);
    internal static string ManifestPath(UniverseContext c, BackupHistoryEntry e) => Path.Combine(DirectoryPath(c, e), "manifest.json");
    internal static string Artifact(UniverseContext c, BackupHistoryEntry e) => Path.Combine(DirectoryPath(c, e), e.Manifest.Artifact);
    internal static void Mutate(UniverseContext c, BackupHistoryEntry e, Action<JsonObject> mutation)
    {
        var path = ManifestPath(c, e); var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject(); mutation(json); File.WriteAllText(path, json.ToJsonString());
    }
    internal sealed class Clock(DateTimeOffset now) : TimeProvider
    { public override DateTimeOffset GetUtcNow() => now; }
    internal static string[] RecognizedDirectories(UniverseContext c, BackupKind kind)
    {
        var root = Path.Combine(c.Storage.ArchiveRoot, Namespace(kind));
        return Directory.Exists(root) ? Directory.GetDirectories(root, "b_*") : [];
    }
    internal static string Git(string root, params string[] args)
    {
        var info = new ProcessStartInfo("git") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("-C"); info.ArgumentList.Add(root); foreach (var arg in args) info.ArgumentList.Add(arg);
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        using var p = Process.Start(info)!; var output = p.StandardOutput.ReadToEndAsync(); var errors = p.StandardError.ReadToEndAsync();
        Assert.True(p.WaitForExit(30000)); Task.WaitAll(output, errors); Assert.True(p.ExitCode == 0, errors.Result); return output.Result.Trim();
    }
    internal static string CatalogRows(string path)
    {
        using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()); c.Open();
        var tables = new[] { "catalog_metadata", "assets", "classifications", "files", "documents", "visual_traits", "objectives", "objective_classifications", "objective_traits", "campaigns", "campaign_objectives" };
        var result = new Dictionary<string, string[]>();
        foreach (var table in tables)
        {
            using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT * FROM " + table; using var reader = cmd.ExecuteReader(); var rows = new List<string>();
            while (reader.Read()) rows.Add(JsonSerializer.Serialize(Enumerable.Range(0, reader.FieldCount).Select(i => reader.GetValue(i) switch
            { DBNull => null, byte[] b => Convert.ToBase64String(b), var v => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) }).ToArray()));
            result.Add(table, rows.Order(StringComparer.Ordinal).ToArray());
        }
        return JsonSerializer.Serialize(result);
    }
}

internal sealed class BackupRepositoryFixture : IDisposable
{
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "nap-backup-tests-" + Guid.NewGuid().ToString("N"));
    internal UniverseContext Context { get; }
    internal BackupRepositoryFixture(string universe = "alpha", bool git = false, bool sha256 = false)
    {
        var id = new UniverseId(universe); Context = new(new UniverseProfile(id, universe),
            new UniverseStorageConfig(id, Path.Combine(Root, "workspace"), Path.Combine(Root, "production"), Path.Combine(Root, "archive")));
        foreach (var path in new[] { Context.Storage.WorkspaceRoot, Context.Storage.StateRoot, Context.Storage.ProductionRoot, Context.Storage.ArchiveRoot }) Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(Context.Storage.ProductionRoot, "README.md"), "first\n");
        if (git)
        {
            BackupTestSupport.Git(Context.Storage.ProductionRoot, "init", "--initial-branch=main", "--object-format=" + (sha256 ? "sha256" : "sha1"));
            BackupTestSupport.Git(Context.Storage.ProductionRoot, "config", "user.name", "NAP test");
            BackupTestSupport.Git(Context.Storage.ProductionRoot, "config", "user.email", "nap@example.invalid");
            Commit("first"); BackupTestSupport.Git(Context.Storage.ProductionRoot, "branch", "history_branch");
            File.WriteAllText(Path.Combine(Context.Storage.ProductionRoot, "README.md"), "second\n"); Commit("second");
        }
    }
    private void Commit(string message)
    { BackupTestSupport.Git(Context.Storage.ProductionRoot, "add", "README.md"); BackupTestSupport.Git(Context.Storage.ProductionRoot, "commit", "-m", message); }
    public void Dispose()
    {
        var full = Path.GetFullPath(Root); var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(parent, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("nap-backup-tests-", StringComparison.Ordinal)) throw new InvalidOperationException("Invalid test cleanup root.");
        foreach (var file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(full, true);
    }
}

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NAP.Core;
using NAP.Presentation;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace NAP.Tests;

public sealed class Nap208UxTests
{
    [Fact]
    public async Task CurrentProductionPagesExcludeCompletedWithoutLosingReviewOrPending()
    {
        using var f = new CatalogTestFixture();
        var source = new WorkSource();
        using var work = new WorkflowViewModel(source, () => f.Context, new Decline(), currentOnly: true);
        await work.LoadPageAsync();
        Assert.Equal(20, work.Rows.Count);
        Assert.DoesNotContain(work.Rows, r => r.Value.State == QueueState.Completed);
        Assert.Contains(work.Rows, r => r.Value.State == QueueState.NeedsReview);
        await work.Next.ExecuteAsync();
        Assert.Equal(7, work.Rows.Count);
        Assert.False(work.Next.CanExecute(null));
        Assert.All(source.Requests, r => Assert.InRange(r.Limit, 1, 20));
        work.Filter = work.Filters.Single(f => f.Value == WorkflowFilter.Archived);
        await work.Apply.ExecuteAsync();
        Assert.All(work.Rows, r => Assert.True(r.Archived));
    }

    [Fact]
    public async Task HistoryStartsWithCompletedAndShowsOutcomeOnce()
    {
        using var f = new CatalogTestFixture();
        using var work = new WorkflowViewModel(new WorkSource(), () => f.Context, new Decline(), completedOnly: true);
        await work.LoadPageAsync();
        Assert.Equal(WorkflowFilter.Completed, work.Filter.Value);
        Assert.All(work.Rows, r => { Assert.Equal(QueueState.Completed, r.Value.State); Assert.Empty(r.Result); });
        Assert.Equal("Pescadora · Treskal · 001", UiLabels.AssetName("portrait_treskal_fisher_female_001"));
        Assert.Equal("Edad aparente", UiLabels.Dimension("/age/apparent_age/value"));
    }

    [Fact]
    public async Task TextSearchAndScopeCountsReadRealSyntheticSqliteWithoutLoadingDetailsOrChangingIt()
    {
        using var f = new CatalogTestFixture(nimroel: true);
        Seed(f, 32);
        var path = Path.Combine(f.Root, "ux-settings.json");
        new LocalUniverseSettingsStore(path).Save([f.Context.Storage]);
        var before = SHA256.HashData(File.ReadAllBytes(f.Catalog.CatalogPath));
        using var vm = new ExplorerViewModel([f.Context.Profile], new(path), new ExplorerService(), new NoFolders());
        await vm.InitializeAsync();
        Assert.Equal("32", vm.CatalogTotal);
        vm.SearchText = "pescadora";
        await vm.ApplyFilters.ExecuteAsync();
        Assert.Equal(16, vm.Assets!.Count);
        Assert.Equal("32", vm.CatalogTotal);
        Assert.Equal("16", vm.FilteredTotal);
        Assert.Null(vm.CountNotice);
        Assert.Null(vm.Detail);
        vm.SearchText = "";
        vm.AssetType = "scene";
        await vm.ApplyFilters.ExecuteAsync();
        Assert.Equal(8, vm.Assets!.Count);
        await vm.ResetFilters.ExecuteAsync();
        Assert.Equal(32, vm.Assets!.Count);
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(f.Catalog.CatalogPath)));
    }

    [VisualExplorerWindowsFact]
    public async Task RealWpfScreensAndFiveThousandTemporaryRecordsAreMeasuredAndCaptured()
    {
        using var f = new CatalogTestFixture(nimroel: true);
        Seed(f, 5000);
        var settings = Path.Combine(f.Root, "ux-settings.json");
        new LocalUniverseSettingsStore(settings).Save([f.Context.Storage]);
        var before = SHA256.HashData(File.ReadAllBytes(f.Catalog.CatalogPath));
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var output = Path.Combine(root, "artifacts/nap208/visual");
        Directory.CreateDirectory(output);
        var app = Path.Combine(root, "src/NAP.App/bin", AutomationHostProcessFixture.BuildConfiguration, "net10.0-windows/NAP.App.dll");
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { app, "--ux208-evidence=" + output, "--settings=" + settings }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(true); throw; }
        var text = await stdout; var errors = await stderr;
        File.WriteAllText(Path.Combine(output, "process.log"), text + errors);
        Assert.True(process.ExitCode == 0, text + errors);
        using var result = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "result.json")));
        Assert.Equal(5000, result.RootElement.GetProperty("records").GetInt32());
        Assert.Equal(0, result.RootElement.GetProperty("bindingErrors").GetInt32());
        Assert.True(result.RootElement.GetProperty("virtualizationVerified").GetBoolean());
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(f.Catalog.CatalogPath)));
    }

    // Disposable synthetic catalog only. Product DBs and their schemas are never edited by this test.
    private static void Seed(CatalogTestFixture f, int count)
    {
        f.Catalog.Initialize();
        using var image = new Image<Rgba32>(96, 120);
        for (var y = 0; y < 120; y++) for (var x = 0; x < 96; x++)
        {
            var face = (x - 48) * (x - 48) + (y - 38) * (y - 38) < 22 * 22;
            image[x, y] = face ? new(159, 198, 204) : y > 75 && Math.Abs(x - 48) < y - 65 ? new(43, 108, 135) : new(9, 30, 41);
        }
        using var png = new MemoryStream(); image.SaveAsPng(png);
        using var webp = new MemoryStream(); image.SaveAsWebp(webp);
        var original = png.ToArray(); var production = webp.ToArray();
        var mh = Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant();
        var ph = Convert.ToHexString(SHA256.HashData(production)).ToLowerInvariant();
        using var db = f.Raw(); using var tx = db.BeginTransaction();
        using var command = db.CreateCommand(); command.Transaction = tx;
        command.CommandText = """
            INSERT INTO assets VALUES($u,$id,$type,$profile,'physically_verified',NULL,$mh,$ms,$ph,$ps,$dir,$dir);
            INSERT INTO files VALUES($u,$id,'Archive','original','master',$dir||'/'||$id||'.png',$mh,$ms,1);
            INSERT INTO files VALUES($u,$id,'Production','output','generated_webp',$dir||'/'||$id||'.webp',$ph,$ps,1);
            INSERT INTO classifications VALUES($u,$id,'culture','norgard');
            INSERT INTO classifications VALUES($u,$id,'location','treskal');
            INSERT INTO classifications VALUES($u,$id,'role',$role);
            INSERT INTO classifications VALUES($u,$id,'sex',$sex);
            """;
        foreach (var name in new[] { "$u", "$id", "$type", "$profile", "$mh", "$ms", "$ph", "$ps", "$dir", "$role", "$sex" }) command.Parameters.AddWithValue(name, "");
        for (var i = 0; i < count; i++)
        {
            var type = i % 4 == 3 ? "scene" : "portrait";
            var role = i % 2 == 0 ? "fisher" : "farmer";
            var sex = i % 2 == 0 ? "female" : "male";
            var id = $"{type}_treskal_{role}_{sex}_sample{i / 999:D2}_{i % 999 + 1:D3}";
            var dir = "synthetic/" + id;
            var pdir = Directory.CreateDirectory(Path.Combine(f.Context.Storage.ProductionRoot, dir)).FullName;
            var adir = Directory.CreateDirectory(Path.Combine(f.Context.Storage.ArchiveRoot, dir)).FullName;
            File.WriteAllBytes(Path.Combine(pdir, id + ".webp"), production);
            File.WriteAllBytes(Path.Combine(adir, id + ".png"), original);
            var values = new object[] { f.Context.Id.Value, id, type, type == "portrait" ? "portrait_npc" : "scene_narrative", mh, original.Length, ph, production.Length, dir, role, sex };
            for (var n = 0; n < values.Length; n++) command.Parameters[n].Value = values[n];
            command.ExecuteNonQuery();
        }
        tx.Commit();
    }
    private sealed class Decline : IUserConfirmation { public Task<bool> ConfirmAsync(UiConfirmation c, CancellationToken ct) => Task.FromResult(false); }
    private sealed class NoFolders : IFolderPicker { public string? Pick(string title, string current) => null; }
    private sealed class WorkSource : IWorkflowDesktopClient
    {
        public List<WorkflowPageRequest> Requests { get; } = [];
        public Task<WorkflowPage> PageAsync(UniverseId u, WorkflowPageRequest r, CancellationToken ct)
        {
            Requests.Add(r);
            var rows = Enumerable.Range(0, 32).Select(i => new WorkflowRow(QueueItemId.Create(), u, "Recurso " + i,
                i < 5 ? QueueState.Completed : i < 10 ? QueueState.Running : i < 15 ? QueueState.NeedsReview : QueueState.Queued,
                QueueIncident.None, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, 1)).Where(row => r.Filter switch
                { WorkflowFilter.Completed => row.State == QueueState.Completed, WorkflowFilter.Processing => row.State == QueueState.Running,
                  WorkflowFilter.Review => row.State == QueueState.NeedsReview, WorkflowFilter.Pending => row.State == QueueState.Queued, _ => true }).ToArray();
            return Task.FromResult(new WorkflowPage(u, r.Offset, rows.Length, rows.Skip(r.Offset).Take(r.Limit).ToArray()));
        }
        public Task<IReadOnlyList<WorkflowBatch>> BatchesAsync(UniverseId u, CancellationToken ct) => Task.FromResult<IReadOnlyList<WorkflowBatch>>([]);
        public Task<WorkflowDetail> DetailAsync(UniverseId u, WorkflowDetailRequest r, CancellationToken ct) => throw new NotSupportedException();
        public Task MutateAsync(UniverseId u, AutomationOperation o, object r, CancellationToken ct) => throw new NotSupportedException();
    }
}

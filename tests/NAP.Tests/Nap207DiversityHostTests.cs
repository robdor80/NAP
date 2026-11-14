using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NAP.AI;
using NAP.Core;
using NAP.Presentation;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace NAP.Tests;

public sealed class Nap207DiversityHostTests
{
    private const string Question = "¿Cuántos pescadores tenemos en Treskal, separados por sexo?";
    private const string Breakdown = "localidad × profesion × sexo";
    private const string PrivateMarker = "private_marker_from_fixture";

    [VisualExplorerWindowsFact]
    public async Task PersonalHostReadsBothIndexesAndVerifiesGeminiOverAuthenticatedIpc()
    {
        using var fixture = new Fixture();
        var before = fixture.DatabaseBytes();
        using var handler = new SimulatedGemini();
        using var http = new HttpClient(handler);
        var ai = new ConfiguredAuditor(new GeminiCatalogDiversityClient(http, "TEST_GEMINI_KEY", "gemini-test"));
        await using var host = fixture.Start(ai);
        await host.Started;
        const string settingsFile = "unused-desktop-settings.json";
        var requests = Path.Combine(fixture.Root, "desktop-requests");
        var bridge = new AutomationDesktopBridge(host, fixture.Contexts, requests, ai);
        var connection = Fixture.Connection();
        await using var server = new AutomationPipeServer(connection, bridge.HandleAsync);
        var ipc = new AutomationHostClient(connection);
        using var desktop = new RemoteDesktopService(ipc, false);
        var context = fixture.Nimroel;

        var explorer = new ExplorerViewModel(fixture.Contexts.Select(c => c.Profile),
            new(Path.Combine(fixture.Root, settingsFile)), desktop, new NoFolders(),
            hostConfigurations: fixture.Contexts.Select(c => c.Storage).ToArray());
        using var shell = new ShellViewModel(explorer, desktop, new NoConfirmation(),
            personalHostDiversity: true);
        await shell.InitializeAsync();
        shell.NavigateTo("Auditor IA");
        await shell.LastRefresh;
        Assert.Equal(UiPageState.Ready, shell.Diversity.State);
        Assert.Equal(9, shell.Diversity.Report?.TotalAssets);
        Assert.NotEmpty(shell.Diversity.Report!.Gaps);
        Assert.False(shell.AskDiversity.CanExecute(null));
        Assert.Equal(0, handler.Calls);

        ai.Availability = new(true, ai.ModelId);
        await shell.Refresh.ExecuteAsync();
        shell.Diversity.Question = "   ";
        Assert.False(shell.AskDiversity.CanExecute(null));
        shell.Diversity.Question = Question;
        Assert.True(shell.AskDiversity.CanExecute(null));

        var mine = await ipc.CallAsync(AutomationOperation.DiversityInventory, context.Id.Value, new { });
        Assert.DoesNotContain(PrivateMarker, mine.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(Fixture.DuplicateId, mine.GetRawText(), StringComparison.Ordinal);
        AssertReport(AutomationProtocol.Read<CatalogDiversityInventory>(mine).Report);

        var query = shell.AskDiversity.ExecuteAsync();
        try
        {
            await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // An in-flight provider call leaves the universe owner's mailbox and other IPC calls available.
            var other = await desktop.ReadDiversityAsync(fixture.Other, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(fixture.Other.Id, other.Report.UniverseId);
            Assert.Equal(0, other.Report.TotalAssets);
            Assert.Equal(0, await host.QueryAsync(context.Id, scope => scope.Queue.List().Count).WaitAsync(TimeSpan.FromSeconds(5)));
            await Rejected(AutomationError.Busy, () => desktop.AskDiversityAsync(context, Question, CancellationToken.None));
        }
        finally { handler.Release.TrySetResult(); }
        await query;
        Assert.True(shell.AskDiversity.CanExecute(null));
        AssertBreakdown(shell.Diversity.Answer!);

        handler.InvalidAnswer = true;
        await shell.AskDiversity.ExecuteAsync();
        Assert.Null(shell.Diversity.Answer);
        Assert.Equal("diversity_query_failed", shell.Diversity.Notice?.Code);
        Assert.Equal(2, handler.Calls);
        await Rejected(AutomationError.InvalidContract, () => desktop.AskDiversityAsync(context, new string('x', 1201), CancellationToken.None));
        await Rejected(AutomationError.UniverseMismatch, () =>
            ipc.CallAsync(AutomationOperation.DiversityInventory, "outside_universe", new { }));
        await Rejected(AutomationError.InvalidContract, () =>
            ipc.CallAsync(AutomationOperation.DiversityAsk, context.Id.Value, new { Question, Inventory = new { Total = 100 } }));
        ai.Availability = new(false, null);
        await Rejected(AutomationError.Disabled, () => desktop.AskDiversityAsync(context, Question, CancellationToken.None));
        Assert.Equal(2, handler.Calls);
        Assert.Empty(Directory.EnumerateFiles(requests));
        fixture.VerifyUnchanged(before);
    }

    [Fact]
    public async Task LaboratoryHostRejectsBothDiversityOperations()
    {
        using var fixture = new AutomationHostTests.Fixture();
        var ai = new ConfiguredAuditor(null) { Availability = new(true, "gemini-test") };
        await using var host = new AutomationHost(fixture.Control, [fixture.A.Context], fixture.Options, ai);
        await host.Started;
        var bridge = new AutomationDesktopBridge(host, [fixture.A.Context], Path.Combine(fixture.Root, "desktop-requests"), ai);
        var connection = Fixture.Connection();
        await using var server = new AutomationPipeServer(connection, bridge.HandleAsync);
        var ipc = new AutomationHostClient(connection);
        await Rejected(AutomationError.Disabled, () => ipc.CallAsync(AutomationOperation.DiversityInventory, fixture.A.Context.Id.Value, new { }));
        await Rejected(AutomationError.Disabled, () => ipc.CallAsync(AutomationOperation.DiversityAsk, fixture.A.Context.Id.Value, new CatalogDiversityQuestion(Question)));
    }

    [VisualExplorerWindowsFact]
    public async Task LiveGeminiDiversity()
    {
        using var fixture = new Fixture();
        var before = fixture.DatabaseBytes();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(100));
        var connection = Fixture.Connection();
        var config = new AutomationHostLaunchConfiguration(1, fixture.Root, fixture.Control, connection,
            fixture.Contexts.Select(c => new AutomationUniverseDefinition(c.Storage, UniverseProfileSnapshotRenderer.Render(c.Profile))).ToArray(),
            PersonalMode: true);
        var launch = Path.Combine(fixture.Root, "launch.json");
        File.WriteAllText(launch, JsonSerializer.Serialize(config, AutomationProtocol.Json));
        var repository = AutomationHostProcessFixture.Repository;
        var build = AutomationHostProcessFixture.BuildConfiguration;
        var host = Path.Combine(repository, "src/NAP.AutomationHost/bin", build, "net10.0/NAP.AutomationHost.dll");
        using var hostProcess = Start(host, "--personal-config", launch);
        var ipc = new AutomationHostClient(connection);
        try
        {
            Assert.StartsWith("NAP personal host ready: ", await hostProcess.StandardOutput.ReadLineAsync(timeout.Token));
            var inventory = AutomationProtocol.Read<CatalogDiversityInventory>(await ipc.CallAsync(
                AutomationOperation.DiversityInventory, fixture.Nimroel.Id.Value, new { }, timeout.Token));
            Assert.True(inventory.AiAvailable, "La configuración de Gemini real no está disponible para el Host personal.");
            AssertReport(inventory.Report);

            var app = Path.Combine(repository, "src/NAP.App/bin", build, "net10.0-windows/NAP.App.dll");
            using var window = Start(app, "--automation-config=" + launch, "--smoke-automation");
            var stdout = window.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = window.StandardError.ReadToEndAsync(timeout.Token);
            try { await window.WaitForExitAsync(timeout.Token); }
            finally { if (!window.HasExited) window.Kill(); }
            Assert.True(window.ExitCode == 0, await stderr);
            Assert.Contains("\"diversityAssets\":9", await stdout, StringComparison.Ordinal);
            Assert.Contains("\"diversityEnabled\":true", await stdout, StringComparison.Ordinal);

            var reply = AutomationProtocol.Read<CatalogDiversityReply>(await ipc.CallAsync(
                AutomationOperation.DiversityAsk, fixture.Nimroel.Id.Value, new CatalogDiversityQuestion(Question), timeout.Token));
            AssertBreakdown(reply.Answer!);
            fixture.VerifyUnchanged(before);
            Console.WriteLine("Gemini real por IPC y WPF personal: 9 recursos (2 nativos, 7 históricos), pescadores de Treskal: female=1, male=2. SQLite intacto.");
        }
        finally
        {
            if (!hostProcess.HasExited)
            {
                using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                try { await ipc.CallAsync(AutomationOperation.ShutdownHost, null, new { }, shutdown.Token); }
                finally { if (!hostProcess.WaitForExit(3000)) hostProcess.Kill(); }
            }
        }
    }

    private static void AssertReport(CatalogDiversityReport report)
    {
        Assert.Equal(new UniverseId("nimroel"), report.UniverseId);
        Assert.Equal(2, report.NativeAssets);
        Assert.Equal(7, report.HistoricalAssets);
        Assert.Equal(9, report.TotalAssets);
        Assert.Contains(report.Gaps, g => g.Dimension == "localidad" && g.Value == "puerto" && g.Count == 1);
        Assert.Contains(report.Evidence, e => e.Dimension == Breakdown && e.Value == "treskal | fisher | female" && e.Count == 1 && e.Population == 3);
        Assert.Contains(report.Evidence, e => e.Dimension == Breakdown && e.Value == "treskal | fisher | male" && e.Count == 2 && e.Population == 3);
        Assert.Contains(report.Evidence, e => e.Dimension == Breakdown && e.Value == "puerto | fisher | female" && e.Count == 1 && e.Population == 1);
    }

    private static void AssertBreakdown(CatalogDiversityAnswer answer)
    {
        Assert.NotNull(answer);
        var facts = answer.Observations.SelectMany(o => o.Evidence).ToArray();
        Assert.Contains(facts, e => e.Dimension == Breakdown && e.Value == "treskal | fisher | female" && e.Count == 1);
        Assert.Contains(facts, e => e.Dimension == Breakdown && e.Value == "treskal | fisher | male" && e.Count == 2);
    }

    private static async Task Rejected(AutomationError code, Func<Task> call) =>
        Assert.Equal(code, (await Assert.ThrowsAsync<AutomationException>(call)).Code);

    private static Process Start(string dll, params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
            { CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(dll);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return Process.Start(start)!;
    }

    private sealed class ConfiguredAuditor(ICatalogDiversityAiClient? client) : IIdentifiedAiAuditClient, IAiAuditAvailability, ICatalogDiversityAiClient
    {
        public string ModelId => "gemini-test";
        public string ProviderId => "fixture";
        public AiAuditAvailability Availability { get; set; } = new(false, null);
        public Task<AiAuditReport> AuditAsync(AiAuditRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CatalogDiversityAnswer> AskAsync(CatalogDiversityReport report, string question, CancellationToken cancellation = default) =>
            client!.AskAsync(report, question, cancellation);
    }

    private sealed class SimulatedGemini : HttpMessageHandler
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Calls { get; private set; }
        internal bool InvalidAnswer { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal("TEST_GEMINI_KEY", Assert.Single(request.Headers.GetValues("x-goog-api-key")));
            using var outer = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var root = outer.RootElement;
            Assert.False(root.GetProperty("store").GetBoolean());
            using var payload = JsonDocument.Parse(root.GetProperty("input").GetString()!);
            Assert.Equal(Question, payload.RootElement.GetProperty("question").GetString());
            var report = payload.RootElement.GetProperty("inventory");
            Assert.Equal(9, report.GetProperty("total_assets").GetInt32());
            Assert.DoesNotContain(PrivateMarker, report.GetRawText(), StringComparison.Ordinal);
            Assert.DoesNotContain(Fixture.DuplicateId, report.GetRawText(), StringComparison.Ordinal);
            var ids = report.GetProperty("evidence").EnumerateArray()
                .Where(e => e.GetProperty("dimension").GetString() == Breakdown &&
                    e.GetProperty("value").GetString()!.StartsWith("treskal | fisher | ", StringComparison.Ordinal))
                .Select(e => e.GetProperty("id").GetString()).ToArray();
            Assert.Equal(2, ids.Length);
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            var model = JsonSerializer.Serialize(new { summary = "Hay pescadores registrados en Treskal.", observations = new[]
            { new { comment = "Este es el reparto por sexo.", evidence_ids = InvalidAnswer ? new[] { "inventado" } : ids } } });
            var response = JsonSerializer.Serialize(new
            {
                status = "completed", steps = new[] { new { type = "model_output", content = new[] { new { type = "text", text = model } } } }
            });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class NoFolders : IFolderPicker { public string? Pick(string title, string current) => null; }
    private sealed class NoConfirmation : IUserConfirmation
    { public Task<bool> ConfirmAsync(UiConfirmation confirmation, CancellationToken cancellation) => Task.FromResult(false); }

    private sealed class Fixture : IDisposable
    {
        internal const string DuplicateId = "portrait_treskal_fisher_female_901";
        private readonly string _directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NAP-diversity-check-" + Guid.NewGuid().ToString("N"));
        internal string Root => Path.Combine(_directory, "host");
        internal string Control => Path.Combine(Root, "control");
        internal UniverseContext Nimroel { get; }
        internal UniverseContext Other { get; }
        internal UniverseContext[] Contexts => [Nimroel, Other];

        internal Fixture()
        {
            var profile = UniverseProfileLoader.Load(UniverseProfileLoaderTests.ConfigPath);
            Nimroel = Context(profile);
            Other = Context(new UniverseProfile(new("otro_universo"), "Otro universo"));
            Directory.CreateDirectory(Control);
            try { Seed(); }
            catch { Dispose(); throw; }
        }
        private UniverseContext Context(UniverseProfile profile)
        {
            var root = Path.Combine(_directory, profile.Id.Value);
            var roots = new UniverseStorageConfig(profile.Id, Path.Combine(root, "workspace"), Path.Combine(root, "production"), Path.Combine(root, "archive"));
            foreach (var path in new[] { roots.WorkspaceRoot, roots.StateRoot, roots.ProductionRoot, roots.ArchiveRoot, roots.InboxRoot, roots.StagingRoot })
                Directory.CreateDirectory(path);
            return new(profile, roots);
        }
        internal AutomationHost Start(IIdentifiedAiAuditClient ai) =>
            new(Control, Contexts, new() { LaboratoryRoot = Root, PersonalMode = true }, ai);

        internal static AutomationConnection Connection() =>
            new("nap-diversity-" + Guid.NewGuid().ToString("N"), Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

        private void Seed()
        {
            var assets = Path.Combine(_directory, "legacy");
            Legacy(DuplicateId, "treskal", "female", "fisher");
            Legacy("portrait_treskal_fisher_male_902", "treskal", "male", "fisher");
            Legacy("portrait_treskal_fisher_male_903", "treskal", "male", "fisher");
            Legacy("portrait_puerto_fisher_female_904", "puerto", "female", "fisher");
            for (var i = 910; i <= 913; i++) Legacy($"portrait_treskal_farmer_male_{i}", "treskal", "male", "farmer");

            var catalog = new AssetCatalog(Nimroel);
            catalog.Initialize();
            Assert.Equal(8, new LegacyPortraitIndex(Nimroel).Import(assets));
            Native(DuplicateId, "fisher", "female");
            Native("portrait_treskal_farmer_male_914", "farmer", "male");

            void Legacy(string id, string location, string sex, string role)
            {
                var dir = Directory.CreateDirectory(Path.Combine(assets, "portraits", "norgard", "treskal", role, sex, id)).FullName;
                using var webp = new Image<Rgba32>(8, 10);
                webp.Save(Path.Combine(dir, id + ".webp"), new WebpEncoder());
                File.WriteAllText(Path.Combine(dir, id + "_prompt.md"), PrivateMarker);
                File.WriteAllText(Path.Combine(dir, id + "_info.md"), PrivateMarker);
                File.WriteAllText(Path.Combine(dir, id + "_visual_identity.json"), JsonSerializer.Serialize(new
                { asset_id = id, identity = new { culture = "norgard", location, profession = role, sex }, arbitrary = PrivateMarker }));
            }

            void Native(string id, string role, string sex)
            {
                using var source = new PackageSemanticTestFixture(Nimroel.Profile, id);
                source.Manifest = source.Manifest with
                { Classification = new() { ["culture"] = "norgard", ["location"] = "treskal", ["role"] = role, ["sex"] = sex } };
                source.WriteManifest();
                File.WriteAllBytes(Path.Combine(source.PackageRoot, id + ".png"), PreparationTestFixture.Png(8, 10, 17));
                File.WriteAllText(Path.Combine(source.PackageRoot, id + "_visual_identity.json"), JsonSerializer.Serialize(new { arbitrary = PrivateMarker }));
                var package = new PackageSemanticValidator().Validate(source.PackageRoot, Nimroel).Package;
                Assert.NotNull(package);
                var repository = new ProductionRepositoryValidator().Validate(Nimroel).Repository!;
                var plan = new ProcessingPlanBuilder().Build(package, repository, new ProductionDestinationResolver().Resolve(package, repository));
                var archive = new ArchiveMasterPlanner(Nimroel).Plan(package, plan);
                var job = JobId.Create();
                var journal = new JobStateStore(Nimroel);
                journal.Create(job);
                journal.Transition(job, JobState.Staged);
                journal.Transition(job, JobState.Validated);
                journal.Transition(job, JobState.Planned);
                new AssetExecutionCoordinator(Nimroel).Execute(job, package, plan, archive, ProductionTestFixture.Pass, 2_000_000);
            }
        }

        private string[] Databases => [Nimroel.Storage.CatalogPath, new LegacyPortraitIndex(Nimroel).DatabasePath];
        internal byte[][] DatabaseBytes() => Databases.Select(File.ReadAllBytes).ToArray();
        internal void VerifyUnchanged(byte[][] before)
        {
            Assert.Equal(before[0], File.ReadAllBytes(Databases[0]));
            Assert.Equal(before[1], File.ReadAllBytes(Databases[1]));
            Assert.False(File.Exists(Other.Storage.CatalogPath));
        }
        public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    }
}

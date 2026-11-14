using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using NAP.AI;
using NAP.Core;
using NAP.Presentation;
using Xunit;

namespace NAP.Tests;

public sealed class NapPersonalRepairTests
{
    [Fact]
    public async Task DefaultStorageAllowanceReachesAuditAndRetainsNamedInboxReceipt()
    {
        using var f = new AutomationHostTests.Fixture();
        StoredAutomationPolicy policy;
        using (var owner = f.A.Queue.AcquireOwner())
        {
            var definition = AutomationPolicyDefinition.DisabledDefault(f.A.Roots) with
            { PolicyId = "repair_check", Operations = StoredAutomationPolicy.ExecutionOperations, Audit = new(1, 1, 60, 0m) };
            policy = f.A.Queue.SavePolicy(owner, definition);
            policy = f.A.Queue.RecordAuthorization(owner, policy.Reference, policy.Revision,
                new("repair_authorization", f.A.Context.Id, policy.Reference, "repair_check", DateTimeOffset.UtcNow));
            policy = f.A.Queue.SetPolicyState(owner, policy.Reference, policy.Revision, AutomationPolicyState.Enabled);
        }
        var common = Path.Combine(f.Root, "shared"); Directory.CreateDirectory(common);
        var original = Path.Combine(common, "mi-retrato.zip"); File.Move(f.A.Zip(), original);
        var hash = SHA256.HashData(File.ReadAllBytes(original));
        await using var host = new AutomationHost(f.Control, [f.A.Context], f.Options with { MixedInboxRoot = common });
        await host.Started; await host.ActivateAsync(f.A.Context.Id, policy.Reference, false);
        await AutomationHostTests.Until(() => host.Snapshot().Universes.Any(u => u.Stage == "audit-unconfigured"), host);
        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(original)));
        var overview = await host.QueryAsync(f.A.Context.Id, s => s.Queue.DesktopOverview(s.Context, []));
        Assert.Equal("Planned", Assert.Single(overview.Recent!).Preparation);
        var receipt = Assert.Single(host.Snapshot().MixedInbox!.Receipts!);
        Assert.Equal("mi-retrato.zip", receipt.FileName);
        Assert.Equal(f.A.Context.Id.Value, receipt.Universe);
        Assert.True(File.Exists(Path.Combine(f.Root, "inbox-receipts.json")));
        Assert.False(host.Snapshot().Audit!.Configured);
    }

    [Fact]
    public void ArchivedIncidentLeavesPendingViewWithoutDeletingHistory()
    {
        using var f = new PreparationTestFixture();
        QueueItem item;
        using (var owner = f.Queue.AcquireOwner())
        {
            item = f.Queue.Observe(owner, f.Context.Id, Path.Combine(f.Roots.InboxRoot, "antiguo.zip"), new(new Sha256Digest(new string('a', 64)), 10));
            item = f.Queue.Transition(owner, item.Id, item.Revision, QueueState.NeedsReview, QueueIncident.ProfileUnknown);
        }
        Assert.False(Assert.Single(f.Queue.Overview().Review).SourcePresent);
        Assert.Equal(0, f.Queue.Overview([item.Id.Value]).Counts.NeedsReview);
        Assert.Equal(1, f.Queue.Overview([item.Id.Value]).ArchivedReview);
        Assert.Empty(f.Queue.Browse(new(Filter: WorkflowFilter.Review), [item.Id.Value]).Items);
        Assert.Single(f.Queue.Browse(new(Filter: WorkflowFilter.Archived), [item.Id.Value]).Items);
        Assert.Equal(item, f.Queue.Get(item.Id));
        Assert.NotEmpty(f.Queue.Events(item.Id));
        Assert.Equal(1, f.Queue.Overview().Counts.NeedsReview);
    }

    [VisualExplorerWindowsFact]
    public void GeminiKeyIsEncryptedForCurrentUser()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "nap-gemini-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "gemini.json"); var settings = new PersonalGeminiSettings(path);
            settings.Save("private-check-key", "check-model", true);
            Assert.DoesNotContain("private-check-key", File.ReadAllText(path));
            Assert.Equal(("private-check-key", "check-model"), settings.Load()!.Value);
            Assert.Throws<InvalidDataException>(() => settings.Save("private-check-key", "check-model", false));
        }
        finally { Directory.Delete(root, true); }
    }

    [VisualExplorerWindowsFact]
    public async Task PersonalActivationRegistersGeminiTermsAndCompletesOneIsolatedPackage()
    {
        await PersonalCircuit(new DurableExecutionTests.AuditClient(), realGemini: false);
    }

    // Explicitly selected only when private credentials are supplied. Never a synthetic fallback or a silent skip.
    [VisualExplorerWindowsFact]
    public async Task LiveGeminiPersonalAcceptance()
    {
        using var http = new HttpClient();
        var auditor = HostGeminiAuditor.Create(http, Environment.GetEnvironmentVariable("NAP_GEMINI_API_KEY"), Environment.GetEnvironmentVariable("NAP_GEMINI_MODEL"));
        Assert.True(auditor is not null, "Real Gemini credentials are required for final acceptance.");
        await PersonalCircuit(auditor!, realGemini: true);
    }

    private static async Task PersonalCircuit(IIdentifiedAiAuditClient auditor, bool realGemini)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NAP-repair-check-" + Guid.NewGuid().ToString("N"));
        var hostRoot = Path.Combine(root, "host"); var control = Path.Combine(hostRoot, "control");
        var shared = Path.Combine(root, "entrada"); Directory.CreateDirectory(control); Directory.CreateDirectory(shared);
        try
        {
            // Real installed profile and geometry; all output roots belong to this isolated check.
            var roots = new UniverseStorageConfig(new("nimroel"), Path.Combine(root, "workspace"), Path.Combine(root, "production"), Path.Combine(root, "archive"));
            foreach (var path in new[] { roots.WorkspaceRoot, roots.InboxRoot, roots.StagingRoot, roots.ProductionRoot, roots.ArchiveRoot }) Directory.CreateDirectory(path);
            var profile = UniverseProfileLoader.Load(Path.Combine(AppContext.BaseDirectory, "config", "universes", "nimroel", "profile.json"));
            var context = new UniverseContext(profile, roots);
            const string asset = "portrait_treskal_farmer_male_901";
            var input = Path.Combine(root, asset + ".zip");
            using (var file = File.Create(input))
            using (var zip = new System.IO.Compression.ZipArchive(file, System.IO.Compression.ZipArchiveMode.Create))
            {
                Add(asset + ".png", PreparationTestFixture.Png(768, 960, 901));
                Add(asset + "_manifest.json", System.Text.Encoding.UTF8.GetBytes("""
                    {"schema_version":2,"universe_id":"nimroel","asset_id":"portrait_treskal_farmer_male_901","asset_type":"portrait","production_profile":"portrait_npc","classification":{"culture":"norgard","location":"treskal","role":"farmer","sex":"male"}}
                    """));
                Add(asset + "_prompt.md", System.Text.Encoding.UTF8.GetBytes("Muestra técnica aislada. Retrato ficticio; no canónico."));
                Add(asset + "_info.md", System.Text.Encoding.UTF8.GetBytes("Comprobación funcional personal; no publicar en el videojuego."));
                Add(asset + "_visual_identity.json", System.Text.Encoding.UTF8.GetBytes("{\"purpose\":\"isolated functional example, not canon\"}"));
                void Add(string name, byte[] bytes) { using var entry = zip.CreateEntry(name).Open(); entry.Write(bytes); }
            }
            var original = SHA256.HashData(File.ReadAllBytes(input));
            await using var session = await PersonalSession.Start(context, hostRoot, control, shared, auditor, realGemini);
            File.Move(input, Path.Combine(shared, asset + ".zip")); input = Path.Combine(shared, asset + ".zip");
            // Deposit after activation. No ScanPending call: the scheduler must receive it automatically.
            var timer = Stopwatch.StartNew();
            AutomationQueueOverview overview;
            do
            {
                overview = (await session.ReadAsync(context.Id, CancellationToken.None)).Queue;
                Assert.True(overview.Counts.NeedsReview == 0, JsonSerializer.Serialize(overview.Recent));
                Assert.True(timer.Elapsed < TimeSpan.FromSeconds(120), "The personal circuit did not finish within 120 seconds.");
                if (overview.Counts.Completed == 1) break;
                await Task.Delay(100);
            } while (true);
            var recent = Assert.Single(overview.Recent!);
            Assert.Equal(QueueState.Completed, recent.Item.State);
            Assert.NotNull(recent.AuditSummary); Assert.Equal(auditor.ModelId, recent.AuditModel);
            Assert.NotNull(new CatalogExplorerReader(context).Detail(asset));
            await session.VerifyThumbnail();
            using (var webp = SixLabors.ImageSharp.Image.Load(Assert.Single(Directory.GetFiles(roots.ProductionRoot, "*.webp", SearchOption.AllDirectories))))
            { Assert.Equal(768, webp.Width); Assert.Equal(960, webp.Height); }
            var master = Assert.Single(Directory.GetFiles(roots.ArchiveRoot, "*.png", SearchOption.AllDirectories));
            Assert.Equal(SHA256.HashData(PreparationTestFixture.Png(768, 960, 901)), SHA256.HashData(File.ReadAllBytes(master)));
            Assert.Equal(original, SHA256.HashData(File.ReadAllBytes(input)));
            var model = new AutomationControlViewModel(() => context, new AlwaysConfirm(), session);
            await model.RefreshAsync();
            Assert.Equal("1", model.Completed); Assert.Contains(model.Recent, r => r.Detail.Contains("Completado") && r.Detail.Contains("Gemini"));
            Assert.Single(model.InboxReceipts); model.Dispose();
            if (realGemini)
            {
                await session.RenderDesktop(recent);
                Console.WriteLine("REAL_GEMINI_ACCEPTANCE: automatic ZIP -> nimroel/portrait_npc -> real Gemini PASS -> WebP 768x960 -> original master hash preserved -> SQLite catalog + thumbnail + desktop result verified. Isolated roots only.");
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(root, true);
            }
        }
    }
    private sealed class AlwaysConfirm : IUserConfirmation
    { public Task<bool> ConfirmAsync(UiConfirmation request, CancellationToken cancellation = default) => Task.FromResult(true); }
    private sealed class PersonalSession(UniverseContext context) : IAutomationOverviewClient, IAsyncDisposable
    {
        private AutomationHost? _host;
        private AutomationHostClient? _client;
        private Process? _child;
        private string? _launch;
        internal static async Task<PersonalSession> Start(UniverseContext context, string hostRoot, string control, string shared, IIdentifiedAiAuditClient auditor, bool native)
        {
            var session = new PersonalSession(context);
            try
            {
                var admission = PreparationTestFixture.Fast with { RescanInterval = TimeSpan.FromSeconds(1) };
                if (native)
                {
                    var config = new AutomationHostLaunchConfiguration(1, hostRoot, control,
                        new("nap-live-" + Guid.NewGuid().ToString("N"), Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))),
                        [new(context.Storage, UniverseProfileSnapshotRenderer.Render(context.Profile))], Admission: admission,
                        MixedInboxRoot: shared, PersonalMode: true);
                    session._launch = Path.Combine(hostRoot, "launch.json");
                    File.WriteAllText(session._launch, JsonSerializer.Serialize(config, AutomationProtocol.Json));
                    var executable = Path.Combine(AutomationHostProcessFixture.Repository, "src/NAP.AutomationHost/bin", AutomationHostProcessFixture.BuildConfiguration, "net10.0/NAP.AutomationHost.dll");
                    var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                    foreach (var argument in new[] { executable, "--personal-config", session._launch }) start.ArgumentList.Add(argument);
                    session._child = Process.Start(start)!;
                    var line = await session._child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20));
                    Assert.StartsWith("NAP personal host ready: ", line);
                    session._client = new(config.Connection);
                    await session._client.CallAsync(AutomationOperation.ActivatePersonal, context.Id.Value, new { });
                }
                else
                {
                    session._host = new AutomationHost(control, [context], new() { LaboratoryRoot = hostRoot, PersonalMode = true, MixedInboxRoot = shared, Admission = admission }, auditor);
                    await session._host.Started; await session._host.ActivatePersonalAsync(context.Id);
                }
                return session;
            }
            catch { await session.DisposeAsync(); throw; }
        }
        public async Task<AutomationDesktopOverview> ReadAsync(UniverseId universe, CancellationToken cancellation) => _client is not null
            ? AutomationProtocol.Read<AutomationDesktopOverview>(await _client.CallAsync(AutomationOperation.Overview, universe.Value, new { }, cancellation))
            : new(_host!.Snapshot(), await _host.QueryAsync(universe, s => s.Queue.DesktopOverview(context, []), cancellation), _host.ReadControl(universe));
        public Task SendAsync(AutomationOperation op, UniverseId? universe, object payload, CancellationToken cancellation) => throw new NotSupportedException();
        internal async Task VerifyThumbnail()
        {
            var asset = Assert.Single(new CatalogExplorerReader(context).Page().Items);
            if (_client is not null)
            {
                var result = AutomationProtocol.Read<AutomationReadResult>(await _client.CallAsync(AutomationOperation.Thumbnail, context.Id.Value, asset));
                var thumbnail = AutomationProtocol.Read<AutomationThumbnailView>(result.Value);
                Assert.NotEmpty(thumbnail.Bytes);
            }
            else
            {
                var thumbnail = await _host!.QueryAsync(context.Id, scope => new ProductionThumbnailCache(context).GetOwnedAsync(scope, asset).GetAwaiter().GetResult());
                Assert.True(File.Exists(thumbnail.CachePath));
            }
        }
        internal async Task RenderDesktop(AutomationRecentItem recent)
        {
            var output = Path.Combine(AutomationHostProcessFixture.Repository, "artifacts", "nap-personal-acceptance"); Directory.CreateDirectory(output);
            var app = Path.Combine(AutomationHostProcessFixture.Repository, "src/NAP.App/bin", AutomationHostProcessFixture.BuildConfiguration, "net10.0-windows/NAP.App.dll");
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { app, "--automation-config=" + _launch, "--smoke-automation", "--smoke-output=" + output }) start.ArgumentList.Add(argument);
            using var window = Process.Start(start)!;
            var stdout = window.StandardOutput.ReadToEndAsync(); var stderr = window.StandardError.ReadToEndAsync();
            try { await window.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60)); }
            finally { if (!window.HasExited) window.Kill(entireProcessTree: true); }
            Assert.True(window.ExitCode == 0, await stderr);
            Assert.Contains("\"automationConnected\":true", await stdout);
            File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { realGemini = true, model = recent.AuditModel,
                decision = "PASS", summary = recent.AuditSummary, automaticReception = true, webpWidth = 768, webpHeight = 960,
                originalZipPreserved = true, originalMasterHashPreserved = true, sqliteCatalog = true, thumbnail = true, wpfRendered = true,
                isolatedRoots = true, checkedUtc = DateTimeOffset.UtcNow }, new JsonSerializerOptions { WriteIndented = true }));
        }
        public async ValueTask DisposeAsync()
        {
            if (_host is not null) await _host.DisposeAsync();
            if (_child is not null)
            {
                if (!_child.HasExited)
                {
                    try { if (_client is not null) await _client.CallAsync(AutomationOperation.ShutdownHost, null, new { }); }
                    finally { if (!await WaitForExit(_child)) _child.Kill(entireProcessTree: true); }
                }
                await _child.WaitForExitAsync(); _child.Dispose();
            }
        }
        private static async Task<bool> WaitForExit(Process process)
        {
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); return true; }
            catch (TimeoutException) { return false; }
        }
    }
}

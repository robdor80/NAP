using System.Text.Json;
using NAP.Core;
using NAP.AI;
using NAP.Presentation;

if (args.Length != 2 || args[0] is not ("--laboratory-config" or "--personal-config"))
{ Console.Error.WriteLine("Use --laboratory-config or --personal-config with an explicit configuration."); return 2; }
try
{
    var info = new FileInfo(args[1]); if (info.Length > 2 * 1024 * 1024) throw new InvalidDataException("Launch configuration exceeds its bound.");
    using var document = JsonDocument.Parse(File.ReadAllBytes(info.FullName));
    var config = AutomationProtocol.Read<AutomationHostLaunchConfiguration>(document.RootElement); var contexts = config.Contexts();
    var personal = args[0] == "--personal-config";
    if (config.PersonalMode != personal)
        throw new InvalidDataException("Host execution mode and launch configuration disagree.");
    // Credentials are supplied privately at runtime; neither launch.json nor GitHub stores them.
    // No configured Gemini means no automatic PASS: the host parks at the audit boundary.
    using var auditTransport = new HttpClient();
    var gemini = HostGeminiAuditor.Create(auditTransport,
        Environment.GetEnvironmentVariable("NAP_GEMINI_API_KEY"),
        Environment.GetEnvironmentVariable("NAP_GEMINI_MODEL"), config.SyntheticAudit);
    IIdentifiedAiAuditClient? auditor = config.SyntheticAudit ? new SyntheticAuditor() : gemini;
    var checkpointReached = 0;
    var storageFailures = 0;
    await using var host = new AutomationHost(config.ControlRoot, contexts, new() { LaboratoryRoot = config.LaboratoryRoot, PersonalMode = personal,
        Admission = config.Admission ?? new(), MixedInboxRoot = config.MixedInboxRoot,
        LaboratoryStorageFailure = (universe, item, error) =>
        {
            // This executable validates temporary synthetic laboratory roots before starting.
            // Keep the original stack/HResult here, never in a public IPC projection or receipt.
            if (Interlocked.Increment(ref storageFailures) > 16) return;
            var detail = error.ToString();
            try { Console.Error.WriteLine($"preparation_storage_unavailable:{universe.Value}:{item.Value}:0x{error.HResult:X8}:{detail[..Math.Min(detail.Length, 65536)]}"); }
            catch (IOException) { /* A broken diagnostic stream must not prevent durable parking. */ }
        },
        LaboratoryCheckpoint = (_, item, boundary) =>
        {
            if (boundary != config.LaboratoryCheckpoint || Interlocked.Exchange(ref checkpointReached, 1) != 0) return;
            // Only an explicitly configured TEMP laboratory can reach this injection. The fixture kills
            // this process after observing the durable boundary; this is never a shutdown mechanism.
            Console.WriteLine("NAP laboratory checkpoint: " + item.Value + ":" + boundary);
            Console.Out.Flush();
            Thread.Sleep(Timeout.Infinite);
        } }, auditor);
    await host.Started;
    var bridge = new AutomationDesktopBridge(host, contexts, Path.Combine(config.LaboratoryRoot, "desktop-requests"), auditor);
    await using var server = new AutomationPipeServer(config.Connection, bridge.HandleAsync);
    using var stop = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
    Console.WriteLine((personal ? "NAP personal host ready: " : "NAP laboratory host ready: ") + host.SessionId);
    var signal = Task.Delay(Timeout.Infinite, stop.Token);
    var finished = await Task.WhenAny(signal, host.Completion);
    if (finished == host.Completion) await host.Completion;
    return 0;
}
catch (Exception error)
{
    // A personal desktop tool needs a useful local startup error, not a generic
    // exception type that conceals the reason. Avoid logging configuration bytes,
    // IPC keys or API credentials.
    var diagnostic = error.GetBaseException();
    Console.Error.WriteLine("host_stopped:" + diagnostic.GetType().Name + ":" + diagnostic.Message);
    return 1;
}

internal sealed class SyntheticAuditor : IIdentifiedAiAuditClient
{
    public string ProviderId => "synthetic_provider";
    public string ModelId => "synthetic_model";
    public Task<AiAuditReport> AuditAsync(AiAuditRequest request, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(new AiAuditReport(AiAuditDecision.Pass, "Explicit synthetic laboratory auditor", [])); }
}

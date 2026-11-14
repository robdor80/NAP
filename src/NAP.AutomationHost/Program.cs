using System.Text.Json;
using NAP.Core;
using NAP.AI;
using NAP.Presentation;

// Production is deliberately not the default mode. It cannot be reached through
// the interactive WPF launcher, nor by repointing a laboratory launch JSON.
if (args.Length != 2 || args[0] is not ("--laboratory-config" or "--production-config"))
{
    Console.Error.WriteLine("An explicit --laboratory-config or --production-config file is required.");
    return 2;
}
try
{
    var laboratoryMode = args[0] == "--laboratory-config";
    if (!laboratoryMode && !OperatingSystem.IsWindows())
        throw new PlatformNotSupportedException("Protected production Host requires Windows.");
    var info = new FileInfo(Path.GetFullPath(args[1]));
    if (!info.Exists || info.Length is < 1 or > 2 * 1024 * 1024 ||
        (info.Attributes & FileAttributes.ReparsePoint) != 0)
        throw new InvalidDataException("Host configuration is missing, linked or outside the size limit.");
    using var document = JsonDocument.Parse(File.ReadAllBytes(info.FullName));
    var lab = laboratoryMode ? AutomationProtocol.Read<AutomationHostLaunchConfiguration>(document.RootElement) : null;
    var production = laboratoryMode ? null : AutomationProtocol.Read<AutomationProductionLaunchConfiguration>(document.RootElement);
    if (lab?.ProtectedEndpoint is not null)
        throw new InvalidDataException("A protected Windows writer cannot start from a laboratory configuration.");

    var contexts = lab is null ? production!.Contexts() : lab.Contexts();
    var synthetic = lab?.SyntheticAudit ?? false;
    // No audit credentials are stored in launch.json or published to GitHub.
    // Missing Gemini credentials remain an audit boundary, NEVER an automatic PASS.
    using var auditTransport = new HttpClient();
    var gemini = HostGeminiAuditor.Create(auditTransport,
        Environment.GetEnvironmentVariable("NAP_GEMINI_API_KEY"),
        Environment.GetEnvironmentVariable("NAP_GEMINI_MODEL"), synthetic);
    IIdentifiedAiAuditClient? auditor = synthetic ? new SyntheticAuditor() : gemini;

    var checkpointReached = 0;
    var storageFailures = 0;
    // Production checks Windows SID, protected distribution/configuration, all data
    // roots, ACLs and a pinned backup receipt BEFORE constructing the first writer.
    // Only a separately installed writer account can reach the protected branch.
    await using var host = laboratoryMode
        ? new AutomationHost(lab!.ControlRoot, contexts, new()
        {
            LaboratoryRoot = lab.LaboratoryRoot,
            Admission = lab.Admission ?? new(),
            MixedInboxRoot = lab.MixedInboxRoot,
            LaboratoryStorageFailure = (universe, item, error) =>
            {
                if (Interlocked.Increment(ref storageFailures) > 16) return;
                var detail = error.ToString();
                try { Console.Error.WriteLine($"preparation_storage_unavailable:{universe.Value}:{item.Value}:0x{error.HResult:X8}:{detail[..Math.Min(detail.Length, 65536)]}"); }
                catch (IOException) { }
            },
            LaboratoryCheckpoint = (_, item, boundary) =>
            {
                if (boundary != lab.LaboratoryCheckpoint || Interlocked.Exchange(ref checkpointReached, 1) != 0) return;
                Console.WriteLine("NAP laboratory checkpoint: " + item.Value + ":" + boundary);
                Console.Out.Flush();
                Thread.Sleep(Timeout.Infinite);
            }
        }, auditor)
        : production!.CreateHost(info.FullName,
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..")), auditor);

    await host.Started;
    var root = lab?.LaboratoryRoot ?? production!.DataRoot;
    var connection = lab?.Connection ?? production!.Connection;
    AutomationWindowsEndpointAccess? endpoint = null;
    if (production is not null)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Protected pipe requires Windows.");
        endpoint = new AutomationWindowsEndpointAccess(production.WriterSid, production.DesktopSid);
    }
    var bridge = new AutomationDesktopBridge(host, contexts, Path.Combine(root, "desktop-requests"), auditor);
    await using var server = new AutomationPipeServer(connection, bridge.HandleAsync, endpoint);

    using var stop = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
    Console.WriteLine((laboratoryMode ? "NAP laboratory host ready: " : "NAP protected host ready: ") + host.SessionId);
    var signal = Task.Delay(Timeout.Infinite, stop.Token);
    var finished = await Task.WhenAny(signal, host.Completion);
    if (finished == host.Completion) await host.Completion;
    return 0;
}
catch (Exception error)
{
    // No secret, filesystem path or received configuration content is printed.
    Console.Error.WriteLine("host_stopped:" + error.GetType().Name);
    return 1;
}

internal sealed class SyntheticAuditor : IIdentifiedAiAuditClient
{
    public string ProviderId => "synthetic_provider";
    public string ModelId => "synthetic_model";
    public Task<AiAuditReport> AuditAsync(AiAuditRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new AiAuditReport(AiAuditDecision.Pass, "Explicit synthetic laboratory auditor", []));
    }
}

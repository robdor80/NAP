using System.Text.Json;
using NAP.Core;
using NAP.Presentation;

if (args.Length != 2 || args[0] != "--laboratory-config")
{ Console.Error.WriteLine("An explicit --laboratory-config file is required. No production/service mode is available."); return 2; }
try
{
    var info = new FileInfo(args[1]); if (info.Length > 2 * 1024 * 1024) throw new InvalidDataException("Launch configuration exceeds its bound.");
    using var document = JsonDocument.Parse(File.ReadAllBytes(info.FullName));
    var config = AutomationProtocol.Read<AutomationHostLaunchConfiguration>(document.RootElement); var contexts = config.Contexts();
    var auditor = config.SyntheticAudit ? new SyntheticAuditor() : null;
    await using var host = new AutomationHost(config.ControlRoot, contexts, new() { LaboratoryRoot = config.LaboratoryRoot }, auditor);
    await host.Started;
    var bridge = new AutomationDesktopBridge(host, contexts, Path.Combine(config.LaboratoryRoot, "desktop-requests"), auditor);
    await using var server = new AutomationPipeServer(config.Connection, bridge.HandleAsync);
    using var stop = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
    Console.WriteLine("NAP laboratory host ready: " + host.SessionId);
    var signal = Task.Delay(Timeout.Infinite, stop.Token);
    var finished = await Task.WhenAny(signal, host.Completion);
    if (finished == host.Completion) await host.Completion;
    return 0;
}
catch (Exception error) { Console.Error.WriteLine("host_stopped:" + error.GetType().Name); return 1; }

internal sealed class SyntheticAuditor : IIdentifiedAiAuditClient
{
    public string ProviderId => "synthetic_provider";
    public string ModelId => "synthetic_model";
    public Task<AiAuditReport> AuditAsync(AiAuditRequest request, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(new AiAuditReport(AiAuditDecision.Pass, "Explicit synthetic laboratory auditor", [])); }
}

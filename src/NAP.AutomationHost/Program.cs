using System.Text.Json;
using NAP.Core;

if (args.Length != 2 || args[0] != "--laboratory-config")
{ Console.Error.WriteLine("An explicit --laboratory-config file is required. No production/service mode is available."); return 2; }
try
{
    var info = new FileInfo(args[1]); if (info.Length > 2 * 1024 * 1024) throw new InvalidDataException("Launch configuration exceeds its bound.");
    using var document = JsonDocument.Parse(File.ReadAllBytes(info.FullName));
    var config = AutomationProtocol.Read<AutomationHostLaunchConfiguration>(document.RootElement); var contexts = config.Contexts();
    await using var host = new AutomationHost(config.ControlRoot, contexts, new() { LaboratoryRoot = config.LaboratoryRoot }, config.SyntheticAudit ? new SyntheticAuditor() : null);
    await host.Started;
    await using var server = new AutomationPipeServer(config.Connection, async (request, principal) =>
    {
        if (request.Operation == AutomationOperation.Status) return AutomationProtocol.Payload(host.Snapshot());
        var universe = new UniverseId(request.UniverseId ?? throw new InvalidDataException("Universe is required."));
        switch (request.Operation)
        {
            case AutomationOperation.Activate:
                var activation = AutomationProtocol.Read<AutomationActivation>(request.Payload);
                return AutomationProtocol.Payload(await host.ActivateAsync(universe, activation.Policy, activation.ResumeOnRestart, request.RequestId));
            case AutomationOperation.Batches:
                var page = AutomationProtocol.Read<AutomationPageRequest>(request.Payload);
                return AutomationProtocol.Payload(await host.BatchesAsync(universe, page.Offset, page.Limit));
            case AutomationOperation.QueuePage:
                var queuePage = AutomationProtocol.Read<AutomationPageRequest>(request.Payload);
                return AutomationProtocol.Payload(await host.QueryAsync(universe, s => s.Queue.List(queuePage.Offset, queuePage.Limit)));
            case AutomationOperation.Events:
                var eventItem = new QueueItemId(AutomationProtocol.Read<AutomationItemRequest>(request.Payload).ItemId);
                return AutomationProtocol.Payload(await host.QueryAsync(universe, s => s.Queue.Events(eventItem)));
            default:
                var action = request.Operation switch
                {
                    AutomationOperation.Pause => AutomationControlAction.Pause, AutomationOperation.Resume => AutomationControlAction.Resume,
                    AutomationOperation.StopUniverse => AutomationControlAction.Stop, AutomationOperation.CancelItem => AutomationControlAction.CancelItem,
                    AutomationOperation.ResumeItem => AutomationControlAction.ResumeItem, _ => throw new InvalidDataException("Unsupported operation.")
                };
                var item = action is AutomationControlAction.CancelItem or AutomationControlAction.ResumeItem
                    ? new QueueItemId(AutomationProtocol.Read<AutomationItemRequest>(request.Payload).ItemId) : null;
                return AutomationProtocol.Payload(host.Control(universe, action, item, request.RequestId));
        }
    });
    using var stop = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
    Console.WriteLine("NAP laboratory host ready: " + host.SessionId);
    try { await Task.Delay(Timeout.Infinite, stop.Token); } catch (OperationCanceledException) { }
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

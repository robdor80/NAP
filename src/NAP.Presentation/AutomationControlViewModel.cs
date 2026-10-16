using NAP.Core;

namespace NAP.Presentation;

public sealed class AutomationControlViewModel : ObservableObject
{
    private readonly AutomationHostClient _client;
    private readonly Func<UniverseContext?> _context;
    private readonly IUserConfirmation _confirmation;
    private string _status = "AutomationHost · conectando";
    private StoredAutomationPolicy? _policy;
    private AutomationHostControl? _control;
    public string Status { get => _status; private set => Set(ref _status, value); }
    public UiAsyncCommand Refresh { get; }
    public UiAsyncCommand Activate { get; }
    public UiAsyncCommand Pause { get; }
    public UiAsyncCommand Resume { get; }
    public UiAsyncCommand Stop { get; }
    public AutomationControlViewModel(AutomationHostClient client, Func<UniverseContext?> context, IUserConfirmation confirmation)
    {
        _client = client; _context = context; _confirmation = confirmation;
        void Failed(Exception error) => Status = "AutomationHost · " + (error is AutomationException a ? a.Code.ToString() : "desconectado o detenido; estado sin confirmar");
        Refresh = new(RefreshAsync, () => true, Failed);
        Activate = new(ActivateAsync, () => _context() is not null && _policy?.IsPreproductionAuthorized == true, Failed);
        Pause = new(() => Change(AutomationOperation.Pause), () => _context() is not null && _control is { Grant: not null, Paused: false, Stopped: false }, Failed);
        Resume = new(() => Change(AutomationOperation.Resume), () => _context() is not null && _policy?.IsPreproductionAuthorized == true && _control is { Grant: not null, Paused: true }, Failed);
        Stop = new(StopAsync, () => true, Failed);
    }
    public async Task RefreshAsync()
    {
        var value = await _client.CallAsync(AutomationOperation.Status, null, new { });
        var snapshot = AutomationProtocol.Read<AutomationHostSnapshot>(value);
        Status = "AutomationHost · " + snapshot.State + " · " + string.Join(" · ", snapshot.Universes.Select(s => s.UniverseId.Value + ": " + s.State + (s.Diagnostic is null ? "" : " (" + s.Diagnostic + ")")));
        if (_context() is { } c)
        {
            _policy = AutomationProtocol.Read<StoredAutomationPolicy>(await _client.CallAsync(AutomationOperation.CurrentPolicy, c.Id.Value, new { }));
            _control = AutomationProtocol.Read<AutomationHostControl>(await _client.CallAsync(AutomationOperation.CurrentControl, c.Id.Value, new { }));
        }
        Activate.Refresh(); Pause.Refresh(); Resume.Refresh();
    }
    private async Task Change(AutomationOperation operation)
    {
        var c = _context() ?? throw new InvalidOperationException("Selecciona un universo.");
        Status = operation == AutomationOperation.Pause ? "AutomationHost · pausa solicitada" : "AutomationHost · reanudación solicitada";
        await _client.CallAsync(operation, c.Id.Value, new { }); await RefreshAsync();
    }
    private async Task ActivateAsync()
    {
        var c = _context()!; var policy = _policy!;
        if (!await _confirmation.ConfirmAsync(new("Activar automatización", "Se aplicará exclusivamente la política autorizada y vigente de este universo sintético. La reanudación automática tras reinicio permanece deshabilitada.",
            c.Id, ["Política: " + policy.Reference.PolicyId + " v" + policy.Reference.Version, "Hash: " + policy.Reference.Hash.Hex,
            "Operaciones: " + policy.Definition.Operations, "Presupuesto IA máximo: " + policy.Definition.Audit.MaxCost]), CancellationToken.None)) return;
        await _client.CallAsync(AutomationOperation.Activate, c.Id.Value, new AutomationActivation(policy.Reference, false)); await RefreshAsync();
    }
    private async Task StopAsync()
    {
        var c = _context(); if (c is null) return;
        if (!await _confirmation.ConfirmAsync(new("Detener automatización", "AutomationHost persistirá la parada y drenará sus operaciones. Un cierre pendiente no se forzará. La interfaz podrá permanecer abierta.", c.Id, []), CancellationToken.None)) return;
        Status = "AutomationHost · parada pendiente";
        var reply = await _client.CallAsync(AutomationOperation.ShutdownHost, null, new { });
        var snapshot = AutomationProtocol.Read<AutomationHostSnapshot>(reply); Status = "AutomationHost · " + snapshot.State;
    }
}

using System.Collections.ObjectModel;
using NAP.Core;

namespace NAP.Presentation;

public interface IAutomationOverviewClient
{
    Task<AutomationDesktopOverview> ReadAsync(UniverseId universe, CancellationToken cancellation);
    Task SendAsync(AutomationOperation operation, UniverseId? universe, object payload, CancellationToken cancellation);
}
public sealed class AutomationOverviewClient(AutomationHostClient client) : IAutomationOverviewClient
{
    public async Task<AutomationDesktopOverview> ReadAsync(UniverseId universe, CancellationToken cancellation) =>
        AutomationProtocol.Read<AutomationDesktopOverview>(await client.CallAsync(AutomationOperation.Overview, universe.Value, new { }, cancellation));
    public async Task SendAsync(AutomationOperation operation, UniverseId? universe, object payload, CancellationToken cancellation) =>
        _ = await client.CallAsync(operation, universe?.Value, payload, cancellation);
}
public sealed record AutomationReviewRow(string Name, string Problem, string Date);

/// <summary>Display projection only. No local execution state, decisions or fallback writer.</summary>
public sealed class AutomationControlViewModel : ObservableObject, IDisposable
{
    private readonly IAutomationOverviewClient _client;
    private readonly Func<UniverseContext?> _context;
    private readonly IUserConfirmation _confirmation;
    private readonly CancellationTokenSource _lifetime = new();
    private AutomationDesktopOverview? _overview;
    private string _status = "Consultando automatización…", _executionScope = "";
    private bool _reading, _changing, _disposed;
    private long _generation;
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string ExecutionScope { get => _executionScope; private set => Set(ref _executionScope, value); }
    public string Pending => _overview?.Queue.Counts.Pending.ToString("N0") ?? "—";
    public string Processing => _overview?.Queue.Counts.Processing.ToString("N0") ?? "—";
    public string Completed => _overview?.Queue.Counts.Completed.ToString("N0") ?? "—";
    public string NeedsReview => _overview?.Queue.Counts.NeedsReview.ToString("N0") ?? "—";
    public string ReviewLabel => _overview?.Queue.Counts.NeedsReview switch { null => "Incidencias sin consultar", 0 => "Sin incidencias", 1 => "1 necesita revisión", var n => $"{n:N0} necesitan revisión" };
    public string ReviewRemainder => _overview is { Queue: var q } && q.Counts.NeedsReview > q.Review.Count
        ? $"{q.Counts.NeedsReview - q.Review.Count:N0} más pendientes de revisión" : "";
    public ObservableCollection<AutomationReviewRow> Review { get; } = [];
    public bool CanPause => !_disposed && !_changing && Current() && _overview!.Control is { Grant: not null, Paused: false, Stopped: false } && SelectedState() == AutomationHostState.Running;
    private bool AuthorizedGrant => Current() && _overview!.Queue.Policy.IsPreproductionAuthorized && _overview.Control.Grant is { } grant &&
        grant.Policy == _overview.Queue.Policy.Reference && grant.PolicyAuthorizationId == _overview.Queue.Policy.Authorization?.AuthorizationId &&
        grant.Actor == _overview.Queue.Policy.Authorization?.Actor;
    public bool CanResume => !_disposed && !_changing && AuthorizedGrant &&
        _overview!.Control is { Grant: not null, Paused: true, Stopped: false } && SelectedState() == AutomationHostState.Paused;
    public UiAsyncCommand Refresh { get; }
    public UiAsyncCommand Activate { get; }
    public UiAsyncCommand Pause { get; }
    public UiAsyncCommand Resume { get; }
    public UiAsyncCommand Stop { get; }
    public AutomationControlViewModel(AutomationHostClient client, Func<UniverseContext?> context, IUserConfirmation confirmation)
        : this(context, confirmation, new AutomationOverviewClient(client)) { }
    public AutomationControlViewModel(Func<UniverseContext?> context, IUserConfirmation confirmation, IAutomationOverviewClient client)
    {
        _client = client; _context = context; _confirmation = confirmation;
        Refresh = new(RefreshAsync, () => !_disposed, _ => Unavailable());
        Activate = new(ActivateAsync, () => !_disposed && !_changing && Current() && _overview!.Queue.Policy.IsPreproductionAuthorized && _overview.Control.Stopped, _ => Unavailable());
        Pause = new(() => Change(AutomationOperation.Pause), () => CanPause, _ => Unavailable());
        Resume = new(() => Change(AutomationOperation.Resume), () => CanResume, _ => Unavailable());
        Stop = new(StopAsync, () => !_disposed && !_changing && Current(), _ => Unavailable());
    }
    private bool Current() => _overview is not null && _overview.Queue.UniverseId == _context()?.Id;
    private AutomationHostState? SelectedState() => _overview?.Host.Universes.SingleOrDefault(s => s.UniverseId == _overview.Queue.UniverseId)?.State;
    public void ContextChanged()
    {
        ++_generation; Clear(); Status = _context() is null ? "Selecciona un universo" : "Consultando automatización…"; ExecutionScope = "";
    }
    public async Task RefreshAsync()
    {
        if (_disposed || _reading || _changing) return;
        var context = _context(); if (context is null) { ContextChanged(); return; }
        var generation = _generation; _reading = true;
        try
        {
            var value = await _client.ReadAsync(context.Id, _lifetime.Token);
            if (_disposed || generation != _generation || context != _context()) return;
            if (value.Queue.UniverseId != context.Id || value.Control.UniverseId != context.Id ||
                value.Queue.Policy.Definition.Roots != context.Storage || value.Host.Universes.Count(s => s.UniverseId == context.Id) != 1)
                throw new InvalidDataException("Overview scope mismatch.");
            _overview = value;
            var state = value.Host.State is AutomationHostState.Blocked or AutomationHostState.Degraded or AutomationHostState.Stopping or AutomationHostState.Stopped
                ? value.Host.State : SelectedState()!.Value;
            Status = Describe(state, value.Queue.Counts);
            var active = value.Host.Universes.Count(s => s.State is AutomationHostState.Running or AutomationHostState.Pausing or AutomationHostState.Stopping);
            ExecutionScope = active == 1 ? "En marcha: 1 universo" : $"En marcha: {active} universos";
            Review.Clear();
            foreach (var item in value.Queue.Review) Review.Add(new(item.FileName, Problem(item.Incident), item.UpdatedUtc.ToLocalTime().ToString("g")));
            Changed();
        }
        catch (OperationCanceledException) when (_disposed) { }
        catch (Exception) { if (!_disposed && generation == _generation && context == _context()) Unavailable(); }
        finally { _reading = false; }
    }
    private void Unavailable() { Clear(); Status = "No se pudo consultar la automatización"; ExecutionScope = ""; }
    private void Clear() { _overview = null; Review.Clear(); Changed(); }
    private void Changed()
    {
        foreach (var name in new[] { nameof(Pending), nameof(Processing), nameof(Completed), nameof(NeedsReview), nameof(ReviewLabel), nameof(ReviewRemainder), nameof(CanPause), nameof(CanResume) }) Notify(name);
        Activate.Refresh(); Pause.Refresh(); Resume.Refresh(); Stop.Refresh();
    }
    private async Task Change(AutomationOperation operation)
    {
        if (!Current()) return;
        var context = _context()!; _changing = true; ++_generation;
        Clear(); Status = operation == AutomationOperation.Pause ? "Pausando automatización…" : "Reanudando automatización…";
        try { await _client.SendAsync(operation, context.Id, new { }, _lifetime.Token); }
        catch { Unavailable(); throw; }
        finally { _changing = false; }
        await RefreshAsync();
    }
    private async Task ActivateAsync()
    {
        if (!Current()) return;
        var context = _context()!; var policy = _overview!.Queue.Policy;
        if (!await _confirmation.ConfirmAsync(new("Activar automatización", "Se aplicará la política autorizada de este universo de laboratorio.", context.Id, []), _lifetime.Token)) return;
        if (!Current() || context != _context()) return;
        _changing = true; ++_generation; Clear();
        try { await _client.SendAsync(AutomationOperation.Activate, context.Id, new AutomationActivation(policy.Reference, false), _lifetime.Token); }
        finally { _changing = false; }
        await RefreshAsync();
    }
    private async Task StopAsync()
    {
        var context = _context(); if (context is null) return;
        if (!await _confirmation.ConfirmAsync(new("Detener automatización", "Se detendrá la automatización de todos los universos conectados.", context.Id, []), _lifetime.Token)) return;
        _changing = true; ++_generation; Clear(); Status = "Deteniendo automatización…";
        try { await _client.SendAsync(AutomationOperation.ShutdownHost, null, new { }, _lifetime.Token); }
        finally { _changing = false; }
        await RefreshAsync();
    }
    public static string Describe(AutomationHostState state, AutomationQueueCounts counts) => state switch
    {
        AutomationHostState.Starting => "Consultando automatización…",
        AutomationHostState.Pausing => "Pausando automatización…",
        AutomationHostState.Paused => "Automatización pausada",
        AutomationHostState.Stopping => "Deteniendo automatización…",
        AutomationHostState.Stopped => "Automatización detenida",
        AutomationHostState.Disabled => "Automatización sin activar",
        AutomationHostState.Blocked or AutomationHostState.Degraded => "Automatización necesita revisión",
        AutomationHostState.Running when counts.Processing > 0 => "Procesando imágenes",
        AutomationHostState.Running when counts.Pending > 0 => "Automatización activa",
        AutomationHostState.Running when counts.NeedsReview > 0 => counts.NeedsReview == 1 ? "1 necesita revisión" : $"{counts.NeedsReview:N0} necesitan revisión",
        AutomationHostState.Running => "Todo al día",
        _ => "Estado sin confirmar"
    };
    public static string Problem(QueueIncident incident) => incident switch
    {
        QueueIncident.UniverseMismatch => "Pertenece a otro universo",
        QueueIncident.ProfileUnknown => "Perfil sin reconocer",
        QueueIncident.RatioUnsupported or QueueIncident.NormalizationLimitExceeded => "Proporciones incompatibles",
        QueueIncident.ExceptionDecisionRequired => "Necesita una decisión",
        QueueIncident.AssetCollision or QueueIncident.ReservationOccupied or QueueIncident.ExistingStorage => "Ya existe otro asset",
        QueueIncident.MasterDuplicate => "Imagen ya registrada",
        QueueIncident.MissingSource => "No se encuentra el archivo original",
        QueueIncident.SourceChanged => "El archivo original ha cambiado",
        QueueIncident.InvalidZip or QueueIncident.UnsafeZip => "Paquete no válido",
        QueueIncident.InvalidManifest => "Datos del asset no válidos",
        QueueIncident.StorageUnavailable => "No se pudo acceder al almacenamiento",
        QueueIncident.ResourceLimit => "Supera los límites permitidos",
        QueueIncident.AuditWarning => "La revisión de imagen requiere atención",
        QueueIncident.AuditFail => "La imagen no supera la revisión",
        QueueIncident.AuditBudgetExhausted => "Límite de revisiones alcanzado",
        QueueIncident.AuditDependencyBlocked or QueueIncident.AuditInvalidResponse => "No se pudo completar la revisión",
        QueueIncident.PolicyInactive => "Automatización sin autorización vigente",
        _ => "Necesita revisión"
    };
    public void Dispose() { if (_disposed) return; _disposed = true; ++_generation; _lifetime.Cancel(); Clear(); _lifetime.Dispose(); }
}

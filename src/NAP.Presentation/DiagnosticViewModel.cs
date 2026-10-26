using System.Collections.ObjectModel;
using System.Text;
using NAP.Core;
namespace NAP.Presentation;

public interface IDiagnosticDesktopClient
{
    Task<DiagnosticPage> ReadAsync(UniverseId universe, DiagnosticQuery query, CancellationToken cancellation);
}
public sealed class DiagnosticDesktopClient(AutomationHostClient client) : IDiagnosticDesktopClient
{
    public async Task<DiagnosticPage> ReadAsync(UniverseId u, DiagnosticQuery q, CancellationToken ct) =>
        AutomationProtocol.Read<DiagnosticPage>(await client.CallAsync(AutomationOperation.Diagnostics, u.Value, q, ct));
}
public sealed record DiagnosticChoice<T>(T? Value, string Label) where T : struct;
public sealed record DiagnosticRow(DateTimeOffset? Utc, DiagnosticSeverity Severity, QueueStage? Category,
    QueueEventCode? Code, QueueEventResult? Result, QueueState? State, QueueIncident? Incident, string? LocalCode = null, JobState? ManualState = null)
{
    public string When => Utc?.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss") ?? "Sin fecha registrada";
    public string Level => Severity switch { DiagnosticSeverity.Warning => "Aviso", DiagnosticSeverity.Error => "Error", _ => "Información" };
    public string Area => Category is { } c ? CategoryLabel(c) : "Aplicación";
    public string Message => Incident switch
    {
        QueueIncident.AuditFail => "La imagen no supera la validación",
        QueueIncident.AuditWarning => "La imagen necesita revisión",
        QueueIncident.RatioUnsupported or QueueIncident.NormalizationLimitExceeded => "Proporciones no admitidas",
        QueueIncident.ProfileUnknown => "Perfil no reconocido",
        QueueIncident.StorageUnavailable => "Almacenamiento no disponible",
        QueueIncident.MissingSource or QueueIncident.SourceChanged => "El original no está disponible o ha cambiado",
        QueueIncident.AssetCollision or QueueIncident.ReservationOccupied => "Posible colisión de assets",
        QueueIncident.PolicyInactive => "La autorización requiere revisión",
        { } i when i != QueueIncident.None => "La operación necesita revisión",
        _ when ManualState == JobState.Completed => "Operación completada",
        _ => Severity == DiagnosticSeverity.Error ? "La operación se ha detenido" : Severity == DiagnosticSeverity.Warning ? "Revisa la operación" : "Actividad registrada"
    };
    // Closed enum vocabulary only. Never pass arbitrary messages, IDs, names, paths or credentials to reports.
    public string Detail => string.Join(" · ", new[] { Safe(Code), Safe(Result), Safe(State), Safe(Incident), Safe(ManualState), KnownLocalCodes.Contains(LocalCode ?? "") ? LocalCode! : "" }.Where(s => s.Length > 0));
    private static readonly HashSet<string> KnownLocalCodes = typeof(NapIssueCodes).GetFields(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static)
        .Where(f=>f.IsLiteral&&f.FieldType==typeof(string)).Select(f=>(string)f.GetRawConstantValue()!).ToHashSet(StringComparer.Ordinal);
    private static string Safe<T>(T? value) where T : struct, Enum => value is { } e && Enum.IsDefined(e) ? e.ToString() : "";
    public static string CategoryLabel(QueueStage stage) => stage switch { QueueStage.Observation => "Entrada", QueueStage.Admission => "Preparación", QueueStage.Preparation => "Preparación de imágenes", QueueStage.Normalization => "Ajuste de imagen", QueueStage.Audit => "Validación", QueueStage.Execution => "Producción", QueueStage.Verification => "Verificación", QueueStage.Catalog => "Catálogo", _ => "Revisión" };
}
public sealed class DiagnosticViewModel : ObservableObject, IDisposable
{
    private readonly IDiagnosticDesktopClient? _client;
    private readonly Func<UniverseId?> _universe;
    private readonly Func<IReadOnlyList<DiagnosticRow>> _local;
    private readonly Func<CancellationToken,Task<IReadOnlyList<DiagnosticRow>>>? _persisted;
    private readonly Stack<long?> _back = new();
    private CancellationTokenSource? _read;
    private int _generation, _reads;
    private long? _before, _through, _next;
    private bool _more, _disposed, _isLocal;
    private string _notice = "Actualiza para consultar";
    private DateTime? _from, _until;
    private DiagnosticChoice<DiagnosticSeverity>? _severity;
    private DiagnosticChoice<QueueStage>? _category;
    private DiagnosticRow? _selected;
    public DiagnosticViewModel(IDiagnosticDesktopClient? client, Func<UniverseId?> universe, Func<IReadOnlyList<DiagnosticRow>> local, Func<CancellationToken,Task<IReadOnlyList<DiagnosticRow>>>? persisted = null)
    {
        _client = client; _isLocal = client is null; _universe = universe; _local = local; _persisted = persisted; _severity = Severities[0]; _category = Categories[0];
        Refresh = new(() => ReadAsync(true), () => !_disposed && _reads==0, Fail);
        Next = new(async () => { _back.Push(_before); _before = _next; await ReadAsync(false); }, () => !_disposed && _reads==0 && _more, Fail);
        Previous = new(async () => { _before = _back.Pop(); await ReadAsync(false); }, () => !_disposed && _reads==0 && _back.Count > 0, Fail);
    }
    public IReadOnlyList<DiagnosticChoice<DiagnosticSeverity>> Severities { get; } = [new(null,"Todas las gravedades"),new(DiagnosticSeverity.Information,"Información"),new(DiagnosticSeverity.Warning,"Avisos"),new(DiagnosticSeverity.Error,"Errores")];
    public IReadOnlyList<DiagnosticChoice<QueueStage>> Categories { get; } = new[] { new DiagnosticChoice<QueueStage>(null,"Todas las categorías") }.Concat(Enum.GetValues<QueueStage>().Select(c=>new DiagnosticChoice<QueueStage>(c,DiagnosticRow.CategoryLabel(c)))).ToArray();
    public DateTime? From { get => _from; set { if (Set(ref _from,value)) ContextChanged(); } }
    public DateTime? Until { get => _until; set { if (Set(ref _until,value)) ContextChanged(); } }
    public DiagnosticChoice<DiagnosticSeverity>? Severity { get => _severity; set { if(Set(ref _severity,value)) ContextChanged(); } }
    public DiagnosticChoice<QueueStage>? Category { get => _category; set { if(Set(ref _category,value)) ContextChanged(); } }
    public ObservableCollection<DiagnosticRow> Rows { get; } = [];
    public DiagnosticRow? Selected { get => _selected; set=>Set(ref _selected,value); }
    public string Notice { get => _notice; private set=>Set(ref _notice,value); }
    public string Source => _isLocal ? "Información local disponible · sesión y muestra de registros" : "Eventos de automatización · universo seleccionado";
    public UiAsyncCommand Refresh { get; }
    public UiAsyncCommand Next { get; }
    public UiAsyncCommand Previous { get; }
    public void ContextChanged()
    { ++_generation; _read?.Cancel(); Rows.Clear(); Selected=null; _back.Clear(); _before=_through=_next=null; _more=false; Notice="Actualiza para consultar"; Next.Refresh(); Previous.Refresh(); Notify(nameof(Report)); }
    public async Task ReadAsync(bool reset)
    {
        if(reset) ContextChanged();
        var generation=++_generation;var universe=_universe();
        _read?.Cancel();_read?.Dispose();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var cancel=_read=CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        ++_reads;Refresh.Refresh();Next.Refresh();Previous.Refresh();Rows.Clear();Selected=null;Notify(nameof(Report));
        Notice="Consultando…";
        try
        {
            var query=new DiagnosticQuery(From is { } f?new DateTimeOffset(f.Date):null,Until is { } u?new DateTimeOffset(u.Date.AddDays(1)):null,Severity?.Value,Category?.Value,_before,_through);
            if(query.From>=query.Until)throw new ArgumentException("Invalid dates.");
            var localRows=_local();
            if(_client is null&&_persisted is not null)localRows=localRows.Concat(await _persisted(cancel.Token)).ToArray();
            var local=localRows.Where(r=>(query.From is null || r.Utc>=query.From)&&(query.Until is null || r.Utc<query.Until)&&
                (query.Severity is null||r.Severity==query.Severity)&&(query.Category is null||r.Category==query.Category)).Take(100).ToArray();
            DiagnosticPage? page=null;
            if(_client is not null&&universe is not null)page=await _client.ReadAsync(universe,query,cancel.Token).WaitAsync(cancel.Token);
            if(_disposed||generation!=_generation||universe!=_universe())return;
            if(page is not null&&(page.UniverseId!=universe||page.Events.Count>30))throw new InvalidDataException("Diagnostic scope differs.");
            _isLocal=page is null;Notify(nameof(Source));
            Rows.Clear();
            foreach(var row in page?.Events.Select(e=>new DiagnosticRow(e.Utc,e.Severity,e.Category,e.Code,e.Result,e.State,e.Incident))??local)Rows.Add(row);
            _through=page?.Through;_next=page?.Events.LastOrDefault()?.Cursor;_more=page?.More??false;
            Notice=Rows.Count==0?"No hay registros con estos filtros":$"{Rows.Count} {(Rows.Count==1?"registro":"registros")} · página {_back.Count+1}";
            Selected=Rows.FirstOrDefault();
        }
        catch(OperationCanceledException){if(generation==_generation)Notice="Consulta cancelada. Actualiza para reintentar.";}
        catch(Exception){if(generation==_generation){Rows.Clear();Selected=null;_more=false;Notice=_client is null?"No se pudo consultar la información local":"No se pudo consultar la automatización. Puedes consultar la información local disponible.";}}
        finally{--_reads;Refresh.Refresh();Next.Refresh();Previous.Refresh();if(generation==_generation)Notify(nameof(Report));}
    }
    public string Report
    {
        get
        {
            var b=new StringBuilder("NAP 2.0 · Diagnóstico anonimizado\n");
            b.AppendLine(_isLocal?"Fuente: información local disponible":"Fuente: automatización; universo anonimizado");
            b.AppendLine($"Consulta: {Notice}");
            b.AppendLine($"Registros: {Rows.Count}. Solo página consultada; no constituye un historial completo.");
            b.AppendLine($"Desde: {From:yyyy-MM-dd}; hasta: {Until:yyyy-MM-dd}; gravedad: {Severity?.Label}; categoría: {Category?.Label}");
            foreach(var r in Rows)b.AppendLine($"{r.Utc?.ToString("O")??"sin fecha"} | {r.Level} | {r.Area} | {r.Message} | {r.Detail}");
            return b.ToString();
        }
    }
    public async Task ShowLocalAsync()
    {
        ContextChanged();var generation=++_generation;var universe=_universe();_isLocal=true;Notify(nameof(Source));
        try
        {
        var local=_local();
        if(_persisted is not null)
        {
            try { using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));local=local.Concat(await _persisted(timeout.Token).WaitAsync(timeout.Token)).ToArray(); }
            catch(Exception){if(generation==_generation)Notice="No se pudieron leer los registros locales";return;}
        }
        if(_disposed||generation!=_generation||universe!=_universe())return;
        foreach(var row in local.Where(r=>(From is null||r.Utc>=new DateTimeOffset(From.Value.Date))&&(Until is null||r.Utc<new DateTimeOffset(Until.Value.Date.AddDays(1)))&&
            (Severity?.Value is null||r.Severity==Severity.Value)&&(Category?.Value is null||r.Category==Category.Value)).Take(100))Rows.Add(row);
        Notice=$"{Rows.Count} registros locales · muestra disponible";Selected=Rows.FirstOrDefault();Notify(nameof(Report));
        }
        catch(Exception){if(generation==_generation){Rows.Clear();Selected=null;Notice="No se pudieron leer los registros locales";Notify(nameof(Report));}}
    }
    private void Fail(Exception _) { Notice="No se pudo completar la consulta"; }
    public void Dispose(){_disposed=true;ContextChanged();_read?.Dispose();}
}

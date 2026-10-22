using System.Collections.ObjectModel;
using NAP.Core;
namespace NAP.Presentation;

public interface IWorkflowDesktopClient
{
    Task<WorkflowPage> PageAsync(UniverseId universe, WorkflowPageRequest request, CancellationToken cancellation);
    Task<WorkflowDetail> DetailAsync(UniverseId universe, WorkflowDetailRequest request, CancellationToken cancellation);
    Task<IReadOnlyList<WorkflowBatch>> BatchesAsync(UniverseId universe, CancellationToken cancellation);
    Task MutateAsync(UniverseId universe, AutomationOperation operation, object request, CancellationToken cancellation);
}
public sealed class WorkflowDesktopClient(AutomationHostClient client) : IWorkflowDesktopClient
{
    public async Task<WorkflowPage> PageAsync(UniverseId u, WorkflowPageRequest r, CancellationToken ct) => AutomationProtocol.Read<WorkflowPage>(await client.CallAsync(AutomationOperation.WorkflowPage,u.Value,r,ct));
    public async Task<WorkflowDetail> DetailAsync(UniverseId u, WorkflowDetailRequest r, CancellationToken ct) => AutomationProtocol.Read<WorkflowDetail>(await client.CallAsync(AutomationOperation.WorkflowDetail,u.Value,r,ct));
    public async Task<IReadOnlyList<WorkflowBatch>> BatchesAsync(UniverseId u,CancellationToken ct) => AutomationProtocol.Read<WorkflowBatch[]>(await client.CallAsync(AutomationOperation.WorkflowBatches,u.Value,new {},ct));
    public async Task MutateAsync(UniverseId u,AutomationOperation op,object r,CancellationToken ct) => _ = await client.CallAsync(op,u.Value,r,ct);
}
public sealed record WorkflowFilterChoice(WorkflowFilter Value,string Label);
public sealed record WorkflowBatchChoice(QueueBatchId? Id,string Label);
public sealed record WorkflowDisplayRow(WorkflowRow Value,string Universe)
{
    public string Name => Value.Name;
    public string State => WorkflowWords.State(Value.State);
    public string Result => Value.Incident != QueueIncident.None ? AutomationControlViewModel.Problem(Value.Incident) : Value.State == QueueState.Completed ? "Completado" : Value.State == QueueState.Duplicate ? "Ya estaba registrado" : "";
    public string Date => WorkflowWords.Date(Value.UpdatedUtc);
    public UiTone Tone => Value.State is QueueState.NeedsReview or QueueState.Rejected or QueueState.MissingSource ? UiTone.Warning : Value.State == QueueState.Completed ? UiTone.Success : UiTone.Neutral;
}
public static class WorkflowWords
{
    public static string Date(DateTimeOffset value) => value.ToLocalTime().ToString("g",System.Globalization.CultureInfo.GetCultureInfo("es-ES"));
    public static string State(QueueState state) => state switch { QueueState.Observed or QueueState.WaitingStable => "Preparando", QueueState.Queued => "Pendiente", QueueState.Running => "Procesando", QueueState.RetryScheduled => "Reintento pendiente", QueueState.Completed => "Completado", QueueState.Duplicate => "Ya registrado", _ => "Necesita revisión" };
    public static string Stage(QueueStage stage) => stage switch { QueueStage.Observation => "Archivo detectado", QueueStage.Admission => "Comprobación del paquete", QueueStage.Preparation => "Preparación", QueueStage.Normalization => "Adaptación de imagen", QueueStage.Audit => "Revisión de imagen", QueueStage.Execution => "Producción", QueueStage.Verification => "Verificación", QueueStage.Catalog => "Registro en catálogo", _ => "Revisión necesaria" };
    public static string Action(QueueEvent value)=>value.Code switch
    {
        QueueEventCode.Observed=>"Archivo recibido",
        QueueEventCode.AttemptCreated=>"Preparación iniciada",
        QueueEventCode.ClaimInterrupted=>"Procesamiento interrumpido",
        QueueEventCode.ClaimRecovered=>"Trabajo recuperado",
        QueueEventCode.CompletedInconsistent=>"Resultado pendiente de revisión",
        QueueEventCode.EvidenceRecorded when value.Stage==QueueStage.Review=>"Decisión registrada",
        QueueEventCode.Transitioned when value.Stage==QueueStage.Review&&value.State==QueueState.Queued=>"Continuación solicitada",
        QueueEventCode.Transitioned when value.State==QueueState.Completed=>"Asset completado",
        _=>Stage(value.Stage)
    };
    public static string Guidance(QueueIncident issue) => issue switch
    {
        QueueIncident.ProfileUnknown => "Corrige el perfil del paquete y vuelve a incorporarlo.",
        QueueIncident.RatioUnsupported or QueueIncident.NormalizationLimitExceeded or QueueIncident.ExceptionDecisionRequired => "Elige una corrección compatible y revisa su vista previa.",
        QueueIncident.AuditWarning or QueueIncident.AuditFail => "Revisa la imagen y prepara una versión corregida. Esta decisión no se puede omitir.",
        QueueIncident.AssetCollision or QueueIncident.MasterDuplicate or QueueIncident.ReservationOccupied or QueueIncident.ExistingStorage => "Compara los originales y corrige el paquete fuera de NAP. Los archivos existentes se conservan.",
        QueueIncident.MissingSource or QueueIncident.SourceChanged or QueueIncident.InvalidEvidence => "Comprueba el original y sus copias. La continuación permanece bloqueada.",
        QueueIncident.StorageUnavailable => "Revisa el acceso y el espacio disponible. El trabajo se conserva para su revisión.",
        QueueIncident.PolicyInactive => "Revisa la autorización de este universo en Ajustes.",
        QueueIncident.AuditBudgetExhausted or QueueIncident.AuditDependencyBlocked or QueueIncident.AuditInvalidResponse => "Revisa la configuración de automatización en Ajustes.",
        _ => "Revisa el paquete antes de volver a incorporarlo."
    };
}
public sealed record WorkflowEventRow(string Date,string Action,string Result);

/// <summary>Read projections with independent scope/generation guards. Mutations only through the authenticated host.</summary>
public sealed class WorkflowViewModel : ObservableObject,IDisposable
{
    private readonly IWorkflowDesktopClient? _client;
    private readonly Func<UniverseContext?> _context;
    private readonly IUserConfirmation _confirmation;
    private readonly CancellationTokenSource _lifetime=new();
    private CancellationTokenSource? _pageRead,_detailRead;
    private long _scope,_pageGeneration,_detailGeneration;
    private bool _disposed,_changing;
    private int _offset,_total,_eventOffset;
    private string _search="",_status="",_detailStatus="";
    private QueueItemId? _focusedItem;
    private QueueItemId? ActiveItem=>Selected?.Value.ItemId??_focusedItem;
    private WorkflowDisplayRow? _selected;
    private WorkflowDetail? _detail;
    private ReviewChoice? _choice;
    private WorkflowFilterChoice _filter;
    private WorkflowBatchChoice _batch=new(null,"Todos los lotes");
    public WorkflowViewModel(IWorkflowDesktopClient? client,Func<UniverseContext?> context,IUserConfirmation confirmation)
    {
        _client=client;_context=context;_confirmation=confirmation;_filter=Filters[0]; Batches.Add(_batch);
        Poll=new(()=>LoadPageAsync(false,true),()=>!_disposed&&!_changing&&ActiveItem==null,FailRead);
        CloseDetail=new(_=>{_focusedItem=null;Selected=null;_detailRead?.Cancel();++_detailGeneration;ClearDetail();DetailStatus="";});
        Refresh=new(()=>LoadPageAsync(true),()=>!_disposed,FailRead);
        Apply=new(()=>LoadPageAsync(true),()=>!_disposed,FailRead);
        Next=new(()=>ChangePage(20),()=>!_disposed&&_offset+20<_total,FailRead);
        Previous=new(()=>ChangePage(-20),()=>!_disposed&&_offset>0,FailRead);
        Propose=new(()=>Change(AutomationOperation.ReviewPropose),()=>!_disposed&&!_changing&&_detail?.CanPropose==true&&Choice!=null,FailMutation);
        Approve=new(()=>Change(AutomationOperation.ReviewApprove),()=>!_disposed&&!_changing&&_detail?.CanApprove==true,FailMutation);
        Continue=new(()=>Change(AutomationOperation.ReviewContinue),()=>!_disposed&&!_changing&&_detail?.CanContinue==true,FailMutation);
        NextEvents=new(()=>LoadEvents(20),()=>!_disposed&&_detail?.MoreEvents==true,FailRead);
        PreviousEvents=new(()=>LoadEvents(-20),()=>!_disposed&&_eventOffset>0,FailRead);
    }
    public IReadOnlyList<WorkflowFilterChoice> Filters {get;}=[new(WorkflowFilter.All,"Todos"),new(WorkflowFilter.Pending,"Pendientes"),new(WorkflowFilter.Processing,"En curso"),new(WorkflowFilter.Completed,"Completados"),new(WorkflowFilter.Review,"Necesitan revisión")];
    public WorkflowFilterChoice Filter {get=>_filter;set{if(Set(ref _filter,value)) _offset=0;}}
    public string Search {get=>_search;set=>Set(ref _search,value);}
    public ObservableCollection<WorkflowBatchChoice> Batches {get;}=[];
    public WorkflowBatchChoice Batch {get=>_batch;set{if(Set(ref _batch,value))_offset=0;}}
    public bool HasBatches=>Batches.Count>1;
    public ObservableCollection<WorkflowDisplayRow> Rows {get;}=[];
    public ObservableCollection<WorkflowEventRow> Events {get;}=[];
    public ObservableCollection<WorkflowEventRow> Decisions {get;}=[];
    public bool HasDecisions=>Decisions.Count>0;
    public WorkflowDisplayRow? Selected {get=>_selected;set{if(Set(ref _selected,value)){_focusedItem=value?.Value.ItemId;_eventOffset=0;ClearDetail();LastDetail=LoadDetailAsync();}}}
    public Task LastDetail {get;private set;}=Task.CompletedTask;
    public string Status {get=>_status;private set=>Set(ref _status,value);}
    public string DetailStatus {get=>_detailStatus;private set=>Set(ref _detailStatus,value);}
    public string PageLabel=>_total==0?"":$"{_offset+1}–{Math.Min(_offset+20,_total)} de {_total:N0}";
    public bool HasDetail=>_detail!=null;
    public bool HasOptions=>_detail?.CanPropose==true&&_detail.Choices.Count>0;
    public bool ActivityExpanded { get; set; }
    private bool _editExpanded;
    public bool EditExpanded {get=>_editExpanded;set=>Set(ref _editExpanded,value);}
    public bool ShowOriginal=>HasImage&&!HasPreview;
    public bool CanApprove=>_detail?.CanApprove==true;
    public bool CanContinue=>_detail?.CanContinue==true;
    public bool HasEventPaging=>_eventOffset>0||_detail?.MoreEvents==true;
    public bool HasPreview=>_detail?.Preview!=null;
    public bool HasImage=>OriginalImage!=null;
    public bool NeedsReview=>_detail?.Item.State is QueueState.NeedsReview or QueueState.Rejected or QueueState.MissingSource;
    public byte[]? OriginalImage=>_detail?.Preview?.OriginalImage??_detail?.OriginalImage;
    public byte[]? CandidateImage=>_detail?.Preview?.CandidateImage;
    public string Name=>_detail?.Item.Name??"";
    public string Universe=>_context()?.Profile.DisplayName??"";
    public string PackageName=>_detail?.Item.PackageName??"";
    public string ItemState=>_detail is null?"":WorkflowWords.State(_detail.Item.State);
    public string Dates=>_detail is null?"":$"Recibido: {WorkflowWords.Date(_detail.Item.CreatedUtc)} · Actualizado: {WorkflowWords.Date(_detail.Item.UpdatedUtc)}";
    public string Problem=>_detail is null?"":AutomationControlViewModel.Problem(_detail.Item.Incident);
    public string Guidance=>_detail?.ReadNotice is {} note?note:_detail?.Preview?.State==ExceptionalDecisionState.Approved
        ? _detail.CanContinue?"Corrección aprobada para este asset.":"Corrección guardada. Revisa la autorización y reanuda este universo para continuar."
        :_detail is null?"":HasOptions?WorkflowWords.Guidance(_detail.Item.Incident):NeedsReview?BlockedGuidance():"";
    private string BlockedGuidance()=>_detail!.Item.Incident is QueueIncident.RatioUnsupported or QueueIncident.NormalizationLimitExceeded or QueueIncident.ExceptionDecisionRequired
        ?"No hay una corrección autorizada disponible. Revisa la imagen y la autorización de este universo.":WorkflowWords.Guidance(_detail.Item.Incident);
    public IReadOnlyList<ReviewChoice> Choices=>_detail?.Choices??[];
    public ReviewChoice? Choice {get=>_choice;set{if(Set(ref _choice,value)){if(_detail?.Preview!=null){_detail=_detail with{Preview=null,CanApprove=false,CanContinue=false};NotifyDetail();}RefreshCommands();}}}
    public string ProfileChange=>_detail?.Preview is {} p?string.IsNullOrEmpty(p.OriginalProfile)||p.OriginalProfile==p.Profile?"Perfil: "+p.Profile:p.OriginalProfile+" → "+p.Profile:"";
    public string OriginalSize=>_detail?.Preview is {} p?$"{p.OriginalWidth} × {p.OriginalHeight}":"Original preservado";
    public string CandidateSize=>_detail?.Preview is {} p?$"Lienzo: {p.CanvasWidth} × {p.CanvasHeight} · Salida: {p.OutputWidth} × {p.OutputHeight}":"";
    public string Transformation=>_detail?.Preview is {} p?$"Bordes añadidos: {p.Left} izquierda, {p.Top} arriba, {p.Right} derecha, {p.Bottom} abajo. Sin recorte.":"";
    public UiAsyncCommand Poll{get;} public RelayCommand CloseDetail{get;}
    public UiAsyncCommand Refresh{get;} public UiAsyncCommand Apply{get;} public UiAsyncCommand Next{get;} public UiAsyncCommand Previous{get;}
    public UiAsyncCommand Propose{get;} public UiAsyncCommand Approve{get;} public UiAsyncCommand Continue{get;}
    public UiAsyncCommand NextEvents{get;} public UiAsyncCommand PreviousEvents{get;}
    private async Task ChangePage(int by){CloseDetail.Execute(null);_offset=Math.Max(0,_offset+by);await LoadPageAsync(false);}
    public async Task LoadPageAsync(bool reset=false,bool quiet=false)
    {
        if(_disposed)return;if(reset){_offset=0;CloseDetail.Execute(null);}
        _pageRead?.Cancel();var read=_pageRead=CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);read.CancelAfter(TimeSpan.FromSeconds(20));
        var generation=++_pageGeneration;var scope=_scope;var c=_context();var selected=ActiveItem;
        if(_client==null||c==null){Rows.Clear();ClearDetail();Status="La automatización no está conectada";read.Dispose();_pageRead=null;return;}
        if(!quiet)Status="Actualizando…";
        try
        {
            var page=await _client.PageAsync(c.Id,new(_offset,20,Filter.Value,Search,Batch.Id),read.Token);
            var batches=await _client.BatchesAsync(c.Id,read.Token);
            if(!Current(scope,c)||generation!=_pageGeneration||read.IsCancellationRequested)return;
            if(page.UniverseId!=c.Id||page.Items.Any(r=>r.UniverseId!=c.Id)||batches.Any(b=>b.UniverseId!=c.Id))throw new InvalidDataException("Workflow scope mismatch.");
            _total=page.Total;_offset=page.Offset;Rows.Clear();foreach(var row in page.Items)Rows.Add(new(row,c.Profile.DisplayName));
            var prior=Batch.Id;Batches.Clear();Batches.Add(new(null,"Todos los lotes"));
            foreach(var b in batches)Batches.Add(new(b.Id,$"{b.CreatedUtc.ToLocalTime():dd/MM HH:mm:ss} · {b.Items} assets"));
            _batch=Batches.FirstOrDefault(b=>b.Id==prior)??Batches[0];Notify(nameof(Batch));Notify(nameof(HasBatches));
            var match=Rows.FirstOrDefault(r=>r.Value.ItemId==selected);
            if(match!=null)Selected=match;
            else if(_focusedItem!=null)await LoadDetailAsync();
            else Selected=null;
            Status=Rows.Count==0?"No hay registros para estos filtros":"";Notify(nameof(PageLabel));RefreshCommands();
        }
        catch(OperationCanceledException){if(Current(scope,c)&&generation==_pageGeneration)FailRead(new IOException());}
        catch(Exception e){if(Current(scope,c)&&generation==_pageGeneration)FailRead(e);}
        finally{read.Dispose();if(ReferenceEquals(_pageRead,read))_pageRead=null;}
    }
    private bool Current(long scope,UniverseContext? c)=>!_disposed&&scope==_scope&&c==_context();
    public async Task LoadDetailAsync()
    {
        _detailRead?.Cancel();if(_disposed||_client==null||ActiveItem==null||_context() is not {} c)return;
        var item=ActiveItem!;var scope=_scope;var generation=++_detailGeneration;
        using var read=CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);_detailRead=read;read.CancelAfter(TimeSpan.FromSeconds(20));
        DetailStatus="Consultando…";
        try
        {
            var d=await _client.DetailAsync(c.Id,new(item,_eventOffset),read.Token);
            if(!Current(scope,c)||generation!=_detailGeneration||ActiveItem!=item||read.IsCancellationRequested)return;
            if(d.UniverseId!=c.Id||d.Item.UniverseId!=c.Id||d.Item.ItemId!=item)throw new InvalidDataException("Workflow detail mismatch.");
            _detail=d;_choice=null;EditExpanded=d.Preview==null;Events.Clear();foreach(var e in d.Events)Events.Add(new(WorkflowWords.Date(e.Utc),WorkflowWords.Action(e),e.Incident!=QueueIncident.None?AutomationControlViewModel.Problem(e.Incident):WorkflowWords.State(e.State)));
            Decisions.Clear();foreach(var decision in d.Decisions??[])Decisions.Add(new(WorkflowWords.Date(decision.Utc),decision.State switch{ExceptionalDecisionState.Proposed=>"Corrección propuesta",ExceptionalDecisionState.Approved=>"Corrección aprobada",_=>"Corrección revocada"},$"{decision.Profile} · {decision.Width} × {decision.Height}"));
            DetailStatus="";NotifyDetail();
        }
        catch(OperationCanceledException){if(Current(scope,c)&&generation==_detailGeneration){ClearDetail();DetailStatus="Lectura cancelada. Actualiza para volver a consultar.";}}
        catch(Exception){if(Current(scope,c)&&generation==_detailGeneration){ClearDetail();DetailStatus="No se pudo consultar el detalle";}}
        finally{if(ReferenceEquals(_detailRead,read))_detailRead=null;}
    }
    public async Task OpenItemAsync(QueueItemId item)
    {
        if(_disposed||_client==null||_context() is null)return;
        // Navigation retains only the requested identity; the bounded host read supplies every fact.
        _focusedItem=item;_selected=Rows.FirstOrDefault(r=>r.Value.ItemId==item);Notify(nameof(Selected));
        _eventOffset=0;ClearDetail();await LoadDetailAsync();
    }
    private Task LoadEvents(int by){_eventOffset=Math.Max(0,_eventOffset+by);return LoadDetailAsync();}
    private async Task Change(AutomationOperation operation)
    {
        if(_client==null||_detail==null||_context() is not {} c)return;
        var scope=_scope;var d=_detail;var choice=Choice;var preview=d.Preview;
        if(operation==AutomationOperation.ReviewApprove)
        {
            if(preview==null||!await _confirmation.ConfirmAsync(new("Aprobar corrección", "Solo este asset. El original se conserva; la imagen volverá a pasar sus verificaciones.", c.Id,
                [d.Item.Name,ProfileChange,$"Lienzo: {preview.CanvasWidth} × {preview.CanvasHeight}",$"Salida: {preview.OutputWidth} × {preview.OutputHeight}"],c.Profile.DisplayName),_lifetime.Token))return;
            if(!Current(scope,c)||_detail!=d)return;
        }
        object request=operation==AutomationOperation.ReviewPropose?new ReviewProposalRequest(d.Item.ItemId,d.Item.Revision,choice!.Key)
            :new ReviewDecisionRequest(d.Item.ItemId,d.Item.Revision,preview!.DecisionId,preview.Version,preview.Evidence,true);
        _changing=true;RefreshCommands();DetailStatus=operation==AutomationOperation.ReviewPropose?"Preparando vista previa…":"Guardando decisión…";
        try
        {
            await _client.MutateAsync(c.Id,operation,request,_lifetime.Token);
            if(!Current(scope,c)||ActiveItem!=d.Item.ItemId)return;
            await LoadDetailAsync();
            if(operation==AutomationOperation.ReviewContinue){await LoadPageAsync();DetailStatus="Continuación solicitada";}
        }
        catch(Exception){if(Current(scope,c)){ClearDetail();DetailStatus="Resultado sin confirmar. Actualiza antes de continuar.";}}
        finally{_changing=false;RefreshCommands();}
    }
    private void NotifyDetail(){foreach(var n in new[]{nameof(HasDetail),nameof(HasOptions),nameof(HasPreview),nameof(CanApprove),nameof(CanContinue),nameof(HasEventPaging),nameof(HasDecisions),nameof(HasImage),nameof(ShowOriginal),nameof(NeedsReview),nameof(OriginalImage),nameof(CandidateImage),nameof(Name),nameof(Universe),nameof(ItemState),nameof(Dates),nameof(PackageName),nameof(Problem),nameof(Guidance),nameof(Choices),nameof(Choice),nameof(OriginalSize),nameof(CandidateSize),nameof(Transformation),nameof(ProfileChange)})Notify(n);RefreshCommands();}
    private void ClearDetail(){_detail=null;_choice=null;Events.Clear();Decisions.Clear();NotifyDetail();}
    private void RefreshCommands(){foreach(var command in new[]{Next,Previous,Propose,Approve,Continue,NextEvents,PreviousEvents})command.Refresh();}
    private void FailRead(Exception _){Rows.Clear();_total=0;Selected=null;ClearDetail();Status="No se pudo consultar la automatización. Actualiza para volver a intentarlo.";Notify(nameof(PageLabel));RefreshCommands();}
    private void FailMutation(Exception _){ClearDetail();DetailStatus="Resultado sin confirmar. Actualiza antes de continuar.";}
    public void ContextChanged(){++_scope;++_pageGeneration;++_detailGeneration;_pageRead?.Cancel();_detailRead?.Cancel();Rows.Clear();_selected=null;_focusedItem=null;_offset=0;_eventOffset=0;_total=0;_search="";_filter=Filters[0];_batch=new(null,"Todos los lotes");Batches.Clear();Batches.Add(_batch);ClearDetail();Status="";DetailStatus="";foreach(var n in new[]{nameof(Selected),nameof(Search),nameof(Filter),nameof(Batch),nameof(HasBatches),nameof(PageLabel)})Notify(n);}
    public void Dispose(){if(_disposed)return;_disposed=true;_lifetime.Cancel();ContextChanged();_lifetime.Dispose();}
}

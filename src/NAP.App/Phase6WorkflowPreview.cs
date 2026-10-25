using NAP.Core;
using NAP.Presentation;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
namespace NAP.App;

/// <summary>In-memory fixture for rendering only; all mutations are rejected.</summary>
internal sealed class Phase6WorkflowPreview(string mode, bool acceptanceReadOnly = false) : IWorkflowDesktopClient
{
    internal static readonly string[] Modes=["production","history","choice","exception","approved","blocked","empty","read-error","manual","activity","activity-decisions"];
    private readonly QueueItemId[] _ids=[QueueItemId.Create(),QueueItemId.Create(),QueueItemId.Create(),QueueItemId.Create()];
    private readonly string _decision="decision_"+Guid.NewGuid().ToString("N");
    private readonly QueueBatchId _batch=QueueBatchId.Create();
    private static readonly DateTimeOffset Utc=new(2026,10,22,10,30,0,TimeSpan.Zero);
    private WorkflowRow[] Rows(UniverseId u)=>mode=="empty"?[]:[
        new(_ids[0],u,"Retrato de muestra",QueueState.NeedsReview,mode=="blocked"?QueueIncident.AuditFail:QueueIncident.RatioUnsupported,Utc.AddMinutes(-20),Utc,_batch,4),
        new(_ids[1],u,"Mapa de muestra",QueueState.Running,QueueIncident.None,Utc.AddMinutes(-15),Utc.AddMinutes(-1),_batch,3),
        new(_ids[2],u,"Escena de muestra",QueueState.Completed,QueueIncident.None,Utc.AddMinutes(-30),Utc.AddMinutes(-2),_batch,8),
        new(_ids[3],u,"Retrato pendiente",QueueState.Queued,QueueIncident.None,Utc.AddMinutes(-4),Utc.AddMinutes(-3),_batch,2)];
    internal AutomationQueueOverview ReadOverview(UniverseId u,StoredAutomationPolicy policy)
    {
        var rows=Rows(u);
        return new(u,Utc,new(rows.Count(r=>r.State is QueueState.Observed or QueueState.WaitingStable or QueueState.Queued or QueueState.RetryScheduled),
            rows.Count(r=>r.State==QueueState.Running),rows.Count(r=>r.State==QueueState.Completed),
            rows.Count(r=>r.State is QueueState.NeedsReview or QueueState.Rejected or QueueState.MissingSource),rows.Count(r=>r.State==QueueState.Duplicate)),
            rows.Where(r=>r.State is QueueState.NeedsReview or QueueState.Rejected or QueueState.MissingSource).Select(r=>new AutomationReviewItem(r.ItemId,r.Name+".zip",r.Incident,r.UpdatedUtc)).ToArray(),policy);
    }
    public Task<WorkflowPage> PageAsync(UniverseId u,WorkflowPageRequest r,CancellationToken ct)
    {
        if(mode=="read-error")throw new System.IO.IOException("Synthetic read failure.");
        var items=mode=="empty"?[]:Rows(u).Where(i=>r.Filter switch{WorkflowFilter.Pending=>i.State==QueueState.Queued,WorkflowFilter.Processing=>i.State==QueueState.Running,WorkflowFilter.Completed=>i.State==QueueState.Completed,WorkflowFilter.Review=>i.State==QueueState.NeedsReview,_=>true}).Where(i=>i.Name.Contains(r.Search,StringComparison.OrdinalIgnoreCase)).ToArray();
        return Task.FromResult(new WorkflowPage(u,r.Offset,items.Length,items.Skip(r.Offset).Take(r.Limit).ToArray()));
    }
    public Task<IReadOnlyList<WorkflowBatch>> BatchesAsync(UniverseId u,CancellationToken ct)=>Task.FromResult<IReadOnlyList<WorkflowBatch>>(mode=="empty"?[]:[new(u,_batch,Utc.AddMinutes(-30),4)]);
    public Task<WorkflowDetail> DetailAsync(UniverseId u,WorkflowDetailRequest request,CancellationToken ct)
    {
        var row=Rows(u).Single(i=>i.ItemId==request.ItemId);
        var correction=mode is "exception" or "approved";
        var canChoose=correction||mode=="choice";
        var preview=correction?new ReviewPreview(_decision,mode=="approved"?2:1,new(new string('a',64)),mode=="approved"?ExceptionalDecisionState.Approved:ExceptionalDecisionState.Proposed,
            768,954,768,960,768,960,0,3,0,3,"Retrato",Pattern(false),Pattern(true)):null;
        var allEvents=mode.StartsWith("activity")?Enumerable.Range(1,24).Select(i=>new QueueEvent(row.ItemId,i,Utc.AddMinutes(i-24),QueueStage.Review,QueueEventCode.EvidenceRecorded,QueueEventResult.Recorded,row.State,row.Incident,null)).Reverse().ToArray():
            new QueueEvent[]{new(row.ItemId,4,row.UpdatedUtc,row.State==QueueState.Completed?QueueStage.Catalog:QueueStage.Review,QueueEventCode.Transitioned,row.State==QueueState.Completed?QueueEventResult.Succeeded:QueueEventResult.ReviewRequired,row.State,row.Incident,null),new(row.ItemId,1,row.CreatedUtc,QueueStage.Observation,QueueEventCode.Observed,QueueEventResult.Recorded,QueueState.Observed,QueueIncident.None,null)};
        WorkflowDecisionEntry[] decisions=mode.StartsWith("activity")?Enumerable.Range(1,mode=="activity"?3:24).Select(i=>new WorkflowDecisionEntry(Utc.AddMinutes(-i),ExceptionalDecisionState.Proposed,"Retrato",768,960)).ToArray():
            correction?[new WorkflowDecisionEntry(Utc,mode=="approved"?ExceptionalDecisionState.Approved:ExceptionalDecisionState.Proposed,"Retrato",768,960)]:[];
        return Task.FromResult(new WorkflowDetail(u,row,allEvents.Skip(request.EventOffset).Take(20).ToArray(),allEvents.Length>request.EventOffset+20,
            canChoose?[new("synthetic-choice","Retrato · 768 × 960 (4:5)","Retrato",768,960)]:[],preview,Pattern(false),canChoose&&!acceptanceReadOnly,mode=="exception"&&!acceptanceReadOnly,mode=="approved"&&!acceptanceReadOnly,
            decisions.Skip(request.DecisionOffset).Take(20).ToArray(),MoreDecisions:decisions.Length>request.DecisionOffset+20,EventThrough:24,DecisionThrough:decisions.Length));
    }
    internal static byte[] Pattern(bool border)
    {
        // Illustrative pixels match the declared geometry: 768×954, extended by 3 rows per edge.
        var height=border?960:954;
        using var image=new Image<Rgba32>(768,height);
        for(var y=0;y<height;y++)for(var x=0;x<768;x++)
        {
            var px=x/6;var py=(border?Math.Clamp(y-3,0,953):y)/6;
            var face=(px-64)*(px-64)+(py-55)*(py-55)<24*24;
            var shoulders=py>95&&Math.Abs(px-64)<(py-80)/2;
            image[x,y]=face?new(177,206,206,255):shoulders?new(76,127,143,255):new((byte)(12+px/8),(byte)(35+py/5),(byte)(49+py/6),255);
        }
        image.Mutate(c=>c.Resize(new ResizeOptions{Mode=ResizeMode.Max,Size=new Size(256,256)}));
        using var memory=new System.IO.MemoryStream();image.SaveAsPng(memory);return memory.ToArray();
    }
    public Task MutateAsync(UniverseId universe,AutomationOperation op,object request,CancellationToken cancellation)=>throw new InvalidOperationException("Synthetic rendering cannot write.");
}

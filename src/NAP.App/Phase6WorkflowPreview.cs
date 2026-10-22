using NAP.Core;
using NAP.Presentation;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
namespace NAP.App;

/// <summary>In-memory fixture for rendering only; all mutations are rejected.</summary>
internal sealed class Phase6WorkflowPreview(string mode) : IWorkflowDesktopClient
{
    internal static readonly string[] Modes=["production","history","exception","approved","blocked","empty","read-error","manual"];
    private readonly QueueItemId[] _ids=[QueueItemId.Create(),QueueItemId.Create(),QueueItemId.Create(),QueueItemId.Create()];
    private readonly string _decision="decision_"+Guid.NewGuid().ToString("N");
    private readonly QueueBatchId _batch=QueueBatchId.Create();
    private static readonly DateTimeOffset Utc=new(2026,10,22,10,30,0,TimeSpan.Zero);
    private WorkflowRow[] Rows(UniverseId u)=>[
        new(_ids[0],u,"Retrato de muestra",QueueState.NeedsReview,mode=="blocked"?QueueIncident.AuditFail:QueueIncident.RatioUnsupported,Utc.AddMinutes(-20),Utc,_batch,4),
        new(_ids[1],u,"Mapa de muestra",QueueState.Running,QueueIncident.None,Utc.AddMinutes(-15),Utc.AddMinutes(-1),_batch,3),
        new(_ids[2],u,"Escena de muestra",QueueState.Completed,QueueIncident.None,Utc.AddMinutes(-30),Utc.AddMinutes(-2),_batch,8),
        new(_ids[3],u,"Retrato pendiente",QueueState.Queued,QueueIncident.None,Utc.AddMinutes(-4),Utc.AddMinutes(-3),_batch,2)];
    public Task<WorkflowPage> PageAsync(UniverseId u,WorkflowPageRequest r,CancellationToken ct)
    {
        if(mode=="read-error")throw new System.IO.IOException("Synthetic read failure.");
        var items=mode=="empty"?[]:Rows(u).Where(i=>r.Filter switch{WorkflowFilter.Pending=>i.State==QueueState.Queued,WorkflowFilter.Processing=>i.State==QueueState.Running,WorkflowFilter.Completed=>i.State==QueueState.Completed,WorkflowFilter.Review=>i.State==QueueState.NeedsReview,_=>true}).Where(i=>i.Name.Contains(r.Search,StringComparison.OrdinalIgnoreCase)).ToArray();
        return Task.FromResult(new WorkflowPage(u,r.Offset,items.Length,items.Skip(r.Offset).Take(r.Limit).ToArray()));
    }
    public Task<IReadOnlyList<WorkflowBatch>> BatchesAsync(UniverseId u,CancellationToken ct)=>Task.FromResult<IReadOnlyList<WorkflowBatch>>([new(u,_batch,Utc.AddMinutes(-30),4)]);
    public Task<WorkflowDetail> DetailAsync(UniverseId u,WorkflowDetailRequest request,CancellationToken ct)
    {
        var row=Rows(u).Single(i=>i.ItemId==request.ItemId);
        var correction=mode is "exception" or "approved";
        var preview=correction?new ReviewPreview(_decision,mode=="approved"?2:1,new(new string('a',64)),mode=="approved"?ExceptionalDecisionState.Approved:ExceptionalDecisionState.Proposed,
            768,950,768,960,768,960,0,5,0,5,"Retrato",Pattern(false),Pattern(true)):null;
        return Task.FromResult(new WorkflowDetail(u,row,[new(row.ItemId,4,Utc,row.State==QueueState.Completed?QueueStage.Catalog:QueueStage.Review,QueueEventCode.Transitioned,row.State==QueueState.Completed?QueueEventResult.Succeeded:QueueEventResult.ReviewRequired,row.State,row.Incident,null),new(row.ItemId,1,Utc.AddMinutes(-20),QueueStage.Observation,QueueEventCode.Observed,QueueEventResult.Recorded,QueueState.Observed,QueueIncident.None,null)],false,
            correction?[new("synthetic-choice","Retrato · 768 × 960 (4:5)","Retrato",768,960)]:[],preview,Pattern(false),correction,mode=="exception",mode=="approved"));
    }
    internal static byte[] Pattern(bool border)
    {
        using var image=new Image<Rgba32>(128,160);
        for(var y=0;y<160;y++)for(var x=0;x<128;x++)
        {
            var face=(x-64)*(x-64)+(y-55)*(y-55)<24*24;
            var shoulders=y>95&&Math.Abs(x-64)<(y-80)/2;
            image[x,y]=face?new(177,206,206,255):shoulders?new(76,127,143,255):new((byte)(12+x/8),(byte)(35+y/5),(byte)(49+y/6),255);
            if(border&&(y<3||y>=157))image[x,y]=new(47,78,87,255);
        }
        using var memory=new System.IO.MemoryStream();image.SaveAsPng(memory);return memory.ToArray();
    }
    public Task MutateAsync(UniverseId universe,AutomationOperation op,object request,CancellationToken cancellation)=>throw new InvalidOperationException("Synthetic rendering cannot write.");
}

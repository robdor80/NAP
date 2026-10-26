using System.Security.Cryptography;
using NAP.Core;
using NAP.Presentation;
using Xunit;
namespace NAP.Tests;

// Small metadata and read-model cases only. No ZIP, provider, production or asset processing.
public sealed class Phase6PolishTests
{
    [Theory]
    [InlineData(0,24)]
    [InlineData(3,24)]
    [InlineData(24,0)]
    public void DurableStreamsPageIndependentlyAndNewRecordsDoNotShiftOpenPages(int decisions,int extraTransitions)
    {
        using var f=new PreparationTestFixture();using var owner=f.Queue.AcquireOwner();
        var item=f.Queue.Observe(owner,f.Context.Id,Path.Combine(f.Roots.InboxRoot,"metadata-only.zip"),new(new(new string('a',64)),10));
        item=f.Queue.Transition(owner,item.Id,item.Revision,QueueState.Queued);
        var rule=f.Context.Profile.AssetRules.Single();
        var profile=EffectiveRuleSnapshot.Create(f.Context.Id,1,rule.AssetType,rule.ProductionProfile,UniverseProfileSnapshotRenderer.Render(f.Context.Profile));
        var attempt=f.Queue.CreateAttempt(owner,item.Id,item.Revision,profile);
        item=f.Queue.Transition(owner,item.Id,f.Queue.Get(item.Id).Revision,QueueState.NeedsReview,QueueIncident.RatioUnsupported);
        var utc=new DateTimeOffset(2026,10,22,0,0,0,TimeSpan.Zero);
        ExceptionalConfigurationDecision Decision(int i)=>new("decision_"+Guid.NewGuid().ToString("N"),1,item.Id,attempt.Id,f.Context.Id,rule.ProductionProfile,profile,
            new(rule.Conversion!.OutputWidth,rule.Conversion.OutputHeight),rule.Conversion.OutputWidth,rule.Conversion.OutputHeight,
            new(new string('f',64)),ExceptionalScope.SingleAsset,ExceptionalDecisionState.Proposed,null,utc.AddSeconds(i));
        for(var i=0;i<decisions;i++)f.Queue.RecordDecision(owner,f.Queue.Get(item.Id).Revision,Decision(i));
        for(var i=0;i<extraTransitions;i++)
        {
            item=f.Queue.Get(item.Id);
            f.Queue.Transition(owner,item.Id,item.Revision,item.State==QueueState.NeedsReview?QueueState.Queued:QueueState.NeedsReview,
                item.State==QueueState.NeedsReview?QueueIncident.None:QueueIncident.RatioUnsupported);
        }
        if(f.Queue.Get(item.Id).State!=QueueState.NeedsReview)f.Queue.Transition(owner,item.Id,f.Queue.Get(item.Id).Revision,QueueState.NeedsReview,QueueIncident.RatioUnsupported);
        var reader=f.Queue;
        var before=SHA256.HashData(File.ReadAllBytes(f.Queue.DatabasePath));
        var first=reader.BrowseWorkflowActivity(new(item.Id));
        Assert.Equal(Math.Min(20,decisions),first.Decisions!.Count);Assert.Equal(decisions>20,first.MoreDecisions);
        Assert.Equal(20,first.Events.Count);Assert.True(first.MoreEvents);
        var cursor=new WorkflowDetailRequest(item.Id,20,0,first.EventThrough,first.DecisionThrough);
        var eventPage=reader.BrowseWorkflowActivity(cursor);
        Assert.Equal(first.Decisions,eventPage.Decisions);Assert.Empty(first.Events.Select(e=>e.Sequence).Intersect(eventPage.Events.Select(e=>e.Sequence)));
        var decisionPage=reader.BrowseWorkflowActivity(cursor with{EventOffset=0,DecisionOffset=20});
        Assert.Equal(first.Events,decisionPage.Events);
        Assert.Equal(Enumerable.Range(0,decisions).Reverse().Select(i=>utc.AddSeconds(i)),first.Decisions.Concat(decisionPage.Decisions!).Select(d=>d.Utc));
        Assert.False(decisionPage.MoreDecisions);
        Assert.Equal(before,SHA256.HashData(File.ReadAllBytes(f.Queue.DatabasePath)));
        // Append a later record after opening the reader: both old pages retain their exact contents.
        var appended=Decision(decisions+1);f.Queue.RecordDecision(owner,f.Queue.Get(item.Id).Revision,appended);
        Assert.Equal(appended,f.Queue.LatestReviewDecision(item.Id));
        if(decisions>0)
        {
            // A later revocation of an older proposal does not replace the newest proposal.
            var older=f.Queue.BrowseDecisions(item.Id,0).Last();
            f.Queue.RecordDecision(owner,f.Queue.Get(item.Id).Revision,older with{Version=2,State=ExceptionalDecisionState.Revoked,Actor="reviewer",Utc=utc.AddHours(1)});
            Assert.Equal(appended,f.Queue.LatestReviewDecision(item.Id));
        }
        before=SHA256.HashData(File.ReadAllBytes(f.Queue.DatabasePath));
        var stable=reader.BrowseWorkflowActivity(cursor);Assert.Equal(eventPage.Events,stable.Events);Assert.Equal(first.Decisions,stable.Decisions);
        var latest=reader.BrowseWorkflowActivity(new(item.Id));Assert.NotEqual(first.EventThrough,latest.EventThrough);Assert.NotEqual(first.DecisionThrough,latest.DecisionThrough);
        var exhausted=reader.BrowseWorkflowActivity(cursor with{EventOffset=100});Assert.Empty(exhausted.Events);Assert.False(exhausted.MoreEvents);Assert.Equal(decisions>20,exhausted.MoreDecisions);
        Assert.Throws<AutomationException>(()=>reader.BrowseWorkflowActivity(new(item.Id,DecisionOffset:-1)));
        Assert.Throws<AutomationException>(()=>reader.BrowseWorkflowActivity(new(item.Id,EventThrough:long.MaxValue)));
        Assert.Equal(before,SHA256.HashData(File.ReadAllBytes(f.Queue.DatabasePath)));
        Assert.Empty(Directory.EnumerateFiles(f.Root,"*.zip",SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(0,0,0,"Todo al día")]
    [InlineData(1,0,0,"Automatización activa")]
    [InlineData(0,1,0,"Procesando imágenes")]
    [InlineData(0,0,1,"1 necesita revisión")]
    [InlineData(1,1,1,"Procesando imágenes")]
    [InlineData(0,0,2,"2 necesitan revisión")]
    public void RealSummaryNeverSaysUpToDateWithWorkOrIncidents(int pending,int processing,int review,string label)=>
        Assert.Equal(label,AutomationControlViewModel.Describe(AutomationHostState.Running,new(pending,processing,1,review,0)));

    [Fact]
    public async Task IndependentClientOffsetsKeepScopeBoundsAndNavigationIdentity()
    {
        var root=Path.Combine(Path.GetTempPath(),"nap-polish-display-only");
        UniverseContext Context(string id)=>new(new UniverseProfile(new(id),id),new(new(id),Path.Combine(root,id,"work"),Path.Combine(root,id,"production"),Path.Combine(root,id,"archive")));
        var context=Context("alpha");var client=new Source(context.Id);
        using var vm=new WorkflowViewModel(client,()=>context,new NoConfirmation());
        await vm.LoadPageAsync();vm.Selected=vm.Rows[0];await vm.LastDetail;
        await vm.NextEvents.ExecuteAsync();Assert.Equal(20,client.Last!.EventOffset);Assert.Equal(0,client.Last.DecisionOffset);
        Assert.Equal(44L,client.Last.EventThrough);Assert.Equal(24L,client.Last.DecisionThrough);
        await vm.NextDecisions.ExecuteAsync();Assert.Equal(20,client.Last.DecisionOffset);Assert.Equal(20,client.Last.EventOffset);
        Assert.Equal(client.Id,client.Last.ItemId);Assert.Equal("21–24",vm.DecisionPageLabel);Assert.False(vm.NextDecisions.CanExecute(null));Assert.True(vm.PreviousDecisions.CanExecute(null));
        await vm.PreviousEvents.ExecuteAsync();Assert.Equal(0,client.Last.EventOffset);Assert.Equal(20,client.Last.DecisionOffset);
        var gate=new TaskCompletionSource<WorkflowDetail>();var old=client.Reply(client.Last);
        client.Read=()=>gate.Task;var pending=vm.PreviousDecisions.ExecuteAsync();
        context=Context("beta");vm.ContextChanged();Assert.True(client.Token.IsCancellationRequested);gate.SetResult(old);await pending;
        Assert.False(vm.HasDetail);Assert.False(vm.PreviousDecisions.CanExecute(null));
        client.Read=null;client.Universe=context.Id;await vm.LoadPageAsync();vm.Selected=vm.Rows[0];await vm.LastDetail;
        Assert.Equal(0,client.Last!.EventOffset);Assert.Equal(0,client.Last.DecisionOffset);Assert.Null(client.Last.EventThrough);Assert.Null(client.Last.DecisionThrough);
        Assert.Equal(context.Id,client.LastUniverse);Assert.Equal(client.Id,client.Last.ItemId);
    }
    [Fact]
    public async Task ExtendedReadContractRoundTripsThroughTheAuthenticatedHost()
    {
        using var f=new AutomationHostTests.Fixture();QueueItem item;
        using(var owner=f.A.Queue.AcquireOwner())item=f.A.Queue.Observe(owner,f.A.Context.Id,Path.Combine(f.A.Roots.InboxRoot,"metadata.zip"),new(new(new string('a',64)),10));
        await using var host=new AutomationHost(f.Control,[f.A.Context,f.B.Context],f.Options);await host.Started;
        var bridge=new AutomationDesktopBridge(host,[f.A.Context,f.B.Context],Path.Combine(f.Root,"desktop-requests"));
        var connection=new AutomationConnection("nap-polish-"+Guid.NewGuid().ToString("N"),Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        await using var server=new AutomationPipeServer(connection,bridge.HandleAsync);var client=new WorkflowDesktopClient(new(connection));
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var first=await client.DetailAsync(f.A.Context.Id,new(item.Id),timeout.Token);
        Assert.Equal(1L,first.EventThrough);Assert.Equal(0L,first.DecisionThrough);Assert.False(first.MoreDecisions);
        var second=await client.DetailAsync(f.A.Context.Id,new(item.Id,DecisionOffset:20,EventThrough:first.EventThrough,DecisionThrough:first.DecisionThrough),timeout.Token);
        Assert.Single(second.Events);Assert.Empty(second.Decisions!);
        await Assert.ThrowsAsync<AutomationException>(()=>client.DetailAsync(f.B.Context.Id,new(item.Id,EventThrough:first.EventThrough),timeout.Token));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(f.Root,"desktop-requests")));
        Assert.Empty(Directory.EnumerateFiles(f.Root,"*.zip",SearchOption.AllDirectories));
    }
    private sealed class NoConfirmation:IUserConfirmation
    {public Task<bool> ConfirmAsync(UiConfirmation c,CancellationToken ct)=>throw new InvalidOperationException("Read-only browsing never confirms.");}
    private sealed class Source(UniverseId universe):IWorkflowDesktopClient
    {
        public UniverseId Universe=universe;public QueueItemId Id=QueueItemId.Create();public WorkflowDetailRequest? Last;public UniverseId? LastUniverse;public CancellationToken Token;public Func<Task<WorkflowDetail>>? Read;
        private WorkflowRow Row()=>new(Id,Universe,"Retrato",QueueState.NeedsReview,QueueIncident.RatioUnsupported,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,null,1);
        public WorkflowDetail Reply(WorkflowDetailRequest r)=>new(Universe,Row(),Enumerable.Range(1,44).Reverse().Skip(r.EventOffset).Take(20).Select(i=>new QueueEvent(Id,i,DateTimeOffset.UtcNow,QueueStage.Review,QueueEventCode.EvidenceRecorded,QueueEventResult.Recorded,QueueState.NeedsReview,QueueIncident.RatioUnsupported,null)).ToArray(),r.EventOffset+20<44,[],null,null,false,false,false,
            Enumerable.Range(1,24).Skip(r.DecisionOffset).Take(20).Select(i=>new WorkflowDecisionEntry(DateTimeOffset.UtcNow,ExceptionalDecisionState.Proposed,"Retrato",40,50)).ToArray(),MoreDecisions:r.DecisionOffset+20<24,EventThrough:44,DecisionThrough:24);
        public Task<WorkflowPage> PageAsync(UniverseId u,WorkflowPageRequest r,CancellationToken ct)=>Task.FromResult(new WorkflowPage(Universe,0,1,[Row()]));
        public Task<IReadOnlyList<WorkflowBatch>> BatchesAsync(UniverseId u,CancellationToken ct)=>Task.FromResult<IReadOnlyList<WorkflowBatch>>([]);
        public Task<WorkflowDetail> DetailAsync(UniverseId u,WorkflowDetailRequest r,CancellationToken ct){LastUniverse=u;Last=r;Token=ct;return Read?.Invoke()??Task.FromResult(Reply(r));}
        public Task MutateAsync(UniverseId u,AutomationOperation op,object r,CancellationToken ct)=>throw new InvalidOperationException("Read-only source.");
    }
}

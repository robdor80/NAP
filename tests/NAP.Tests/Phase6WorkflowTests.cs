using System.Security.Cryptography;
using NAP.Core;
using NAP.Presentation;
using Xunit;
namespace NAP.Tests;

public sealed class Phase6WorkflowTests
{
    [Fact]
    public async Task AuthenticatedPagingFiltersAndEventsAreScopedBoundedAndReadOnly()
    {
        using var f=new AutomationHostTests.Fixture();QueueItem review;
        using(var owner=f.A.Queue.AcquireOwner())
        {
            review=f.A.Queue.Observe(owner,f.A.Context.Id,Path.Combine(f.A.Roots.InboxRoot,"retrato.zip"),new(new(new string('a',64)),10));
            review=f.A.Queue.Transition(owner,review.Id,review.Revision,QueueState.NeedsReview,QueueIncident.AuditFail);
            f.A.Queue.Observe(owner,f.A.Context.Id,Path.Combine(f.A.Roots.InboxRoot,"mapa.zip"),new(new(new string('b',64)),10));
        }
        await using var host=new AutomationHost(f.Control,[f.A.Context,f.B.Context],f.Options);await host.Started;
        var bridge=new AutomationDesktopBridge(host,[f.A.Context,f.B.Context],Path.Combine(f.Root,"desktop-requests"));
        var connection=Connection();await using var server=new AutomationPipeServer(connection,bridge.HandleAsync);var client=new WorkflowDesktopClient(new(connection));
        await host.QueryAsync(f.A.Context.Id,s=>s.Queue.CurrentPolicy());
        var before=SHA256.HashData(File.ReadAllBytes(f.A.Queue.DatabasePath));using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var page=await client.PageAsync(f.A.Context.Id,new(Filter:WorkflowFilter.Review),timeout.Token);
        Assert.Equal(1,page.Total);Assert.Equal(review.Id,Assert.Single(page.Items).ItemId);Assert.Equal("retrato",page.Items[0].Name);
        Assert.Equal(2,(await client.PageAsync(f.A.Context.Id,new(Limit:1),timeout.Token)).Total);
        Assert.Empty((await client.PageAsync(f.B.Context.Id,new(),timeout.Token)).Items);
        Assert.Single((await client.PageAsync(f.A.Context.Id,new(Search:"mapa"),timeout.Token)).Items);
        var detail=await client.DetailAsync(f.A.Context.Id,new(review.Id),timeout.Token);
        Assert.Equal(2,detail.Events.Count);Assert.False(detail.CanApprove);Assert.False(detail.CanContinue);Assert.Empty(detail.Choices);
        await Assert.ThrowsAsync<AutomationException>(()=>client.DetailAsync(f.B.Context.Id,new(review.Id),timeout.Token));
        await Assert.ThrowsAsync<AutomationException>(()=>client.PageAsync(f.A.Context.Id,new(Limit:41),timeout.Token));
        Assert.Equal(before,SHA256.HashData(File.ReadAllBytes(f.A.Queue.DatabasePath)));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(f.Root,"desktop-requests")));
        Assert.Empty(Directory.EnumerateFiles(f.Root,"*.zip",SearchOption.AllDirectories));
    }
    [Fact]
    public async Task OneSyntheticAssetPersistsVerifiedDecisionAndContinuesWithoutAuditOverrideOrGlobalChange()
    {
        // One functional asset, not a ZIP volume fixture. No publisher or productive policy is enabled.
        using var f=new AutomationHostTests.Fixture();QueueItemId item;JobId job;StoredAutomationPolicy policy;string input;
        var blocked=new List<QueueItemId>();
        using(var owner=f.A.Queue.AcquireOwner())
        {
            policy=f.A.Policy(owner,operations:AutomationOperations.Admit|AutomationOperations.Normalize,authorizePreparation:true);
            input=f.A.Zip("exception_single_asset.zip",width:39,height:50);
            var admitted=f.A.Admit(owner,input);item=admitted.ItemId!;job=admitted.JobId!;
            Assert.Equal(PreparationCode.GeometryPolicyRequired,f.A.Service.Prepare(owner,item).Code);
            foreach(var entry in new[]{('b',QueueIncident.AuditWarning),('c',QueueIncident.AuditFail)})
            {
                var value=f.A.Queue.Observe(owner,f.A.Context.Id,Path.Combine(f.A.Roots.InboxRoot,entry.Item1+"-metadata-only.zip"),new(new(new string(entry.Item1,64)),10));
                f.A.Queue.Transition(owner,value.Id,value.Revision,QueueState.NeedsReview,entry.Item2);blocked.Add(value.Id);
            }
        }
        var original=File.ReadAllBytes(input);var profile=UniverseProfileSnapshotRenderer.Render(f.A.Context.Profile);
        await using var host=new AutomationHost(f.Control,[f.A.Context,f.B.Context],f.Options);await host.Started;
        await host.ActivateAsync(f.A.Context.Id,policy.Reference,false);var control=host.ReadControl(f.A.Context.Id);
        var bridge=new AutomationDesktopBridge(host,[f.A.Context,f.B.Context],Path.Combine(f.Root,"desktop-requests"));var connection=Connection();
        await using var server=new AutomationPipeServer(connection,bridge.HandleAsync);var wire=new AutomationHostClient(connection);var client=new WorkflowDesktopClient(wire);
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var detail=await client.DetailAsync(f.A.Context.Id,new(item),timeout.Token);Assert.True(detail.CanPropose);Assert.NotNull(detail.OriginalImage);
        var choice=Assert.Single(detail.Choices.Where(c=>c.Width==39&&c.Height==50));
        var proposal=new ReviewProposalRequest(item,detail.Item.Revision,choice.Key);var command="command_"+Guid.NewGuid().ToString("N");
        await wire.CallAsync(AutomationOperation.ReviewPropose,f.A.Context.Id.Value,proposal,timeout.Token,command);
        await wire.CallAsync(AutomationOperation.ReviewPropose,f.A.Context.Id.Value,proposal,timeout.Token,command);
        detail=await client.DetailAsync(f.A.Context.Id,new(item),timeout.Token);Assert.True(detail.CanApprove);Assert.False(detail.CanContinue);
        var preview=detail.Preview!;Assert.InRange(preview.CandidateImage.Length,1,300_000);
        var request=new ReviewDecisionRequest(item,detail.Item.Revision,preview.DecisionId,preview.Version,preview.Evidence,true);
        await Assert.ThrowsAsync<AutomationException>(()=>wire.CallAsync(AutomationOperation.ReviewApprove,f.A.Context.Id.Value,request with{Confirmed=false},timeout.Token));
        await Assert.ThrowsAsync<AutomationException>(()=>wire.CallAsync(AutomationOperation.ReviewApprove,f.A.Context.Id.Value,request with{Evidence=new(new string('0',64))},timeout.Token));
        await Assert.ThrowsAsync<AutomationException>(()=>wire.CallAsync(AutomationOperation.ReviewApprove,f.A.Context.Id.Value,request with{Version=99},timeout.Token));
        Assert.Single(await host.QueryAsync(f.A.Context.Id,s=>s.Queue.Decisions(item)));
        await wire.CallAsync(AutomationOperation.ReviewPropose,f.A.Context.Id.Value,new ReviewProposalRequest(item,detail.Item.Revision,choice.Key),timeout.Token);
        await Assert.ThrowsAsync<AutomationException>(()=>wire.CallAsync(AutomationOperation.ReviewApprove,f.A.Context.Id.Value,request,timeout.Token));
        detail=await client.DetailAsync(f.A.Context.Id,new(item),timeout.Token);
        Assert.Contains(detail.Decisions!,d=>d.State==ExceptionalDecisionState.Revoked);
        preview=detail.Preview!;request=new(item,detail.Item.Revision,preview.DecisionId,preview.Version,preview.Evidence,true);
        await wire.CallAsync(AutomationOperation.ReviewApprove,f.A.Context.Id.Value,request,timeout.Token);
        detail=await client.DetailAsync(f.A.Context.Id,new(item),timeout.Token);Assert.False(detail.CanApprove);Assert.True(detail.CanContinue);
        Assert.Equal(ExceptionalDecisionState.Approved,detail.Preview!.State);
        var persisted=await host.QueryAsync(f.A.Context.Id,s=>new DurablePreparationService(s.Context,s.Queue).ReadReviewPreview(item));Assert.Equal(detail.Preview.DecisionId,persisted!.DecisionId);
        foreach(var id in blocked)
        {
            var bad=await client.DetailAsync(f.A.Context.Id,new(id),timeout.Token);Assert.False(bad.CanPropose);Assert.False(bad.CanApprove);Assert.False(bad.CanContinue);
            await Assert.ThrowsAsync<AutomationException>(()=>wire.CallAsync(AutomationOperation.ReviewContinue,f.A.Context.Id.Value,new ReviewDecisionRequest(id,bad.Item.Revision,preview.DecisionId,2,preview.Evidence,true),timeout.Token));
            Assert.Equal(QueueState.NeedsReview,(await host.QueryAsync(f.A.Context.Id,s=>s.Queue.Get(id))).State);
        }
        var p=detail.Preview;
        var continued=AutomationProtocol.Read<PreparationResult>(await wire.CallAsync(AutomationOperation.ReviewContinue,f.A.Context.Id.Value,new ReviewDecisionRequest(item,detail.Item.Revision,p.DecisionId,p.Version,p.Evidence,true),timeout.Token));
        Assert.Equal(PreparationCode.Planned,continued.Code);Assert.Equal(job,continued.JobId);
        var record=await host.QueryAsync(f.A.Context.Id,s=>new DurablePreparationService(s.Context,s.Queue).State.Get(item));
        Assert.Equal(NormalizationAuthorityKind.HumanDecision,record!.Authority!.Kind);Assert.NotEqual(DurableAuditState.Pass,record.AuditState);
        Assert.Equal(original,File.ReadAllBytes(input));Assert.Equal(profile,UniverseProfileSnapshotRenderer.Render(f.A.Context.Profile));
        Assert.Equal(policy.Reference,(await host.QueryAsync(f.A.Context.Id,s=>s.Queue.CurrentPolicy())).Reference);
        Assert.Equal(control.Revision,host.ReadControl(f.A.Context.Id).Revision);Assert.False(host.ReadControl(f.A.Context.Id).Paused);
        Assert.Empty(Directory.EnumerateFiles(f.A.Roots.ProductionRoot,"*.webp",SearchOption.AllDirectories));f.A.AssertUntouched();
    }
    [Fact]
    public async Task RevokedPolicyBlocksIndividualContinuationBeforeAnyDecisionOrPreparationWrite()
    {
        using var f=new AutomationHostTests.Fixture();StoredAutomationPolicy policy;QueueItem item;
        using(var owner=f.A.Queue.AcquireOwner())
        {
            policy=f.A.Policy(owner,operations:AutomationOperations.Admit|AutomationOperations.Normalize,authorizePreparation:true);
            item=f.A.Queue.Observe(owner,f.A.Context.Id,Path.Combine(f.A.Roots.InboxRoot,"metadata-only.zip"),new(new(new string('a',64)),10));
            item=f.A.Queue.Transition(owner,item.Id,item.Revision,QueueState.NeedsReview,QueueIncident.RatioUnsupported);
        }
        await using var host=new AutomationHost(f.Control,[f.A.Context],f.Options);await host.Started;await host.ActivateAsync(f.A.Context.Id,policy.Reference,false);
        await host.QueryAsync(f.A.Context.Id,s=>s.Queue.SetPolicyState(s.Owner,policy.Reference,policy.Revision,AutomationPolicyState.Revoked));
        await Assert.ThrowsAsync<AutomationException>(()=>host.RunReviewAsync<bool>(f.A.Context.Id,item.Id,true,(s,p)=>throw new InvalidOperationException("Must not reach writer.")));
        var after=await host.QueryAsync(f.A.Context.Id,s=>s.Queue.Get(item.Id));Assert.Equal(item.Revision,after.Revision);Assert.Empty(await host.QueryAsync(f.A.Context.Id,s=>s.Queue.Decisions(item.Id)));
        Assert.Empty(Directory.EnumerateFiles(f.Root,"*.zip",SearchOption.AllDirectories));
    }
    [Fact]
    public async Task LateDetailAndPageRepliesCannotCrossUniverseAndCancelTheOldRead()
    {
        var a=Context("alpha");var b=Context("beta");UniverseContext current=a;var source=new Fake();source.Value=Detail(a.Id);
        using var vm=new WorkflowViewModel(source,()=>current,new Confirm(false));await vm.LoadPageAsync();
        var gate=new TaskCompletionSource<WorkflowDetail>();source.ReadDetail=()=>gate.Task;
        vm.Selected=vm.Rows[0];var pending=vm.LastDetail;current=b;vm.ContextChanged();Assert.True(source.Token.IsCancellationRequested);
        gate.SetResult(source.Value);await pending;Assert.False(vm.HasDetail);Assert.Empty(vm.Rows);Assert.Null(vm.OriginalImage);
        source.ReadDetail=null;source.Value=Detail(b.Id);await vm.LoadPageAsync();Assert.All(vm.Rows,r=>Assert.Equal(b.Id,r.Value.UniverseId));
    }
    [Fact]
    public async Task ExplicitApprovalBindsPreviewAndDoesNotInventHostOutcome()
    {
        var c=Context("alpha");var source=new Fake{Value=Detail(c.Id)};var confirmation=new Confirm(false);
        using var vm=new WorkflowViewModel(source,()=>c,confirmation);await vm.LoadPageAsync();vm.Selected=vm.Rows[0];await vm.LastDetail;
        await vm.Approve.ExecuteAsync();Assert.Empty(source.Sent);Assert.Equal(c.Profile.DisplayName,confirmation.Last!.UniverseName);
        confirmation.Accept=true;await vm.Approve.ExecuteAsync();var sent=Assert.Single(source.Sent);var request=Assert.IsType<ReviewDecisionRequest>(sent.Request);
        Assert.Equal(source.Value.Preview!.Evidence,request.Evidence);Assert.True(request.Confirmed);Assert.False(vm.CanContinue);
        source.FailMutation=true;await vm.Approve.ExecuteAsync();Assert.Equal(2,source.Sent.Count);Assert.False(vm.HasDetail);Assert.False(vm.Approve.CanExecute(null));Assert.Contains("sin confirmar",vm.DetailStatus);
    }
    [Fact]
    public async Task ForeignSnapshotsAndDisconnectedReadsClearActionsWithoutFallback()
    {
        var c=Context("alpha");var source=new Fake{Value=Detail(new("beta"))};using var vm=new WorkflowViewModel(source,()=>c,new Confirm(true));
        await vm.LoadPageAsync();Assert.Empty(vm.Rows);Assert.False(vm.HasDetail);
        source.Value=Detail(c.Id);await vm.LoadPageAsync();vm.Selected=vm.Rows[0];await vm.LastDetail;Assert.True(vm.CanApprove);
        source.FailRead=true;await vm.LoadPageAsync();Assert.Empty(vm.Rows);Assert.False(vm.CanApprove);Assert.Empty(source.Sent);
    }
    [Fact]
    public async Task OpeningAnIncidentOutsideTheVisiblePageKeepsItsExactIdentityAndPollingPreservesEdits()
    {
        var c=Context("alpha");var value=Detail(c.Id);var source=new Fake{Value=value,PageItem=value.Item with{ItemId=QueueItemId.Create(),Name="Otro asset"}};
        using var vm=new WorkflowViewModel(source,()=>c,new Confirm(true));await vm.LoadPageAsync();
        await vm.OpenItemAsync(value.Item.ItemId);Assert.True(vm.HasDetail);Assert.Equal(value.Item.Name,vm.Name);
        await vm.Approve.ExecuteAsync();Assert.Equal(value.Item.ItemId,Assert.IsType<ReviewDecisionRequest>(Assert.Single(source.Sent).Request).ItemId);
        var reads=source.PageReads;vm.Choice=vm.Choices[0];await vm.Poll.ExecuteAsync();Assert.Equal(reads,source.PageReads);Assert.NotNull(vm.Choice);
        vm.CloseDetail.Execute(null);Assert.False(vm.HasDetail);Assert.True(vm.Poll.CanExecute(null));await vm.Poll.ExecuteAsync();Assert.Equal(reads+1,source.PageReads);
    }
    [Fact]
    public async Task UniverseChangeWhileConfirmingNeverSendsAnApprovalForTheOldScreen()
    {
        UniverseContext c=Context("alpha");var source=new Fake{Value=Detail(c.Id)};var confirmation=new Confirm(true);var gate=new TaskCompletionSource<bool>();confirmation.Response=gate.Task;
        using var vm=new WorkflowViewModel(source,()=>c,confirmation);await vm.LoadPageAsync();vm.Selected=vm.Rows[0];await vm.LastDetail;
        var pending=vm.Approve.ExecuteAsync();c=Context("beta");vm.ContextChanged();gate.SetResult(true);await pending;Assert.Empty(source.Sent);Assert.False(vm.HasDetail);
    }
    private static AutomationConnection Connection()=>new("nap-phase6-review-"+Guid.NewGuid().ToString("N"),Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
    private static UniverseContext Context(string name){var root=Path.Combine(Path.GetTempPath(),"phase6-display-only",name);return new(new UniverseProfile(new(name),"Universo "+name),new(new(name),Path.Combine(root,"workspace"),Path.Combine(root,"production"),Path.Combine(root,"archive")));}
    private static WorkflowDetail Detail(UniverseId u)
    {
        var id=QueueItemId.Create();var now=DateTimeOffset.UtcNow;var row=new WorkflowRow(id,u,"Retrato de muestra",QueueState.NeedsReview,QueueIncident.RatioUnsupported,now,now,null,2);
        return new(u,row,[],false,[new("choice","Retrato · 39 × 50","Retrato",39,50)],new("decision_"+Guid.NewGuid().ToString("N"),1,new(new string('a',64)),ExceptionalDecisionState.Proposed,39,50,39,50,39,50,0,0,0,0,"Retrato",[],[]),null,true,true,false);
    }
    private sealed class Confirm(bool accept):IUserConfirmation
    {
        public bool Accept=accept;public UiConfirmation? Last;public Task<bool>? Response;
        public Task<bool> ConfirmAsync(UiConfirmation c,CancellationToken ct){Last=c;return Response??Task.FromResult(Accept);}
    }
    private sealed class Fake:IWorkflowDesktopClient
    {
        public WorkflowDetail Value=null!;public WorkflowRow? PageItem;public int PageReads;public bool FailRead,FailMutation;public Func<Task<WorkflowDetail>>? ReadDetail;public CancellationToken Token;
        public List<(AutomationOperation Operation,object Request)> Sent=[];
        public Task<WorkflowPage> PageAsync(UniverseId u,WorkflowPageRequest r,CancellationToken ct){PageReads++;return FailRead?throw new IOException("private/path"):Task.FromResult(new WorkflowPage(Value.UniverseId,r.Offset,1,[PageItem??Value.Item]));}
        public Task<IReadOnlyList<WorkflowBatch>> BatchesAsync(UniverseId u,CancellationToken ct)=>Task.FromResult<IReadOnlyList<WorkflowBatch>>([]);
        public Task<WorkflowDetail> DetailAsync(UniverseId u,WorkflowDetailRequest r,CancellationToken ct){Token=ct;return ReadDetail?.Invoke()??Task.FromResult(Value);}
        public Task MutateAsync(UniverseId u,AutomationOperation op,object request,CancellationToken ct){Sent.Add((op,request));if(FailMutation)throw new IOException("unknown accepted effect");return Task.CompletedTask;}
    }
}

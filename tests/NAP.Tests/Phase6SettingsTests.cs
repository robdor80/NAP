using System.Security.Cryptography;
using NAP.Core;
using NAP.Presentation;
using Xunit;
namespace NAP.Tests;

// Small synthetic metadata cases. No ZIP files, provider calls, production or machine clock changes.
public sealed class Phase6SettingsTests
{
    [Theory]
    [InlineData("2027-10-19",0)]
    [InlineData("2027-10-20",1)]
    [InlineData("2027-11-15",1)]
    [InlineData("2027-12-19",2)]
    [InlineData("2028-01-01",2)]
    [InlineData("2028-01-11",3)]
    [InlineData("2028-01-17",3)]
    [InlineData("2028-01-18",4)]
    [InlineData("2028-06-01",4)]
    public void ThresholdsCatchUpWithoutChangingTheMachineClock(string date,int reminders)
    {
        var today=DateOnly.Parse(date);var value=LicenseRegister.Evaluate(LicenseRegister.Initial(),today);
        var license=Assert.Single(value.Licenses);Assert.Equal(reminders,license.Reminders.Count);
        Assert.Equal(reminders,LicenseRegister.Evaluate(value,today).Licenses.Single().Reminders.Count);
        Assert.Equal(reminders==0?(LicenseReminderKind?)null:(LicenseReminderKind)(reminders-1),LicenseRegister.Pending(license)?.Kind);
    }
    [Fact]
    public async Task OrdinaryStartupPersistenceAttendedRestartAndRenewalRequireNoHost()
    {
        var root=Path.Combine(Path.GetTempPath(),"nap-license-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var path=Path.Combine(root,"administrative.json");var today=new DateOnly(2027,12,23);
            var vm=new LicenseViewModel(new LicenseRegister(path),()=>today);await vm.CheckAsync();
            Assert.NotEmpty(vm.PendingNotice);Assert.Equal(2,vm.Selected!.Value.Reminders.Count);
            await vm.Attend.ExecuteAsync();Assert.Empty(vm.PendingNotice);
            var store=new LicenseRegister(path);var bytes=File.ReadAllBytes(path);Assert.Null(LicenseRegister.Pending(store.Check(today).Licenses.Single()));
            Assert.Equal(bytes,File.ReadAllBytes(path));
            today=new(2028,1,12);var restarted=new LicenseViewModel(new LicenseRegister(path),()=>today);await restarted.CheckAsync();
            Assert.Equal(LicenseReminderKind.SevenDays,LicenseRegister.Pending(restarted.Selected!.Value)!.Kind);
            restarted.RenewalDate=new DateTime(2029,1,18);await restarted.Renew.ExecuteAsync();
            Assert.Empty(restarted.PendingNotice);Assert.Single(restarted.Selected!.Value.Renewals);
            Assert.Equal(new DateOnly(2028,10,20),LicenseRegister.Threshold(restarted.Selected.Value.Expiry,LicenseReminderKind.NinetyDays));
            Assert.Equal(new DateOnly(2029,1,18),new LicenseRegister(path).Read().Licenses.Single().Expiry);
            Assert.DoesNotContain("LicenseKey",File.ReadAllText(path));Assert.Single(Directory.GetFiles(root));
        }
        finally{Directory.Delete(root,true);}
    }
    [Fact]
    public void CorruptOrUnknownRegisterIsNeverOverwrittenAndRenewalMustAdvanceExpiry()
    {
        var root=Path.Combine(Path.GetTempPath(),"nap-license-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var path=Path.Combine(root,"administrative.json");File.WriteAllText(path,"{\"Version\":99,\"Licenses\":[]}");var bytes=File.ReadAllBytes(path);var store=new LicenseRegister(path);
            Assert.Throws<InvalidDataException>(()=>store.Check(new(2028,1,20)));Assert.Equal(bytes,File.ReadAllBytes(path));
            File.Delete(path);store.Check(new(2027,1,1));
            Assert.Throws<ArgumentException>(()=>store.Renew("imagesharp-community",new(2027,12,1),new(2027,1,1)));
            Assert.Throws<ArgumentException>(()=>new LicenseRegister(Path.Combine(root,"sixlabors.lic")));
        }
        finally{Directory.Delete(root,true);}
    }
    [Fact]
    public void DurableDiagnosticFiltersUseStableBoundedReadOnlyPages()
    {
        using var f=new PreparationTestFixture();using var owner=f.Queue.AcquireOwner();
        var item=f.Queue.Observe(owner,f.Context.Id,Path.Combine(f.Roots.InboxRoot,"metadata.zip"),new(new(new string('a',64)),10));
        for(var n=0;n<32;n++)item=f.Queue.Observe(owner,f.Context.Id,Path.Combine(f.Roots.InboxRoot,$"metadata-{n}.zip"),new(new((n+1).ToString("x64")),10));
        item=f.Queue.Transition(owner,item.Id,item.Revision,QueueState.NeedsReview,QueueIncident.ProfileUnknown);
        var hash=SHA256.HashData(File.ReadAllBytes(f.Queue.DatabasePath));var reader=new AutomationQueueStore(f.Roots);
        var first=reader.BrowseDiagnostics(new());Assert.Equal(30,first.Events.Count);Assert.True(first.More);
        var next=reader.BrowseDiagnostics(new(Before:first.Events.Last().Cursor,Through:first.Through));Assert.False(next.More);
        Assert.Empty(first.Events.Select(e=>e.Cursor).Intersect(next.Events.Select(e=>e.Cursor)));
        Assert.Equal(34,first.Events.Count+next.Events.Count);
        Assert.Single(reader.BrowseDiagnostics(new(Severity:DiagnosticSeverity.Warning)).Events);
        Assert.Empty(reader.BrowseDiagnostics(new(Severity:DiagnosticSeverity.Error)).Events);
        Assert.Single(reader.BrowseDiagnostics(new(Category:QueueStage.Review)).Events);
        Assert.Empty(reader.BrowseDiagnostics(new(From:DateTimeOffset.UtcNow.AddDays(1))).Events);
        Assert.Throws<AutomationException>(()=>reader.BrowseDiagnostics(new(From:DateTimeOffset.UtcNow,Until:DateTimeOffset.UtcNow.AddDays(-1))));
        Assert.Equal(hash,SHA256.HashData(File.ReadAllBytes(f.Queue.DatabasePath)));
        item=f.Queue.Observe(owner,f.Context.Id,Path.Combine(f.Roots.InboxRoot,"later.zip"),new(new(new string('b',64)),10));
        Assert.Equal(first.Events,reader.BrowseDiagnostics(new(Through:first.Through)).Events);
        Assert.Empty(Directory.GetFiles(f.Root,"*.zip",SearchOption.AllDirectories));
    }
    [Fact]
    public async Task OfflineReportUsesClosedVocabularyAndExcludesAllFreeTextAndSensitiveIdentifiers()
    {
        var secret="token=private-token /home/alice/secret C:\\Users\\Alice\\file queue_private sixlabors.lic";
        var row=new DiagnosticRow(null,DiagnosticSeverity.Error,null,null,null,null,null,secret);
        using var vm=new DiagnosticViewModel(null,()=>new UniverseId("private_universe"),()=>[row]);
        await vm.Refresh.ExecuteAsync();Assert.Single(vm.Rows);Assert.Contains("Sin fecha",vm.Rows[0].When);
        foreach(var denied in new[]{"private-token","alice","Alice","private_universe","sixlabors.lic","queue_private"})Assert.DoesNotContain(denied,vm.Report);
        vm.From=new(2026,10,1);await vm.Refresh.ExecuteAsync();Assert.Empty(vm.Rows); // No invented date for local notices.
        vm.From=null;vm.Severity=vm.Severities.Single(s=>s.Value==DiagnosticSeverity.Warning);await vm.ShowLocalAsync();Assert.Empty(vm.Rows);
    }
    [Fact]
    public async Task HostReadFailuresAndForeignOrLateResponsesCannotLeakAcrossUniverses()
    {
        var a=new UniverseId("synthetic_a");var b=new UniverseId("synthetic_b");var selected=a;
        var pending=new TaskCompletionSource<DiagnosticPage>();var client=new FakeDiagnostics((u,q,ct)=>pending.Task);
        using var vm=new DiagnosticViewModel(client,()=>selected,()=>[]);var read=vm.Refresh.ExecuteAsync();selected=b;vm.ContextChanged();pending.SetResult(new(a,[],false,0));await read;Assert.Empty(vm.Rows);
        client.Read=(_,_,_)=>Task.FromResult(new DiagnosticPage(a,[],false,0));await vm.Refresh.ExecuteAsync();Assert.Contains("No se pudo",vm.Notice);
        client.Read=(_,_,_)=>throw new IOException("secret path");await vm.Refresh.ExecuteAsync();Assert.DoesNotContain("secret",vm.Report);await vm.ShowLocalAsync();Assert.Contains("local",vm.Source);
        using var broken=new DiagnosticViewModel(null,()=>selected,()=>throw new IOException("private local secret"));
        await broken.ShowLocalAsync();Assert.Contains("No se pudieron",broken.Notice);Assert.DoesNotContain("private",broken.Report);
    }
    [Fact]
    public void ReportExportCreatesOnlyANewTextFileAndNeverOverwritesAnySource()
    {
        using var f=new PreparationTestFixture();using var owner=f.Queue.AcquireOwner();var output=Path.Combine(f.Root,"report.txt");var admin=Path.Combine(f.Root,"application");
        DiagnosticReportWriter.Export(output,"Informe anonimizado",[f.Roots],admin);var bytes=File.ReadAllBytes(output);
        Assert.Throws<IOException>(()=>DiagnosticReportWriter.Export(output,"replace",[f.Roots],admin));Assert.Equal(bytes,File.ReadAllBytes(output));
        Assert.Throws<InvalidDataException>(()=>DiagnosticReportWriter.Export(Path.Combine(f.Roots.WorkspaceRoot,"report.txt"),"data",[f.Roots],admin));
        Assert.Throws<InvalidDataException>(()=>DiagnosticReportWriter.Export(Path.Combine(admin,"report.txt"),"data",[f.Roots],admin));
        Assert.Throws<InvalidDataException>(()=>DiagnosticReportWriter.Export(Path.Combine(f.Root,"sixlabors.lic"),"data",[f.Roots],admin));
        f.AssertUntouched();
    }
    [Fact]
    public async Task DiagnosticContractUsesAuthenticatedReadWithoutCreatingMutationReceipts()
    {
        using var f=new AutomationHostTests.Fixture();
        using(var owner=f.A.Queue.AcquireOwner())f.A.Queue.Observe(owner,f.A.Context.Id,Path.Combine(f.A.Roots.InboxRoot,"metadata.zip"),new(new(new string('a',64)),10));
        await using var host=new AutomationHost(f.Control,[f.A.Context,f.B.Context],f.Options);await host.Started;
        var requests=Path.Combine(f.Root,"desktop-requests");var bridge=new AutomationDesktopBridge(host,[f.A.Context,f.B.Context],requests);
        var connection=new AutomationConnection("nap-diagnostics-"+Guid.NewGuid().ToString("N"),Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        await using var server=new AutomationPipeServer(connection,bridge.HandleAsync);var client=new DiagnosticDesktopClient(new(connection));
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var page=await client.ReadAsync(f.A.Context.Id,new(),timeout.Token);Assert.Single(page.Events);Assert.Equal(f.A.Context.Id,page.UniverseId);
        var other=await client.ReadAsync(f.B.Context.Id,new(),timeout.Token);Assert.Empty(other.Events);Assert.Equal(f.B.Context.Id,other.UniverseId);
        Assert.Empty(Directory.EnumerateFiles(requests));Assert.Empty(Directory.EnumerateFiles(f.Root,"*.zip",SearchOption.AllDirectories));
    }
    [Fact]
    public void OfflineJournalReadIsBoundedDoesNotInventDatesAndNeverRepairsDamagedFiles()
    {
        using var f=new PreparationTestFixture();using var owner=f.Queue.AcquireOwner();var store=new JobStateStore(f.Context);var job=JobId.Create();store.Create(job);
        var bad=Path.Combine(f.Roots.StateRoot,JobId.Create().Value+".json");File.WriteAllText(bad,"corrupt synthetic metadata");
        var before=Directory.GetFiles(f.Roots.StateRoot).ToDictionary(p=>p,p=>SHA256.HashData(File.ReadAllBytes(p)));
        var result=LocalDiagnosticReader.Read(f.Context,CancellationToken.None);Assert.Single(result.States);Assert.True(result.ReadError);Assert.False(result.More);
        foreach(var file in before)Assert.Equal(file.Value,SHA256.HashData(File.ReadAllBytes(file.Key)));
        f.AssertUntouched();
    }
    [Fact]
    public void RegisterSupportsIndependentLicensesAndRejectsDuplicateJsonFields()
    {
        var initial=LicenseRegister.Initial();var imageSharp=initial.Licenses.Single();
        var other=imageSharp with{Key="synthetic-second-license",Name="Licencia sintética",AssociatedFile="metadata-only",Expiry=new(2030,1,1)};
        var value=LicenseRegister.Evaluate(initial with{Licenses=[imageSharp,other]},new(2028,1,18));
        Assert.Equal(4,value.Licenses[0].Reminders.Count);Assert.Empty(value.Licenses[1].Reminders);
        var attended=LicenseRegister.MarkAttended(value,imageSharp.Key,new(2028,1,18));Assert.Null(LicenseRegister.Pending(attended.Licenses[0]));
        var root=Path.Combine(Path.GetTempPath(),"nap-license-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var path=Path.Combine(root,"metadata.json");var text="{\"Version\":1,\"Version\":1,\"Licenses\":[]}";File.WriteAllText(path,text);
            Assert.Throws<InvalidDataException>(()=>new LicenseRegister(path).Check(new(2028,1,18)));Assert.Equal(text,File.ReadAllText(path));
        }
        finally{Directory.Delete(root,true);}
    }
    private sealed class FakeDiagnostics(Func<UniverseId,DiagnosticQuery,CancellationToken,Task<DiagnosticPage>> read):IDiagnosticDesktopClient
    {
        public Func<UniverseId,DiagnosticQuery,CancellationToken,Task<DiagnosticPage>> Read=read;
        public Task<DiagnosticPage> ReadAsync(UniverseId u,DiagnosticQuery q,CancellationToken ct)=>Read(u,q,ct);
    }
}

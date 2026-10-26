using NAP.Core;
using NAP.Presentation;
namespace NAP.App;

/// <summary>Pure metadata fixture. No vendor file, settings, queue, network or assets are opened.</summary>
internal sealed class Phase6SettingsPreview(string mode) : IDiagnosticDesktopClient
{
    internal static readonly string[] Modes=["diagnostics","diagnostics-filtered","diagnostics-export","diagnostics-offline","diagnostics-error","licenses","licenses-90","licenses-30","licenses-7","licenses-expired","licenses-renewed"];
    internal static DateOnly Date(string mode) => mode switch
    {
        "licenses-90"=>new(2027,10,20),"licenses-30"=>new(2027,12,19),"licenses-7"=>new(2028,1,11),"licenses-expired"=>new(2028,1,20),"licenses-renewed"=>new(2028,1,21),_=>new(2026,10,22)
    };
    internal static LicenseViewModel Licenses(string mode)
    {
        var today=Date(mode);var registry=LicenseRegister.Initial();
        if(mode=="licenses-renewed")registry=registry with{Licenses=registry.Licenses.Select(l=>l with{Expiry=new(2029,1,18),Renewals=[new(today,l.Expiry,new(2029,1,18))]}).ToArray()};
        var store=new MemoryRegister(registry);var vm=new LicenseViewModel(store,()=>today);vm.Apply(store.Check(today));return vm;
    }
    private sealed class MemoryRegister(LicenseRegistry initial) : ILicenseRegister
    {
        private LicenseRegistry _value=initial;
        public LicenseRegistry Check(DateOnly today)=>_value=LicenseRegister.Evaluate(_value,today);
        public LicenseRegistry Attend(string key,DateOnly today)=>_value=LicenseRegister.MarkAttended(_value,key,today);
        public LicenseRegistry Renew(string key,DateOnly expiry,DateOnly today)=>_value=LicenseRegister.RecordRenewal(_value,key,expiry,today);
    }
    public Task<DiagnosticPage> ReadAsync(UniverseId universe,DiagnosticQuery query,CancellationToken cancellation)
    {
        if(mode=="diagnostics-error")throw new System.IO.IOException("Synthetic disconnected source.");
        var utc=new DateTimeOffset(2026,10,22,10,30,0,TimeSpan.Zero);
        DiagnosticEvent[] rows=[new(4,utc,QueueStage.Audit,DiagnosticSeverity.Error,QueueEventCode.StageRecorded,QueueEventResult.Rejected,QueueState.NeedsReview,QueueIncident.AuditFail),
            new(3,utc.AddMinutes(-1),QueueStage.Normalization,DiagnosticSeverity.Warning,QueueEventCode.Transitioned,QueueEventResult.ReviewRequired,QueueState.NeedsReview,QueueIncident.RatioUnsupported),
            new(2,utc.AddMinutes(-2),QueueStage.Execution,DiagnosticSeverity.Warning,QueueEventCode.Transitioned,QueueEventResult.Waiting,QueueState.RetryScheduled,QueueIncident.StorageUnavailable),
            new(1,utc.AddMinutes(-3),QueueStage.Catalog,DiagnosticSeverity.Information,QueueEventCode.StageRecorded,QueueEventResult.Succeeded,QueueState.Completed,QueueIncident.None)];
        return Task.FromResult(new DiagnosticPage(universe,rows.Where(r=>(query.From is null||r.Utc>=query.From)&&(query.Until is null||r.Utc<query.Until)&&(query.Severity is null||r.Severity==query.Severity)&&(query.Category is null||r.Category==query.Category)).ToArray(),false,4));
    }
}

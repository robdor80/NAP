using NAP.Core;
namespace NAP.Presentation;

public sealed record LicenseDisplay(AdministrativeLicense Value, DateOnly Today)
{
    public string Name => Value.Name;
    public string Expiry => Value.Expiry.ToString("dd/MM/yyyy");
    public string Remaining => Today > Value.Expiry ? $"Venció hace {Today.DayNumber - Value.Expiry.DayNumber} días" : Today == Value.Expiry ? "Vence hoy" : $"{Value.Expiry.DayNumber - Today.DayNumber} días restantes";
    public string Reminder => LicenseRegister.Pending(Value)?.Kind switch
    {
        LicenseReminderKind.NinetyDays => "Renovación pendiente · aviso de 90 días",
        LicenseReminderKind.ThirtyDays => "Renovación pendiente · aviso de 30 días",
        LicenseReminderKind.SevenDays => "Renovación pendiente · aviso de 7 días",
        LicenseReminderKind.Expired => "Vencimiento registrado · revisa la renovación", _ => "Sin avisos pendientes"
    };
    public IEnumerable<string> History => Value.Renewals.Reverse().Select(r => $"{r.Registered:dd/MM/yyyy} · Renovación registrada hasta {r.NewExpiry:dd/MM/yyyy}")
        .Concat(Value.Reminders.Reverse().Take(12).Select(r => $"{r.Detected:dd/MM/yyyy} · {r.Kind switch { LicenseReminderKind.NinetyDays => "90 días", LicenseReminderKind.ThirtyDays => "30 días", LicenseReminderKind.SevenDays => "7 días", _ => "Vencimiento" }} · {(r.Attended is null ? "Pendiente" : $"Atendido {r.Attended:dd/MM/yyyy}")}"));
}
public sealed class LicenseViewModel : ObservableObject
{
    private readonly LicenseRegister? _store;
    private readonly Func<DateOnly> _today;
    private LicenseRegistry _value = new(1, []);
    private DateTime? _renewal;
    private string _notice = "";
    private string? _selectedKey;
    public LicenseViewModel(LicenseRegister? store, Func<DateOnly>? today = null)
    {
        _store = store; _today = today ?? (() => DateOnly.FromDateTime(DateTime.Now));
        Check = new(CheckAsync, () => _store is not null, Failed);
        Attend = new(async () => Apply(await Task.Run(() => _store!.Attend(Selected!.Value.Key, _today()))), () => _store is not null && Selected is not null && LicenseRegister.Pending(Selected.Value) is not null, Failed);
        Renew = new(async () =>
        {
            Apply(await Task.Run(() => _store!.Renew(Selected!.Value.Key, DateOnly.FromDateTime(RenewalDate!.Value), _today())));
            Notice = "Renovación registrada"; RenewalDate = null;
        }, () => _store is not null && Selected is not null && RenewalDate is { } d && DateOnly.FromDateTime(d) > Selected.Value.Expiry && DateOnly.FromDateTime(d) > _today(), Failed);
    }
    public UiAsyncCommand Check { get; }
    public UiAsyncCommand Attend { get; }
    public UiAsyncCommand Renew { get; }
    public IReadOnlyList<LicenseDisplay> Licenses => _value.Licenses.Select(l => new LicenseDisplay(l, _today())).ToArray();
    public LicenseDisplay? Selected { get => Licenses.FirstOrDefault(l => l.Value.Key == _selectedKey) ?? Licenses.FirstOrDefault(); set { if(_selectedKey==value?.Value.Key)return; _selectedKey = value?.Value.Key; Notify(); Attend.Refresh(); Renew.Refresh(); } }
    public string Notice { get => _notice; private set => Set(ref _notice, value); }
    public DateTime? RenewalDate { get => _renewal; set { Set(ref _renewal, value); Renew.Refresh(); } }
    public string PendingNotice => _value.Licenses.Any(l => LicenseRegister.Pending(l) is not null) ? "Licencias · renovación pendiente" : "";
    public async Task CheckAsync()
    {
        if (_store is null) return;
        try { Apply(await Task.Run(() => _store.Check(_today()))); Notice = ""; }
        catch (Exception) { Failed(null!); }
    }
    public void Apply(LicenseRegistry registry)
    { _value = registry; Notify(nameof(Licenses)); Notify(nameof(Selected)); Notify(nameof(PendingNotice)); Attend.Refresh(); Renew.Refresh(); }
    private void Failed(Exception _) { Notice = "No se pudo leer o guardar el registro de licencias. Se conserva el archivo existente."; }
}

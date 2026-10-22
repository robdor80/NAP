using System.Text.Json;
using System.Text.Json.Serialization;

namespace NAP.Core;

public enum LicenseReminderKind { NinetyDays, ThirtyDays, SevenDays, Expired }
public sealed record LicenseReminder(DateOnly Expiry, LicenseReminderKind Kind, DateOnly Detected, DateOnly? Attended = null);
public sealed record LicenseRenewal(DateOnly Registered, DateOnly PreviousExpiry, DateOnly NewExpiry);
public sealed record AdministrativeLicense(string Key, string Name, string AssociatedFile, DateOnly Expiry,
    IReadOnlyList<LicenseReminder> Reminders, IReadOnlyList<LicenseRenewal> Renewals);
public sealed record LicenseRegistry(int Version, IReadOnlyList<AdministrativeLicense> Licenses);

/// <summary>Administrative metadata only. This component has no API for opening a vendor license.</summary>
public sealed class LicenseRegister
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NAP", "license-reminders.json");
    private readonly string _path;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public LicenseRegister(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Administrative JSON path required.");
        _path = path;
    }
    public static LicenseRegistry Initial() => new(1, [new("imagesharp-community", "Six Labors ImageSharp Community", "sixlabors.lic", new(2028, 1, 18), [], [])]);
    public static DateOnly Threshold(DateOnly expiry, LicenseReminderKind kind) => expiry.AddDays(kind switch { LicenseReminderKind.NinetyDays => -90, LicenseReminderKind.ThirtyDays => -30, LicenseReminderKind.SevenDays => -7, _ => 0 });
    public static LicenseRegistry Evaluate(LicenseRegistry registry, DateOnly today) => registry with
    {
        Licenses = registry.Licenses.Select(l => l with { Reminders = l.Reminders.Concat(Enum.GetValues<LicenseReminderKind>()
            .Where(k => today >= Threshold(l.Expiry, k) && !l.Reminders.Any(r => r.Expiry == l.Expiry && r.Kind == k))
            .Select(k => new LicenseReminder(l.Expiry, k, today))).TakeLast(200).ToArray() }).ToArray()
    };
    public static LicenseReminder? Pending(AdministrativeLicense license) => license.Reminders
        .Where(r => r.Expiry == license.Expiry && r.Attended is null).OrderByDescending(r => r.Kind).FirstOrDefault();
    public LicenseRegistry Check(DateOnly today) => Change(r => Evaluate(r, today));
    public LicenseRegistry Attend(string key, DateOnly today) => Change(r => Evaluate(r, today) with
    {
        Licenses = Evaluate(r, today).Licenses.Select(l => l.Key != key ? l : l with
        { Reminders = l.Reminders.Select(n => n.Expiry == l.Expiry && n.Attended is null ? n with { Attended = today } : n).ToArray() }).ToArray()
    });
    public LicenseRegistry Renew(string key, DateOnly expiry, DateOnly today) => Change(r =>
    {
        var old = r.Licenses.Single(l => l.Key == key);
        if (expiry <= today || expiry <= old.Expiry) throw new ArgumentException("La nueva fecha debe ser posterior a hoy y al vencimiento registrado.");
        return Evaluate(r with { Licenses = r.Licenses.Select(l => l.Key != key ? l : l with
        {
            Expiry = expiry, Reminders = l.Reminders.Select(n => n.Attended is null ? n with { Attended = today } : n).ToArray(),
            Renewals = l.Renewals.Append(new LicenseRenewal(today, l.Expiry, expiry)).TakeLast(50).ToArray()
        }).ToArray() }, today);
    });
    public LicenseRegistry Read()
    {
        Guard(); if (!File.Exists(_path)) return Initial();
        using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 256 * 1024) throw new InvalidDataException("Administrative register exceeds its bound.");
        var value = JsonSerializer.Deserialize<LicenseRegistry>(stream, Json) ?? throw new InvalidDataException("Missing register.");
        Validate(value); return value;
    }
    private LicenseRegistry Change(Func<LicenseRegistry, LicenseRegistry> update)
    {
        Guard(); Directory.CreateDirectory(Path.GetDirectoryName(_path)!); Guard();
        using var lease = ExecutionMutex.Acquire("LicenseReminders", Path.GetDirectoryName(_path)!, Path.GetFileName(_path));
        var before = Read(); var after = update(before); Validate(after);
        if (File.Exists(_path) && JsonSerializer.Serialize(before, Json) == JsonSerializer.Serialize(after, Json)) return after;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(after, Json);
        if (bytes.Length > 256 * 1024) throw new InvalidDataException("Administrative register exceeds its bound.");
        var temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(bytes); file.Flush(true); }
            Guard(); File.Move(temp, _path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        return after;
    }
    private void Guard()
    {
        ProductionPaths.CheckPath(_path, NapIssueCodes.CatalogInvalid);
        for (var p = Path.GetDirectoryName(_path); p is not null; p = Path.GetDirectoryName(p))
            if (Directory.Exists(Path.Combine(p, ".git")) || File.Exists(Path.Combine(p, ".git"))) throw new InvalidDataException("Administrative register must remain outside Git.");
        if (Directory.Exists(_path)) throw new InvalidDataException("Register is not a file.");
    }
    private static void Validate(LicenseRegistry value)
    {
        if (value.Version != 1 || value.Licenses is null || value.Licenses.Count is < 1 or > 32 || value.Licenses.Select(l => l.Key).Distinct().Count() != value.Licenses.Count)
            throw new InvalidDataException("Unsupported administrative register.");
        foreach (var l in value.Licenses)
            if (string.IsNullOrWhiteSpace(l.Key) || l.Key.Length > 80 || string.IsNullOrWhiteSpace(l.Name) || l.Name.Length > 160 ||
                l.AssociatedFile != Path.GetFileName(l.AssociatedFile) || l.AssociatedFile.Length > 80 || l.Expiry.Year < 2000 ||
                l.Reminders is null || l.Renewals is null || l.Reminders.Count > 200 || l.Renewals.Count > 50 ||
                l.Reminders.Any(r => !Enum.IsDefined(r.Kind) || r.Expiry.Year < 2000 || r.Detected < Threshold(r.Expiry, r.Kind) || r.Attended < r.Detected) ||
                l.Reminders.Select(r => (r.Expiry, r.Kind)).Distinct().Count() != l.Reminders.Count)
                throw new InvalidDataException("Invalid administrative license metadata.");
    }
}

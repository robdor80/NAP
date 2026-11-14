using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Runtime.Versioning;
using NAP.Core;

namespace NAP.AI;

/// <summary>Only the encrypted key is persisted. DPAPI binds it to the current Windows user.</summary>
[SupportedOSPlatform("windows")]
public sealed class PersonalGeminiSettings
{
    private sealed record Stored(int Version, string Model, string ProtectedKey, bool Consented);
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NAP", "gemini.json");
    private readonly string _path;
    public PersonalGeminiSettings(string? path = null) => _path = path ?? DefaultPath;
    public (string Key, string Model)? Load()
    {
        if (!File.Exists(_path)) return null;
        var info = new FileInfo(_path);
        if (info.Length > 32768 || info.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("Invalid Gemini settings file.");
        var value = JsonSerializer.Deserialize<Stored>(File.ReadAllBytes(_path)) ?? throw new InvalidDataException("Invalid Gemini settings.");
        if (value.Version != 1 || !value.Consented) throw new InvalidDataException("Gemini consent is missing.");
        var bytes = ProtectedData.Unprotect(Convert.FromBase64String(value.ProtectedKey), null, DataProtectionScope.CurrentUser);
        try { return (Encoding.UTF8.GetString(bytes), value.Model); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public void Save(string key, string model, bool consented)
    {
        if (!consented || string.IsNullOrWhiteSpace(key) || key.Length > 4096 || key.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(model) || model.Length > 160 || model.Any(char.IsControl))
            throw new InvalidDataException("Indica clave, modelo y autorización de uso de Gemini.");
        var bytes = Encoding.UTF8.GetBytes(key.Trim());
        byte[] encrypted;
        try { encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        if (File.Exists(_path) && File.GetAttributes(_path).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("Invalid Gemini settings path.");
        var temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(file, new Stored(1, model.Trim(), Convert.ToBase64String(encrypted), true)); file.Flush(true); }
            File.Move(temp, _path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

/// <summary>Reload only through the Host's paused/drained audit boundary. Manual and automatic paths share this instance.</summary>
public sealed class PersonalGeminiAuditor(HttpClient transport) : IIdentifiedAiAuditClient, IAiAuditAvailability, ICatalogDiversityAiClient
{
    private sealed record Configuration(GeminiOperationAuditClient Audit, GeminiCatalogDiversityClient Diversity, string Model);
    private volatile Configuration? _configuration;
    private volatile string _diagnostic = "missing_configuration";
    public string ProviderId => "gemini-interactions-v1";
    public string ModelId => Availability.Model ?? "unconfigured";
    public AiAuditAvailability Availability => _configuration is { } configuration
        ? new(true, configuration.Model) : new(false, null, _diagnostic);
    public void Reload()
    {
        Configuration? configuration = null;
        var diagnostic = "missing_configuration";
        try
        {
            var settings = OperatingSystem.IsWindows() ? new PersonalGeminiSettings().Load() : null;
            var key = settings?.Key ?? Environment.GetEnvironmentVariable("NAP_GEMINI_API_KEY");
            var model = settings?.Model ?? Environment.GetEnvironmentVariable("NAP_GEMINI_MODEL");
            if (HostGeminiAuditor.Create(transport, key, model) is { } audit)
                configuration = new(audit, new(transport, key!, model!), model!);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException or JsonException or FormatException or InvalidDataException)
        { diagnostic = "invalid_configuration"; }
        _diagnostic = diagnostic;
        _configuration = configuration;
    }
    public Task<AiAuditReport> AuditAsync(AiAuditRequest request, CancellationToken cancellationToken = default) =>
        (_configuration?.Audit ?? throw new AiAuditClientException(AiAuditErrorKind.Configuration, "Configura y autoriza Gemini en Ajustes.")).AuditAsync(request, cancellationToken);

    public Task<CatalogDiversityAnswer> AskAsync(CatalogDiversityReport report, string question, CancellationToken cancellation = default) =>
        (_configuration?.Diversity ?? throw new AiAuditClientException(AiAuditErrorKind.Configuration, "Configura y autoriza Gemini en Ajustes."))
            .AskAsync(report, question, cancellation);
}

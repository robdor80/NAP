using NAP.Core;

namespace NAP.AI;

/// <summary>Explicit, credential-free-by-default Host audit composition.
/// Selection does not grant policy authority and never makes a synthetic PASS.</summary>
public static class HostGeminiAuditor
{
    public static GeminiOperationAuditClient? Create(HttpClient transport, string? apiKey, string? model, bool syntheticAudit = false)
    {
        ArgumentNullException.ThrowIfNull(transport);
        var keyPresent = !string.IsNullOrWhiteSpace(apiKey);
        var modelPresent = !string.IsNullOrWhiteSpace(model);
        if (syntheticAudit && (keyPresent || modelPresent))
            throw new InvalidDataException("Synthetic test audit and configured Gemini cannot be combined.");
        if (keyPresent != modelPresent)
            throw new InvalidDataException("Gemini audit requires both a private API key and a model.");
        return keyPresent ? new GeminiOperationAuditClient(transport, apiKey!, model!) : null;
    }
}

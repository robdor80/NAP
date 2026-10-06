using System.Collections.ObjectModel;

namespace NAP.Core;

/// <summary>Allowlisted plan facts only. Constructed through AiAuditRequestBuilder.</summary>
public sealed class AiAuditRequest
{
    internal AiAuditRequest(UniverseAssetKey assetKey, string assetType, string productionProfile,
        IReadOnlyDictionary<string, string> classification, IEnumerable<string> inputRoles,
        string destinationRelativeDirectory, IEnumerable<AiAuditIssueFact> validationIssues)
    {
        ArgumentNullException.ThrowIfNull(assetKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetType);
        ArgumentException.ThrowIfNullOrWhiteSpace(productionProfile);
        ArgumentNullException.ThrowIfNull(classification);
        ArgumentNullException.ThrowIfNull(inputRoles);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationRelativeDirectory);
        ArgumentNullException.ThrowIfNull(validationIssues);
        // Portable relative-directory validation, never Path.GetFullPath or filesystem I/O.
        if (destinationRelativeDirectory.Contains('\\') || destinationRelativeDirectory.Contains(':') ||
            destinationRelativeDirectory.Any(char.IsControl) ||
            destinationRelativeDirectory.Split('/').Any(segment => segment.Length == 0 || segment is "." or ".."))
            throw new ArgumentException("The audit destination must be a simple relative directory with slash-separated segments.", nameof(destinationRelativeDirectory));
        var roles = inputRoles.ToArray();
        if (roles.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Input roles cannot contain null or blank elements.", nameof(inputRoles));
        var issues = validationIssues.ToArray();
        if (issues.Any(issue => issue is null))
            throw new ArgumentException("Validation issues cannot contain null elements.", nameof(validationIssues));
        AssetKey = assetKey;
        AssetType = assetType;
        ProductionProfile = productionProfile;
        Classification = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(classification, StringComparer.Ordinal));
        InputRoles = Array.AsReadOnly(roles.OrderBy(role => role, StringComparer.Ordinal).ToArray());
        DestinationRelativeDirectory = destinationRelativeDirectory;
        ValidationIssues = Array.AsReadOnly(issues);
    }

    public UniverseAssetKey AssetKey { get; }
    public string AssetType { get; }
    public string ProductionProfile { get; }
    public IReadOnlyDictionary<string, string> Classification { get; }
    public IReadOnlyList<string> InputRoles { get; }
    public string DestinationRelativeDirectory { get; }
    public IReadOnlyList<AiAuditIssueFact> ValidationIssues { get; }
}

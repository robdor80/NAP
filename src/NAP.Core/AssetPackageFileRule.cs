namespace NAP.Core;

/// <summary>One profile-defined flat package filename. Does not inspect files or validate their content.</summary>
public sealed record AssetPackageFileRule
{
    public AssetPackageFileRule(string role, string suffix, string extension, bool required, string? contentValidator = null)
    {
        UniverseProfileIdentifiers.Require(role, nameof(role));
        if (suffix is null || (suffix.Length != 0 &&
            (suffix[0] != '_' || !AssetNamingRules.IsValidMachineIdentifier(suffix[1..]))))
            throw new ArgumentException("Expected an empty suffix or underscore plus a Naming v1 machine identifier.", nameof(suffix));
        if (!IsValidExtension(extension))
            throw new ArgumentException("Expected dot-separated lowercase ASCII alphanumeric extension segments.", nameof(extension));
        if (contentValidator is not null)
            UniverseProfileIdentifiers.Require(contentValidator, nameof(contentValidator));

        Role = role;
        Suffix = suffix;
        Extension = extension;
        Required = required;
        ContentValidator = contentValidator;
    }

    public string Role { get; }
    public string Suffix { get; }
    public string Extension { get; }
    public bool Required { get; }
    public string? ContentValidator { get; }

    public string ResolveFileName(string assetId)
    {
        if (!AssetNamingRules.IsValidAssetId(assetId))
            throw new ArgumentException("The asset ID must follow Naming v1.", nameof(assetId));
        return assetId + Suffix + Extension;
    }

    private static bool IsValidExtension(string? extension)
    {
        if (extension is not { Length: >= 2 } || extension[0] != '.' || extension[^1] == '.')
            return false;
        for (var index = 1; index < extension.Length; index++)
        {
            var character = extension[index];
            if (character is >= 'a' and <= 'z' or >= '0' and <= '9')
                continue;
            if (character != '.' || extension[index - 1] == '.')
                return false;
        }
        return true;
    }
}

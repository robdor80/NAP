namespace NAP.Core;

/// <summary>Checks Naming v1 form without normalization, vocabulary lookup or classification inference.</summary>
public static class AssetNamingRules
{
    public const int MaxMachineIdentifierLength = 64;
    public const int MaxAssetIdLength = 96;

    public static bool IsValidMachineIdentifier(string? value) =>
        value is { Length: <= MaxMachineIdentifierLength } && IsAsciiSnakeCase(value);

    public static bool IsValidAssetId(string? assetId)
    {
        if (assetId is not { Length: <= MaxAssetIdLength } || !IsAsciiSnakeCase(assetId))
        {
            return false;
        }

        var sequenceSeparator = assetId.LastIndexOf('_');
        if (sequenceSeparator < 0 || assetId.Length - sequenceSeparator - 1 != 3)
        {
            return false;
        }

        var sequence = assetId.AsSpan(sequenceSeparator + 1);
        return sequence[0] is >= '0' and <= '9' &&
               sequence[1] is >= '0' and <= '9' &&
               sequence[2] is >= '0' and <= '9' &&
               !sequence.SequenceEqual("000") &&
               assetId.AsSpan(0, sequenceSeparator).Contains('_');
    }

    /// <summary>Checks the exact type prefix and at least one descriptor after the entire type.</summary>
    public static bool MatchesAssetType(string? assetId, string? assetType) =>
        IsValidAssetId(assetId) && IsValidMachineIdentifier(assetType) &&
        assetId!.StartsWith(assetType + "_", StringComparison.Ordinal) &&
        assetId.LastIndexOf('_') > assetType!.Length + 1;

    private static bool IsAsciiSnakeCase(string value)
    {
        if (value.Length == 0 || value[0] is not (>= 'a' and <= 'z') || value[^1] == '_')
        {
            return false;
        }

        for (var index = 1; index < value.Length; index++)
        {
            var character = value[index];
            if (character is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                continue;
            }
            if (character != '_' || value[index - 1] == '_')
            {
                return false;
            }
        }
        return true;
    }
}

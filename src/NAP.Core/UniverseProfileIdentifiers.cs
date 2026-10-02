namespace NAP.Core;

internal static class UniverseProfileIdentifiers
{
    internal static void Require(string value, string parameterName)
    {
        if (!AssetNamingRules.IsValidMachineIdentifier(value))
            throw new ArgumentException("Expected a Naming v1 machine identifier.", parameterName);
    }

    internal static IReadOnlyList<string> Snapshot(IEnumerable<string> values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        var snapshot = values.ToArray();
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in snapshot)
        {
            Require(value, parameterName);
            if (!unique.Add(value))
                throw new ArgumentException("Duplicate classification dimensions are not allowed.", parameterName);
        }
        return Array.AsReadOnly(snapshot);
    }
}

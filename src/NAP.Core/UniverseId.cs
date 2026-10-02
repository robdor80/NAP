namespace NAP.Core;

/// <summary>A canonical universe identity using the unchanged Naming v1 machine identifier form.</summary>
public sealed record UniverseId
{
    public UniverseId(string value)
    {
        if (!AssetNamingRules.IsValidMachineIdentifier(value))
        {
            throw new ArgumentException("The universe ID must be a Naming v1 machine identifier.", nameof(value));
        }
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

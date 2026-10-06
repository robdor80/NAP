namespace NAP.Core;

/// <summary>An opaque, global process identity, independent of assets, universes and job state.</summary>
public sealed record JobId
{
    public JobId(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length != 36 || !value.StartsWith("job_", StringComparison.Ordinal) ||
            value.Skip(4).Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')) ||
            value == "job_00000000000000000000000000000000")
            throw new ArgumentException("A Job ID must use the canonical form 'job_' followed by 32 lowercase ASCII hexadecimal characters and must not be all zero.", nameof(value));
        Value = value;
    }

    public string Value { get; }
    public override string ToString() => Value;
    public static JobId Create() => new("job_" + Guid.NewGuid().ToString("N"));
}

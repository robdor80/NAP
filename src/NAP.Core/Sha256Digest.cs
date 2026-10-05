namespace NAP.Core;

/// <summary>A canonical SHA-256 value: exactly 64 lowercase ASCII hexadecimal characters.</summary>
public sealed record Sha256Digest
{
    public Sha256Digest(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);
        if (hex.Length != 64 || hex.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("A SHA-256 digest must contain exactly 64 lowercase ASCII hexadecimal characters.", nameof(hex));
        Hex = hex;
    }

    public string Hex { get; }
    public override string ToString() => Hex;
}

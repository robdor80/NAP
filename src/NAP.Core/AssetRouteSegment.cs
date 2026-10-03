namespace NAP.Core;

/// <summary>A structured routing segment. Performs no substitution, path calculation or I/O.</summary>
public sealed record AssetRouteSegment
{
    private AssetRouteSegment(AssetRouteSegmentKind kind, string? value)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (kind == AssetRouteSegmentKind.AssetId)
        {
            if (value is not null)
                throw new ArgumentException("An asset ID segment must not carry a value.", nameof(value));
        }
        else UniverseProfileIdentifiers.Require(value!, nameof(value));
        Kind = kind;
        Value = value;
    }

    public AssetRouteSegmentKind Kind { get; }
    public string? Value { get; }

    public static AssetRouteSegment Literal(string value) => new(AssetRouteSegmentKind.Literal, value);
    public static AssetRouteSegment Classification(string dimension) => new(AssetRouteSegmentKind.Classification, dimension);
    public static AssetRouteSegment AssetId() => new(AssetRouteSegmentKind.AssetId, null);
}

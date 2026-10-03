namespace NAP.Core;

/// <summary>Ordered declarative routing configuration, without resolving a destination.</summary>
public sealed class AssetRoutingRule
{
    public AssetRoutingRule(IEnumerable<AssetRouteSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        var snapshot = segments.ToArray();
        if (snapshot.Length == 0 || snapshot.Any(segment => segment is null))
            throw new ArgumentException("Routing requires at least one segment and no null segments.", nameof(segments));
        Segments = Array.AsReadOnly(snapshot);
    }

    public IReadOnlyList<AssetRouteSegment> Segments { get; }
}

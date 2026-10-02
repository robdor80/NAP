namespace NAP.Core;

/// <summary>Minimal profile identity; configurable universe rules are introduced later.</summary>
public sealed record UniverseProfile
{
    public UniverseProfile(UniverseId id, string displayName)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        Id = id;
        DisplayName = displayName;
    }

    public UniverseId Id { get; }
    public string DisplayName { get; }
}

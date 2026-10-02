namespace NAP.Core;

/// <summary>Explicit active universe scope; profile and storage identities must agree.</summary>
public sealed record UniverseContext
{
    public UniverseContext(UniverseProfile profile, UniverseStorageConfig storage)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(storage);
        if (profile.Id != storage.UniverseId)
        {
            throw new ArgumentException("Profile and storage must belong to the same universe.", nameof(storage));
        }
        Profile = profile;
        Storage = storage;
    }

    public UniverseProfile Profile { get; }
    public UniverseStorageConfig Storage { get; }
    public UniverseId Id => Profile.Id;
}

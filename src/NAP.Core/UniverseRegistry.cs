using System.Diagnostics.CodeAnalysis;

namespace NAP.Core;

/// <summary>An ordered, read-only snapshot of available profiles, indexed by universe identity.</summary>
public sealed class UniverseRegistry
{
    private readonly Dictionary<UniverseId, UniverseProfile> _profilesById = new();

    public UniverseRegistry(IEnumerable<UniverseProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        var snapshot = profiles.ToArray();
        foreach (var profile in snapshot)
        {
            if (profile is null)
            {
                throw new ArgumentException("A registry cannot contain null profiles.", nameof(profiles));
            }
            if (!_profilesById.TryAdd(profile.Id, profile))
            {
                throw new ArgumentException("A registry cannot contain duplicate universe IDs.", nameof(profiles));
            }
        }
        Profiles = Array.AsReadOnly(snapshot);
    }

    public IReadOnlyList<UniverseProfile> Profiles { get; }

    public bool TryGet(UniverseId id, [NotNullWhen(true)] out UniverseProfile? profile)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _profilesById.TryGetValue(id, out profile);
    }
}

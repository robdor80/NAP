namespace NAP.Core;

public sealed record AutomationUniverseDefinition(UniverseStorageConfig Storage, string ProfileJson);
public sealed record AutomationHostLaunchConfiguration(int SchemaVersion, string LaboratoryRoot, string ControlRoot,
    AutomationConnection Connection, IReadOnlyList<AutomationUniverseDefinition> Universes, bool SyntheticAudit = false)
{
    public IReadOnlyList<UniverseContext> Contexts()
    {
        if (SchemaVersion != 1 || Universes is null || Universes.Count is < 1 or > 64) throw new InvalidDataException("Unsupported host launch configuration.");
        return Universes.Select(u =>
        {
            if (u.ProfileJson.Length > 1024 * 1024) throw new InvalidDataException("Profile exceeds its limit.");
            using var input = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(u.ProfileJson));
            return new UniverseContext(UniverseProfileLoader.Load(input), u.Storage);
        }).ToArray();
    }
}

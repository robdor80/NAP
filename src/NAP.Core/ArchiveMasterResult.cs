namespace NAP.Core;

/// <summary>A successful verified archive outcome; warnings remain available to the caller.</summary>
public sealed class ArchiveMasterResult
{
    internal ArchiveMasterResult(ArchiveMasterPlan plan, ArchiveMasterOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Issues.ShouldStop || !Enum.IsDefined(outcome)) throw new ArgumentException("Expected a successful archive outcome.");
        AssetKey = plan.AssetKey;
        Outcome = outcome;
        MasterDigest = plan.MasterDigest;
        RelativeDirectory = plan.RelativeDirectory;
        FilesVerified = Array.AsReadOnly(plan.Files.ToArray());
        Issues = plan.Issues;
    }

    public UniverseAssetKey AssetKey { get; }
    public ArchiveMasterOutcome Outcome { get; }
    public Sha256Digest MasterDigest { get; }
    public string RelativeDirectory { get; }
    public IReadOnlyList<ArchiveMasterFile> FilesVerified { get; }
    public NapIssueReport Issues { get; }
}

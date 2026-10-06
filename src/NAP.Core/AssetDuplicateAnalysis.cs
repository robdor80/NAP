namespace NAP.Core;

/// <summary>A coherent pairwise content relation within one universe; no job or execution state.</summary>
public sealed class AssetDuplicateAnalysis
{
    internal AssetDuplicateAnalysis(AssetContentRelation relation, AssetContentFingerprint candidate,
        AssetContentFingerprint existing, NapIssueReport issues)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(issues);
        if (!Enum.IsDefined(relation))
            throw new ArgumentOutOfRangeException(nameof(relation));
        if (candidate.AssetKey.UniverseId != existing.AssetKey.UniverseId)
            throw new ArgumentException("The candidate and existing fingerprints must belong to the same universe.", nameof(existing));

        var sameKey = candidate.AssetKey == existing.AssetKey;
        var sameDigest = candidate.Digest == existing.Digest;
        var coherent = relation switch
        {
            AssetContentRelation.Distinct => !sameKey && !sameDigest,
            AssetContentRelation.SameAssetSameContent => sameKey && sameDigest,
            AssetContentRelation.SameAssetDifferentContent => sameKey && !sameDigest,
            AssetContentRelation.DifferentAssetSameContent => !sameKey && sameDigest,
            _ => false
        };
        if (!coherent)
            throw new ArgumentException("The relation must match the fingerprints.", nameof(relation));

        var coherentReport = relation switch
        {
            AssetContentRelation.Distinct or AssetContentRelation.SameAssetSameContent => issues.IsClean,
            AssetContentRelation.SameAssetDifferentContent => HasSingleIssue(
                NapIssueCodes.AssetContentCollision, NapIssueSeverity.Error, NapIssueDisposition.Stop),
            AssetContentRelation.DifferentAssetSameContent => HasSingleIssue(
                NapIssueCodes.AssetContentPossibleDuplicate, NapIssueSeverity.Warning, NapIssueDisposition.Continue),
            _ => false
        };
        if (!coherentReport)
            throw new ArgumentException("The issue report must match the relation.", nameof(issues));

        Relation = relation;
        Candidate = candidate;
        Existing = existing;
        Issues = issues;

        bool HasSingleIssue(string code, NapIssueSeverity severity, NapIssueDisposition disposition) =>
            issues.Issues.Count == 1 && issues.Issues[0].Code == code &&
            issues.Issues[0].Severity == severity && issues.Issues[0].Disposition == disposition;
    }

    public AssetContentRelation Relation { get; }
    public AssetContentFingerprint Candidate { get; }
    public AssetContentFingerprint Existing { get; }
    public NapIssueReport Issues { get; }
    /// <summary>Same identity and bytes against this known fingerprint, not proof of a completed job.</summary>
    public bool IsIdempotent => Relation == AssetContentRelation.SameAssetSameContent;
    public bool IsCollision => Relation == AssetContentRelation.SameAssetDifferentContent;
    public bool IsPossibleDuplicate => Relation == AssetContentRelation.DifferentAssetSameContent;
}

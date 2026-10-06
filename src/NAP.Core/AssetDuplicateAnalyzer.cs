namespace NAP.Core;

/// <summary>Pure, stateless comparison of already known fingerprints within one universe.</summary>
public sealed class AssetDuplicateAnalyzer
{
    public AssetDuplicateAnalysis Analyze(AssetContentFingerprint candidate, AssetContentFingerprint existing)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(existing);
        if (candidate.AssetKey.UniverseId != existing.AssetKey.UniverseId)
            throw new ArgumentException("The candidate and existing fingerprints must belong to the same universe.", nameof(existing));

        var sameKey = candidate.AssetKey == existing.AssetKey;
        var sameDigest = candidate.Digest == existing.Digest;
        var universe = candidate.AssetKey.UniverseId.Value;
        AssetContentRelation relation;
        NapIssue[] issues;
        if (sameKey && sameDigest)
        {
            relation = AssetContentRelation.SameAssetSameContent;
            issues = [];
        }
        else if (sameKey)
        {
            relation = AssetContentRelation.SameAssetDifferentContent;
            issues = [new NapIssue(NapIssueCodes.AssetContentCollision, NapIssueSeverity.Error,
                NapIssueDisposition.Stop, "The asset key is already associated with different content.",
                detail: $"universe={universe}; asset_id={candidate.AssetKey.AssetId}; existing_sha256={existing.Digest.Hex}; candidate_sha256={candidate.Digest.Hex}")];
        }
        else if (sameDigest)
        {
            relation = AssetContentRelation.DifferentAssetSameContent;
            issues = [new NapIssue(NapIssueCodes.AssetContentPossibleDuplicate, NapIssueSeverity.Warning,
                NapIssueDisposition.Continue, "The same content is already associated with a different asset key.",
                detail: $"universe={universe}; candidate_asset_id={candidate.AssetKey.AssetId}; existing_asset_id={existing.AssetKey.AssetId}; sha256={candidate.Digest.Hex}")];
        }
        else
        {
            relation = AssetContentRelation.Distinct;
            issues = [];
        }
        return new AssetDuplicateAnalysis(relation, candidate, existing, new NapIssueReport(issues));
    }
}

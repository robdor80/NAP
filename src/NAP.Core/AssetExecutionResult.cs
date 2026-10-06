namespace NAP.Core;

/// <summary>Verified MVP completion; no catalog or repository automation side effects.</summary>
public sealed class AssetExecutionResult
{
    internal AssetExecutionResult(JobId jobId, ArchiveMasterResult archive, ProductionAssetResult production)
    {
        JobId = jobId; FinalState = JobState.Completed; Archive = archive; Production = production;
    }
    public JobId JobId { get; }
    public JobState FinalState { get; }
    public ArchiveMasterResult Archive { get; }
    public ProductionAssetResult Production { get; }
}

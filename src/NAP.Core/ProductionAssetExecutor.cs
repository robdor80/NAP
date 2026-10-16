namespace NAP.Core;

/// <summary>PASS-gated production writes and separate read-only physical verification, under a root process mutex.</summary>
public sealed class ProductionAssetExecutor
{
    private readonly UniverseContext _context;
    public ProductionAssetExecutor(UniverseContext context) { ArgumentNullException.ThrowIfNull(context); _context = context; }

    public ProductionAssetResult Execute(ProductionAssetPlan plan, AiAuditReport auditReport)
    {
        ArgumentNullException.ThrowIfNull(plan); RequirePass(auditReport);
        using var jobLease = plan.JobId is null ? null : ExecutionMutex.Acquire("Job", _context.Storage.StateRoot, plan.JobId.Value);
        return ExecuteCoordinated(plan, auditReport, jobLease);
    }

    internal ProductionAssetResult ExecuteCoordinated(ProductionAssetPlan plan, AiAuditReport auditReport, ExecutionMutex? jobLease)
        => ExecuteControlled(plan, auditReport, jobLease, null);

    internal ProductionAssetResult ExecuteControlled(ProductionAssetPlan plan, AiAuditReport auditReport, ExecutionMutex? jobLease,
        WorkflowExecutionControl? control)
    {
        ArgumentNullException.ThrowIfNull(plan); RequirePass(auditReport);
        if (plan.JobId is not null) jobLease!.Require("Job", _context.Storage.StateRoot, plan.JobId.Value);
        if (plan.Issues.ShouldStop) throw new ProductionStorageException(plan.Issues);
        RequireContext(plan);
        using var coordination = ExecutionMutex.Acquire("Production", _context.Storage.ProductionRoot);
        var current = new ProductionAssetPlanner(_context).Revalidate(plan);
        if (current.Issues.ShouldStop) throw new ProductionStorageException(current.Issues);
        if (current.JobId is not null && ProductionExecutionEvidence.State(_context, current.JobId) == JobState.Completed)
        {
            if (current.FilesToWrite.Count != 0) throw ProductionStorageException.Stop(NapIssueCodes.ProductionCompletedInconsistent, "COMPLETED outputs cannot be repaired.");
            VerifyAll(current); return new ProductionAssetResult(current, ProductionAssetOutcome.AlreadyProduced);
        }
        control?.Before("production-intent");
        var evidence = ProductionExecutionEvidence.Begin(_context, current, jobLease);
        control?.After("production-intent");
        if (current.FilesToWrite.Count > 0) ProductionPaths.Destination(_context, current.RelativeDirectory, create: true);
        foreach (var file in current.FilesToWrite)
        {
            control?.Before("production-file:" + file.FileName);
            WriteFileControlled(current, file, control);
            evidence?.Record(file, jobLease!);
            control?.After("production-file:" + file.FileName);
        }
        VerifyAll(current);
        return new ProductionAssetResult(current, current.FilesToWrite.Count == 0 ? ProductionAssetOutcome.AlreadyProduced : ProductionAssetOutcome.Produced);
    }

    public ProductionAssetResult Verify(ProductionAssetPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        using var jobLease = plan.JobId is null ? null : ExecutionMutex.Acquire("Job", _context.Storage.StateRoot, plan.JobId.Value);
        return VerifyCoordinated(plan, jobLease);
    }

    internal ProductionAssetResult VerifyCoordinated(ProductionAssetPlan plan, ExecutionMutex? jobLease)
    {
        if (plan.JobId is not null) jobLease!.Require("Job", _context.Storage.StateRoot, plan.JobId.Value);
        RequireContext(plan);
        using var coordination = ExecutionMutex.Acquire("Production", _context.Storage.ProductionRoot);
        var current = new ProductionAssetPlanner(_context).Revalidate(plan);
        if (current.Issues.ShouldStop) throw new ProductionStorageException(current.Issues);
        VerifyAll(current);
        return new ProductionAssetResult(current, ProductionAssetOutcome.AlreadyProduced);
    }

    internal static void RequirePass(AiAuditReport auditReport)
    {
        ArgumentNullException.ThrowIfNull(auditReport);
        if (auditReport.Decision != AiAuditDecision.Pass || !auditReport.Passed)
            throw new InvalidOperationException("Production execution requires a validated AI audit PASS.");
    }

    private void RequireContext(ProductionAssetPlan plan)
    {
        if (plan.AssetKey.UniverseId != _context.Id || !ProductionPaths.Same(plan.ProductionRoot, _context.Storage.ProductionRoot))
            throw new ArgumentException("The production plan belongs to another context.", nameof(plan));
    }

    private void WriteFile(ProductionAssetPlan plan, ProductionAssetFile file) => WriteFileControlled(plan, file, null);

    private void WriteFileControlled(ProductionAssetPlan plan, ProductionAssetFile file, WorkflowExecutionControl? control)
    {
        ProductionPaths.Destination(_context, plan.RelativeDirectory);
        new ProductionAssetPlanner(_context).VerifySources(plan);
        ProductionAssetPlanner.CheckEntries(plan);
        if (ProductionPaths.FileExists(file.DestinationPath, NapIssueCodes.ProductionFileCollision)) throw Collision(file);
        var temp = file.DestinationPath + $".{Guid.NewGuid():N}.tmp";
        using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            if (file.Kind == ProductionAssetFileKind.GeneratedWebp) file.WriteGenerated(output);
            else
            {
                using var input = new FileStream(file.SourcePath!, FileMode.Open, FileAccess.Read, FileShare.Read);
                input.CopyTo(output);
            }
            output.Flush(flushToDisk: true);
        }
        PublishFileControlled(plan, file, temp, control);
    }

    private void PublishFile(ProductionAssetPlan plan, ProductionAssetFile file, string temp) => PublishFileControlled(plan, file, temp, null);

    private void PublishFileControlled(ProductionAssetPlan plan, ProductionAssetFile file, string temp, WorkflowExecutionControl? control)
    {
        ProductionPaths.Destination(_context, plan.RelativeDirectory);
        if (!ProductionPaths.Same(Path.GetDirectoryName(temp)!, plan.DestinationDirectory) || !ProductionAssetPlanner.IsOwnTemp(plan, Path.GetFileName(temp)))
            throw new ArgumentException("The production temp must be a recognized sibling of the final.", nameof(temp));
        ProductionAssetPlanner.VerifyOutput(plan, file, temp, NapIssueCodes.ProductionVerificationFailed);
        new ProductionAssetPlanner(_context).VerifySources(plan);
        ProductionAssetPlanner.CheckEntries(plan);
        ProductionPaths.CheckCasing(plan.DestinationDirectory, file.FileName);
        if (ProductionPaths.FileExists(file.DestinationPath, NapIssueCodes.ProductionFileCollision)) throw Collision(file);
        control?.Before("production-publish:" + file.FileName);
        try { File.Move(temp, file.DestinationPath, overwrite: false); }
        catch (IOException ex) when (ProductionPaths.Attributes(file.DestinationPath) is not null)
        { throw ProductionStorageException.Stop(NapIssueCodes.ProductionFileCollision, "The final cannot be overwritten.", file.DestinationPath, ex); }
        ProductionAssetPlanner.VerifyOutput(plan, file, file.DestinationPath, NapIssueCodes.ProductionVerificationFailed);
        control?.After("production-publish:" + file.FileName);
    }

    private void VerifyAll(ProductionAssetPlan plan)
    {
        ProductionStorageRootValidator.Require(_context);
        ProductionPaths.Destination(_context, plan.RelativeDirectory);
        new ProductionAssetPlanner(_context).VerifySources(plan);
        ProductionAssetPlanner.CheckEntries(plan);
        foreach (var file in plan.Files) ProductionAssetPlanner.VerifyOutput(plan, file, file.DestinationPath, NapIssueCodes.ProductionVerificationFailed);
        var evidence = ProductionExecutionEvidence.Load(_context, plan);
        if (plan.JobId is not null && (evidence is null || !evidence.IsComplete))
            throw ProductionStorageException.Stop(NapIssueCodes.ProductionRecoveryInvalid, "Successful Job production requires durable receipts for every final publication.");
        ProductionAssetPlanner.CheckEntries(plan);
    }

    private static ProductionStorageException Collision(ProductionAssetFile file) => ProductionStorageException.Stop(NapIssueCodes.ProductionFileCollision, "A final file appeared after preflight; automatic collision resolution is forbidden.", file.DestinationPath);
}

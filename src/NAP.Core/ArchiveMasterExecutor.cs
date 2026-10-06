namespace NAP.Core;

/// <summary>Preserves originals only after AI PASS and locked archive preflight. Never writes production or job state.</summary>
public sealed class ArchiveMasterExecutor
{
    private readonly UniverseContext _context;

    public ArchiveMasterExecutor(UniverseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public ArchiveMasterResult Execute(ArchiveMasterPlan plan, AiAuditReport auditReport)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(auditReport);
        // This gate precedes every filesystem operation, including lock/directory creation.
        if (auditReport.Decision != AiAuditDecision.Pass || !auditReport.Passed)
            throw new InvalidOperationException("Archive execution requires a validated AI audit PASS.");
        if (plan.Issues.ShouldStop) throw new ArchiveStorageException(plan.Issues);
        if (plan.AssetKey.UniverseId != _context.Id || !ArchivePaths.Same(plan.ArchiveRoot, _context.Storage.ArchiveRoot))
            throw new ArgumentException("The archive plan belongs to a different universe or root.", nameof(plan));

        using var archiveLock = ArchiveLock.Acquire(_context);
        var store = new ArchiveMasterIndexStore(_context);
        var index = store.Load();
        var current = new ArchiveMasterPlanner(_context).Revalidate(plan, index);
        if (current.Issues.ShouldStop) throw new ArchiveStorageException(current.Issues);
        if (current.Action == ArchiveMasterAction.AlreadyArchived)
            return new ArchiveMasterResult(current, ArchiveMasterOutcome.AlreadyArchived);

        archiveLock.EnableWrites();
        if (current.FilesToCopy.Count > 0) ArchivePaths.EnsureDirectory(_context, current.DestinationDirectory);
        foreach (var file in current.FilesToCopy) CopyFile(file);
        foreach (var file in current.Files)
        {
            ArchiveRootValidator.Require(_context);
            ArchiveMasterPlanner.VerifyFile(file.SourcePath, file, NapIssueCodes.ArchiveSourceChanged);
            ArchiveMasterPlanner.VerifyFile(file.DestinationPath, file, NapIssueCodes.ArchiveVerificationFailed);
        }

        // Detect a non-cooperating index change as well; never replace a changed index snapshot.
        var latest = store.Load();
        if (!SameIndex(index, latest))
            throw ArchiveStorageException.Stop(NapIssueCodes.ArchiveIndexInconsistent, "The index changed while the archive lock was held.", store.IndexPath);
        var entry = new ArchiveMasterIndexEntry(current.AssetKey.AssetId, current.AssetType, current.MasterDigest,
            current.MasterSizeBytes, current.RelativeDirectory, true);
        store.Publish(new ArchiveMasterIndex(_context.Id, index.Entries.Append(entry)), archiveLock);
        return new ArchiveMasterResult(current, current.Action == ArchiveMasterAction.CopyAndIndex ? ArchiveMasterOutcome.Copied : ArchiveMasterOutcome.IndexedExisting);
    }

    private void CopyFile(ArchiveMasterFile file)
    {
        ArchiveRootValidator.Require(_context);
        ArchiveMasterPlanner.VerifyFile(file.SourcePath, file, NapIssueCodes.ArchiveSourceChanged);
        if (ArchivePaths.FileExists(file.DestinationPath))
            throw ArchiveStorageException.Stop(NapIssueCodes.ArchiveFileCollision, "A final file appeared after archive preflight.", file.DestinationPath);
        var temp = file.DestinationPath + $".{Guid.NewGuid():N}.tmp";
        using (var source = new FileStream(file.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            source.CopyTo(output);
            output.Flush(flushToDisk: true);
        }
        PublishFile(temp, file);
    }

    private void PublishFile(string temp, ArchiveMasterFile file)
    {
        ArchiveRootValidator.Require(_context);
        if (!ArchivePaths.Same(Path.GetDirectoryName(temp)!, Path.GetDirectoryName(file.DestinationPath)!))
            throw new ArgumentException("The archive temp must be a sibling of the final file.", nameof(temp));
        ArchiveMasterPlanner.VerifyFile(temp, file, NapIssueCodes.ArchiveVerificationFailed);
        ArchiveMasterPlanner.VerifyFile(file.SourcePath, file, NapIssueCodes.ArchiveSourceChanged);
        if (ArchivePaths.FileExists(file.DestinationPath))
            throw ArchiveStorageException.Stop(NapIssueCodes.ArchiveFileCollision, "A final file appeared while copying.", file.DestinationPath);
        try { File.Move(temp, file.DestinationPath, overwrite: false); }
        catch (IOException ex) when (ArchivePaths.Attributes(file.DestinationPath) is not null)
        {
            throw ArchiveStorageException.Stop(NapIssueCodes.ArchiveFileCollision, "The final file cannot be overwritten.", file.DestinationPath, ex);
        }
        ArchiveMasterPlanner.VerifyFile(file.DestinationPath, file, NapIssueCodes.ArchiveVerificationFailed);
    }

    private static bool SameIndex(ArchiveMasterIndex first, ArchiveMasterIndex second) =>
        first.UniverseId == second.UniverseId && first.Entries.Count == second.Entries.Count &&
        first.Entries.Zip(second.Entries).All(pair => pair.First.AssetId == pair.Second.AssetId && pair.First.AssetType == pair.Second.AssetType &&
            pair.First.MasterSha256 == pair.Second.MasterSha256 && pair.First.MasterSizeBytes == pair.Second.MasterSizeBytes &&
            pair.First.RelativeDirectory == pair.Second.RelativeDirectory && pair.First.Verified == pair.Second.Verified);
}

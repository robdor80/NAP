namespace NAP.Core;

/// <summary>Never enables Archive writes. Acquisition order: Production, Archive, then Catalog by the caller.</summary>
internal sealed class CatalogSourceLease : IDisposable
{
    private readonly ExecutionMutex _production;
    private readonly ArchiveLock _archive;
    private CatalogSourceLease(ExecutionMutex production, ArchiveLock archive) { _production = production; _archive = archive; }
    internal static CatalogSourceLease Acquire(UniverseContext context)
    {
        try
        {
            var production = ExecutionMutex.Acquire("Production", context.Storage.ProductionRoot);
            try { return new CatalogSourceLease(production, ArchiveLock.Acquire(context)); }
            catch { production.Dispose(); throw; }
        }
        catch (ArchiveStorageException ex) { throw CatalogException.Stop(NapIssueCodes.CatalogSourceInvalid, "The archive boundary is invalid.", inner: ex); }
        catch (IOException ex) { throw CatalogException.Stop(NapIssueCodes.CatalogBusy, "A physical source operation is active; retry explicitly.", inner: ex); }
    }
    public void Dispose() { _archive.Dispose(); _production.Dispose(); }
}

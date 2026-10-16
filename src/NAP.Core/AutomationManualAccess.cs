namespace NAP.Core;

/// <summary>Legacy synchronous entry points share the host's owner scope without creating a queue.
/// Only the actual owner thread may borrow its existing lease.</summary>
public static class AutomationManualAccess
{
    [ThreadStatic] private static Dictionary<string, AutomationQueueOwner>? _owners;
    private static string RootKey(string root) => OperatingSystem.IsWindows() ? Path.GetFullPath(root).ToUpperInvariant() : Path.GetFullPath(root);
    internal static void Register(AutomationQueueOwner owner, string root) => (_owners ??= new())[RootKey(root)] = owner;
    internal static void Unregister(AutomationQueueOwner owner, string root)
    { var key = RootKey(root); if (_owners?.GetValueOrDefault(key) == owner) _owners.Remove(key); }
    [ThreadStatic] private static ExecutionMutex? _manual;
    [ThreadStatic] private static string? _manualRoot;
    private sealed class Borrowed : IDisposable { public void Dispose() { } }
    private sealed class Owned(ExecutionMutex lease) : IDisposable
    {
        public void Dispose() { lease.Dispose(); _manual = null; _manualRoot = null; }
    }
    public static IDisposable Enter(UniverseContext context)
    {
        if (_owners?.GetValueOrDefault(RootKey(context.Storage.StateRoot)) is { } owner)
        { owner.Require(context.Storage.StateRoot); return new Borrowed(); }
        if (_manual is { } current)
        {
            if (!ProductionPaths.Same(_manualRoot!, context.Storage.StateRoot)) throw new AutomationException(AutomationError.UniverseMismatch, "Nested manual operation changed universe.");
            current.Require("AutomationOwner", _manualRoot!); return new Borrowed();
        }
        try { var lease = ExecutionMutex.Acquire("AutomationOwner", context.Storage.StateRoot); _manual = lease; _manualRoot = context.Storage.StateRoot; return new Owned(lease); }
        catch (IOException error) { throw new AutomationException(AutomationError.Busy, "AutomationHost owns this universe; use its authenticated channel.", error); }
    }
    internal static IDisposable EnterLegacy(UniverseContext context)
    {
        try { return Enter(context); }
        catch (AutomationException error) when (error.Code == AutomationError.Busy)
        { throw new IOException("The universe automation owner is occupied.", error); }
    }
}

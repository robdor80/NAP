namespace NAP.Core;

public sealed partial class AutomationHost
{
    public Task<RemovalPlan> PreviewRemovalAsync(UniverseId universe, RemovalSelection selection) =>
        QueryHeavyAsync(universe, scope =>
        {
            lock (_controlsGate)
                return new ResourceRemoval(scope.Context, scope.Queue, scope.Owner, Controls, _mixedInbox)
                    .Preview(selection, Controls.Read(universe).CancelledItems);
        });

    public Task<RemovalResult> RemoveAsync(UniverseId universe, RemovalRequest request) =>
        QueryHeavyAsync(universe, scope =>
        {
            lock (_controlsGate)
            lock (_mixedInbox?.PublicationGate ?? _controlsGate)
            {
                // The owner serializes admission and local processing. A remote response can
                // still be in flight, so defer only while this universe has an outstanding call.
                if (_dispatch?.UniverseId == universe)
                    return new(false, [], ["Espera a que termine la consulta a Gemini y vuelve a intentarlo."]);
                var result = new ResourceRemoval(scope.Context, scope.Queue, scope.Owner, Controls, _mixedInbox)
                    .Remove(request, Controls.Read(universe).CancelledItems);
                if (result.Removed)
                {
                    _controls[universe] = Controls.Read(universe);
                    var slot = Find(universe);
                    slot.Items.Clear();
                    slot.Offset = 0;
                    slot.PriorityItem = null;
                    if (_mixedReport is not null)
                        lock (_gate) _mixedReport = _mixedReport with { Receipts = _mixedInbox?.Receipts() };
                }
                return result;
            }
        });

    private void RecoverResourceRemoval(Slot slot) =>
        slot.Runtime!.InvokeAsync(scope =>
        {
            new ResourceRemoval(scope.Context, scope.Queue, scope.Owner, Controls, _mixedInbox).RecoverPending();
            return true;
        }).GetAwaiter().GetResult();
}

/// <summary>Catalog-only access when running the desktop without the automation host.</summary>
public static class LocalResourceRemoval
{
    public static RemovalPlan Preview(UniverseContext context, RemovalSelection selection)
    {
        using var lease = AutomationManualAccess.Enter(context);
        RequireLocalOnly(context);
        return new ResourceRemoval(context).Preview(selection, []);
    }
    public static RemovalResult Remove(UniverseContext context, RemovalRequest request)
    {
        using var lease = AutomationManualAccess.Enter(context);
        RequireLocalOnly(context);
        return new ResourceRemoval(context).Remove(request, []);
    }
    private static void RequireLocalOnly(UniverseContext context)
    {
        if (File.Exists(AutomationQueueBoundary.DatabasePath(context.Storage)))
            throw new InvalidOperationException("Abre NAP con su automatización configurada para retirar también los trabajos y copias internos.");
    }
}

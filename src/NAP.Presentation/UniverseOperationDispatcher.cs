using NAP.Core;

namespace NAP.Presentation;

public interface IUniverseOperationDispatcher
{
    Task<T> RunAsync<T>(UniverseContext context, Func<T> operation, CancellationToken cancellation);
}
public sealed class LocalUniverseOperationDispatcher : IUniverseOperationDispatcher
{
    public static LocalUniverseOperationDispatcher Instance { get; } = new();
    public Task<T> RunAsync<T>(UniverseContext context, Func<T> operation, CancellationToken cancellation) => Task.Run(() =>
    {
        cancellation.ThrowIfCancellationRequested(); using var owner = AutomationManualAccess.Enter(context);
        LocalUniverseSettingsStore.ValidateAvailable(context.Storage); return operation();
    }, cancellation);
}
public sealed class HostedUniverseOperationDispatcher(AutomationHost host, bool readOnly = false) : IUniverseOperationDispatcher
{
    public Task<T> RunOwnedAsync<T>(UniverseContext context, Func<AutomationUniverseScope, T> operation, CancellationToken cancellation)
        => readOnly ? throw new InvalidOperationException("Read-only dispatcher cannot retry Git.") : host.RunManualAsync(context.Id, operation, cancellation);
    public Task<GitHostAccess> GitAccessAsync(UniverseContext c, CancellationToken ct) => host.QueryAsync(c.Id, scope =>
    {
        var control = host.ReadControl(c.Id);
        if (!control.Paused && !control.Stopped) return new GitHostAccess(false, "Pausa o detén el universo en el host antes de una mutación Git.", false);
        for (var offset = 0; ; offset += 100)
        {
            var page = scope.Queue.List(offset, 100);
            if (page.Any(i => i.State == QueueState.Running)) return new GitHostAccess(false, "La cola contiene una ejecución Running; resuélvela antes del mantenimiento Git.", false);
            if (page.Count < 100) break;
        }
        return new GitHostAccess(true, null);
    }, ct);
    public Task<T> RunAsync<T>(UniverseContext context, Func<T> operation, CancellationToken cancellation)
        => readOnly ? host.QueryAsync(context.Id, _ => operation(), cancellation) : host.RunManualAsync(context.Id, _ => operation(), cancellation);
}

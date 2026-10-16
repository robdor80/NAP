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
    public Task<T> RunAsync<T>(UniverseContext context, Func<T> operation, CancellationToken cancellation)
        => readOnly ? host.QueryAsync(context.Id, _ => operation(), cancellation) : host.RunManualAsync(context.Id, _ => operation(), cancellation);
}

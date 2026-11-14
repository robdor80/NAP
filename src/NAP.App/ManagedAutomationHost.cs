using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NAP.Core;

namespace NAP.App;

/// <summary>
/// Owns the desktop launch/attachment handshake, not the durable workflow or its authority.
/// Closing WPF deliberately leaves an active Host running so the next UI can reattach.
/// NAP02 never changes the Host's existing TEMP laboratory/authorization boundary.
/// </summary>
internal sealed class ManagedAutomationHost : IDisposable
{
    private readonly Process? _spawned;
    internal AutomationHostLaunchConfiguration Configuration { get; }
    internal AutomationHostClient Client { get; }

    private ManagedAutomationHost(AutomationHostLaunchConfiguration configuration, AutomationHostClient client, Process? spawned)
    {
        Configuration = configuration;
        Client = client;
        _spawned = spawned;
    }

    internal static async Task<ManagedAutomationHost> OpenAsync(string configPath, CancellationToken cancellation = default)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("WPF Host management requires Windows.");
        var path = Path.GetFullPath(configPath);
        var file = new FileInfo(path);
        if (!file.Exists || file.Length is < 1 or > 2 * 1024 * 1024 ||
            (file.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Host launch configuration is missing, linked, or too large.");

        using var document = JsonDocument.Parse(await File.ReadAllBytesAsync(path, cancellation));
        var configuration = AutomationProtocol.Read<AutomationHostLaunchConfiguration>(document.RootElement);
        var contexts = configuration.Contexts();
        var client = new AutomationHostClient(configuration.Connection);

        // Launches are serialized across WPF processes. The Host's native writer mutex is an
        // independent final guard; UI synchronization cannot grant filesystem authority.
        var gateName = @"Local\NAPHostLaunch_" +
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant())));
        // Mutex abandonment is recoverable after an abrupt desktop crash; unlike a named
        // semaphore it does not leave a zero count behind when its owner disappears.
        // All acquire/release operations stay on ONE worker thread.
        return await Task.Run(() =>
        {
            using var gate = new Mutex(false, gateName);
            bool held;
            try { held = gate.WaitOne(TimeSpan.FromSeconds(25)); }
            catch (AbandonedMutexException) { held = true; }
            if (!held) throw new TimeoutException("Another desktop is still starting this Host.");
            try { return ConnectOrStartAsync(configuration, client, contexts, path, cancellation).GetAwaiter().GetResult(); }
            finally { gate.ReleaseMutex(); }
        }, cancellation);
    }

    private static async Task<ManagedAutomationHost> ConnectOrStartAsync(
        AutomationHostLaunchConfiguration configuration, AutomationHostClient client,
        IReadOnlyList<UniverseContext> contexts, string path, CancellationToken cancellation)
    {
        if (await CanAttachAsync(client, contexts, configuration.MixedInboxRoot, cancellation))
            return new ManagedAutomationHost(configuration, client, null);

        // NAP06 keeps two self-contained processes isolated in one installation.
        // Developer builds still ship the Host beside NAP.App.exe.
        var hostDirectory = Path.Combine(AppContext.BaseDirectory, "host");
        var executable = Path.Combine(hostDirectory, "NAP.AutomationHost.exe");
        var assembly = Path.Combine(hostDirectory, "NAP.AutomationHost.dll");
        if (!File.Exists(executable) && !File.Exists(assembly))
        {
            executable = Path.Combine(AppContext.BaseDirectory, "NAP.AutomationHost.exe");
            assembly = Path.Combine(AppContext.BaseDirectory, "NAP.AutomationHost.dll");
        }
        if (!File.Exists(executable) && !File.Exists(assembly))
            throw new FileNotFoundException("NAP.AutomationHost is absent from the application package.");

        var start = new ProcessStartInfo(File.Exists(executable) ? executable : "dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        if (!File.Exists(executable)) start.ArgumentList.Add(assembly);
        start.ArgumentList.Add(configuration.PersonalMode ? "--personal-config" : "--laboratory-config");
        start.ArgumentList.Add(path);

        var spawned = Process.Start(start) ?? throw new InvalidOperationException("Host process did not start.");
        try
        {
            var ready = await spawned.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(25), cancellation);
            var prefix = configuration.PersonalMode ? "NAP personal host ready: " : "NAP laboratory host ready: ";
            if (ready is null || !ready.StartsWith(prefix, StringComparison.Ordinal))
                throw new InvalidOperationException("Host did not report a valid startup handshake.");
            if (!await CanAttachAsync(client, contexts, configuration.MixedInboxRoot, cancellation))
                throw new InvalidDataException("Host IPC scope did not match its launch configuration.");
            return new ManagedAutomationHost(configuration, client, spawned);
        }
        catch
        {
            if (!spawned.HasExited) spawned.Kill(entireProcessTree: true);
            spawned.Dispose();
            throw;
        }
    }

    private static async Task<bool> CanAttachAsync(AutomationHostClient client,
        IReadOnlyList<UniverseContext> contexts, string? mixedInboxRoot, CancellationToken cancellation)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(6));
        try
        {
            var snapshot = AutomationProtocol.Read<AutomationHostSnapshot>(
                await client.CallAsync(AutomationOperation.Status, null, new { }, deadline.Token));
            if (string.IsNullOrEmpty(snapshot.SessionId) || string.IsNullOrEmpty(snapshot.InstallationId) ||
                snapshot.Universes.Count != contexts.Count ||
                !snapshot.Universes.Select(s => s.UniverseId).OrderBy(s => s.Value, StringComparer.Ordinal)
                    .SequenceEqual(contexts.Select(c => c.Id).OrderBy(s => s.Value, StringComparer.Ordinal)))
                throw new InvalidDataException("Host universe identities do not match the desktop configuration.");
            if (!string.Equals(mixedInboxRoot is null ? null : Path.GetFullPath(mixedInboxRoot),
                    snapshot.MixedInboxRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The attached Host routes a different mixed Inbox.");

            foreach (var context in contexts)
            {
                var overview = AutomationProtocol.Read<AutomationDesktopOverview>(
                    await client.CallAsync(AutomationOperation.Overview, context.Id.Value, new { }, deadline.Token));
                if (overview.Queue.UniverseId != context.Id || overview.Control.UniverseId != context.Id ||
                    overview.Queue.Policy.Definition.Roots != context.Storage)
                    throw new InvalidDataException("Host storage scope differs from the configured desktop universe.");
            }
            return true;
        }
        catch (Exception error) when (!cancellation.IsCancellationRequested &&
            error is IOException or TimeoutException or OperationCanceledException)
        {
            // An absent endpoint is not an authorization failure. Authentication/scope failures
            // must instead abort startup, never silently launch a competing writer.
            return false;
        }
    }

    public void Dispose() => _spawned?.Dispose(); // Does not stop, kill or orphan-claim a running Host.
}

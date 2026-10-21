using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NAP.Core;

public enum AutomationOperation
{
    Status, Activate, Pause, Resume, StopUniverse, CancelItem, ResumeItem, Batches, QueuePage, Events,
    Dashboard, Pipeline, Prepare, PrepareNormalized, Audit, Execute, Planning, SaveObjective, Backups,
    Backup, PlanRestore, Restore, PlanRetention, Retain, InspectGit, GitReceipts, PlanCommit, Commit,
    RecoverGit, RetryCommit, Push, CatalogPage, CatalogDetail, Statistics, Thumbnail,
    NormalizationPrepare, NormalizationDecide, NormalizationRead, ReadBlobChunk, CurrentPolicy, ShutdownHost, ReleaseBlob, CurrentControl, Overview
}
public sealed record AutomationRequest(int Version, string RequestId, AutomationOperation Operation,
    string? UniverseId, JsonElement Payload);
public sealed record AutomationResponse(int Version, string RequestId, bool Succeeded, string? Error, JsonElement Payload);
public sealed record AutomationConnection(string PipeName, string KeyBase64);
public sealed record AutomationActivation(PolicyReference Policy, bool ResumeOnRestart);
public sealed record AutomationPageRequest(int Offset = 0, int Limit = 100);
public sealed record AutomationItemRequest(string ItemId);
public sealed record AutomationBlobRead(string BlobId, int Offset, int Count);
public sealed record AutomationBlob(string BlobId, int Length, string Sha256);
public sealed class AutomationRemoteOperationException(string code, IReadOnlyList<NapIssue> issues) : Exception("Host operation stopped: " + code)
{
    public NapIssueReport Issues { get; } = new(issues);
}

public static class AutomationProtocol
{
    public const int Version = 1;
    public const int MaximumFrameBytes = 1024 * 1024;
    public static JsonSerializerOptions Json { get; } = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 48,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
    public static JsonElement Payload<T>(T value) => JsonSerializer.SerializeToElement(value, Json);
    public static T Read<T>(JsonElement value)
    {
        Unique(value);
        return value.Deserialize<T>(Json) ?? throw new InvalidDataException("Null protocol value.");
    }
    private static void Unique(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject()) { if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate protocol field."); Unique(property.Value); }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) Unique(item);
    }
    internal static byte[] Key(AutomationConnection connection)
    {
        if (!ProductionPaths.SafeSegment(connection.PipeName) || connection.PipeName.Length > 100 || !connection.PipeName.StartsWith("nap-", StringComparison.Ordinal))
            throw new ArgumentException("Invalid local endpoint.");
        var key = Convert.FromBase64String(connection.KeyBase64);
        if (key.Length != 32) throw new ArgumentException("A random 256-bit connection key is required."); return key;
    }
    internal static byte[] Proof(byte[] key, string role, byte[] client, byte[] server)
        => HMACSHA256.HashData(key, (byte[])[.. Encoding.ASCII.GetBytes("NAP-IPC-v1:" + role), .. client, .. server]);
    internal static async Task WriteAsync<T>(Stream stream, T value, CancellationToken cancellation)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        if (bytes.Length is < 1 or > MaximumFrameBytes) throw new InvalidDataException("Protocol frame exceeds its bound.");
        var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header, cancellation).ConfigureAwait(false); await stream.WriteAsync(bytes, cancellation).ConfigureAwait(false);
        await stream.FlushAsync(cancellation).ConfigureAwait(false);
    }
    internal static async Task<T> ReadAsync<T>(Stream stream, CancellationToken cancellation)
    {
        var header = new byte[4]; await stream.ReadExactlyAsync(header, cancellation).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is < 1 or > MaximumFrameBytes) throw new InvalidDataException("Invalid frame length.");
        var bytes = new byte[length]; await stream.ReadExactlyAsync(bytes, cancellation).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 48 }); return Read<T>(doc.RootElement);
    }
}

/// <summary>Authenticated local-only, same-OS-principal laboratory client. An unavailable host never falls back to direct writes.</summary>
public sealed class AutomationHostClient
{
    private readonly AutomationConnection _connection;
    private readonly byte[] _key;
    public AutomationHostClient(AutomationConnection connection) { _connection = connection; _key = AutomationProtocol.Key(connection); }
    public async Task<AutomationResponse> SendAsync(AutomationRequest request, CancellationToken cancellation = default)
    {
        using var pipe = new NamedPipeClientStream(".", _connection.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(5000, cancellation).ConfigureAwait(false);
        var client = RandomNumberGenerator.GetBytes(32); await pipe.WriteAsync(client, cancellation).ConfigureAwait(false);
        var server = new byte[32]; var proof = new byte[32];
        await pipe.ReadExactlyAsync(server, cancellation).ConfigureAwait(false); await pipe.ReadExactlyAsync(proof, cancellation).ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(proof, AutomationProtocol.Proof(_key, "server", client, server))) throw new UnauthorizedAccessException("Host authentication failed.");
        await pipe.WriteAsync(AutomationProtocol.Proof(_key, "client", client, server), cancellation).ConfigureAwait(false);
        await AutomationProtocol.WriteAsync(pipe, request, cancellation).ConfigureAwait(false);
        var response = await AutomationProtocol.ReadAsync<AutomationResponse>(pipe, cancellation).ConfigureAwait(false);
        if (response.Version != AutomationProtocol.Version || response.RequestId != request.RequestId) throw new InvalidDataException("Host response correlation differs.");
        return response;
    }
    public async Task<JsonElement> CallAsync<T>(AutomationOperation operation, string? universe, T payload,
        CancellationToken cancellation = default, string? requestId = null)
    {
        var request = new AutomationRequest(1, requestId ?? "command_" + Guid.NewGuid().ToString("N"), operation, universe, AutomationProtocol.Payload(payload));
        var response = await SendAsync(request, cancellation).ConfigureAwait(false);
        if (!response.Succeeded)
        {
            if (response.Payload.ValueKind == JsonValueKind.Array)
                throw new AutomationRemoteOperationException(response.Error ?? "host_stopped", AutomationProtocol.Read<NapIssue[]>(response.Payload));
            throw new AutomationException(Enum.TryParse<AutomationError>(response.Error, out var code) ? code : AutomationError.InvalidContract, "Host operation failed: " + response.Error);
        }
        return response.Payload;
    }
}

/// <summary>Four bounded connections, mutually authenticated challenge, typed messages, one request per connection.
/// Disconnecting a caller does not cancel accepted effects. Startup under another identity is a later deployment decision.</summary>
public sealed class AutomationPipeServer : IAsyncDisposable
{
    private readonly AutomationConnection _connection;
    private readonly byte[] _key;
    private readonly Func<AutomationRequest, string, Task<JsonElement>> _handler;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _connections = new(4, 4);
    private readonly ConcurrentDictionary<int, Task> _clients = new();
    private readonly Task _accept;
    private int _sequence;
    public AutomationPipeServer(AutomationConnection connection, Func<AutomationRequest, string, Task<JsonElement>> handler)
    { _connection = connection; _key = AutomationProtocol.Key(connection); _handler = handler; _accept = AcceptAsync(); }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null; bool acquired = false;
            try
            {
                await _connections.WaitAsync(_stop.Token).ConfigureAwait(false); acquired = true;
                pipe = new(_connection.PipeName, PipeDirection.InOut, 4, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                var id = Interlocked.Increment(ref _sequence); var client = ServeAsync(pipe); _clients[id] = client;
                _ = client.ContinueWith(t => { _ = t.Exception; _clients.TryRemove(id, out _); }, CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                pipe = null; acquired = false;
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
            finally { pipe?.Dispose(); if (acquired) _connections.Release(); }
        }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe)
    {
        using (pipe)
        using (var handshake = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
        {
            handshake.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                var client = new byte[32]; await pipe.ReadExactlyAsync(client, handshake.Token).ConfigureAwait(false);
                var server = RandomNumberGenerator.GetBytes(32); await pipe.WriteAsync(server, handshake.Token).ConfigureAwait(false);
                await pipe.WriteAsync(AutomationProtocol.Proof(_key, "server", client, server), handshake.Token).ConfigureAwait(false);
                var proof = new byte[32]; await pipe.ReadExactlyAsync(proof, handshake.Token).ConfigureAwait(false);
                if (!CryptographicOperations.FixedTimeEquals(proof, AutomationProtocol.Proof(_key, "client", client, server))) return;
                var request = await AutomationProtocol.ReadAsync<AutomationRequest>(pipe, handshake.Token).ConfigureAwait(false);
                if (request.Version != 1 || !Enum.IsDefined(request.Operation)) throw new InvalidDataException("Unsupported protocol.");
                AutomationValidation.Id(request.RequestId, "command_");
                string principal;
                if (OperatingSystem.IsWindows())
                {
                    string? sid = null;
                    pipe.RunAsClient(() => { if (OperatingSystem.IsWindows()) { using var identity = System.Security.Principal.WindowsIdentity.GetCurrent(); sid = identity.User?.Value; } });
                    principal = sid ?? throw new UnauthorizedAccessException("No authenticated client SID.");
                }
                else principal = pipe.GetImpersonationUserName();
                AutomationResponse response;
                try { response = new(1, request.RequestId, true, null, await _handler(request, principal).ConfigureAwait(false)); }
                catch (Exception error)
                {
                    var report = error switch
                    {
                        AutomationRemoteOperationException r => r.Issues, CatalogException r => r.Issues, ProductionStorageException r => r.Issues,
                        ArchiveStorageException r => r.Issues, BackupException r => r.Issues, GitProductionException r => r.Issues, _ => null
                    };
                    response = new(1, request.RequestId, false, error is AutomationException a ? a.Code.ToString() : error.GetType().Name,
                        report is null ? AutomationProtocol.Payload<object?>(null) : AutomationProtocol.Payload(report.Issues.ToArray()));
                }
                // Once accepted, the operation is drained even if the client disconnects or server shutdown is requested.
                using var replyDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await AutomationProtocol.WriteAsync(pipe, response, replyDeadline.Token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or OperationCanceledException or UnauthorizedAccessException or InvalidDataException or JsonException or AutomationException) { }
            finally { _connections.Release(); }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel(); await _accept.ConfigureAwait(false);
        await Task.WhenAll(_clients.Values).ConfigureAwait(false); // no forced close of accepted operations
    }
}

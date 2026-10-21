using System.Security.Cryptography;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class AutomationProtocolTests
{
    [Fact]
    public async Task MutualAuthenticationRejectsWrongKeyWithoutInvokingHandler()
    {
        var connection = new AutomationConnection("nap-test-" + Guid.NewGuid().ToString("N"), Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        int calls = 0;
        await using var server = new AutomationPipeServer(connection, (request, principal) =>
        { Assert.False(string.IsNullOrWhiteSpace(principal)); Interlocked.Increment(ref calls); return Task.FromResult(AutomationProtocol.Payload("synthetic")); });
        var bad = new AutomationHostClient(connection with { KeyBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => bad.CallAsync(AutomationOperation.Status, null, new { }));
        Assert.Equal(0, calls);
        var good = new AutomationHostClient(connection); var result = await good.CallAsync(AutomationOperation.Status, null, new { });
        Assert.Equal("synthetic", result.GetString()); Assert.Equal(1, calls);
    }

    [Fact]
    public async Task UnsupportedVersionDoesNotExecuteAndDisconnectDoesNotCancelAcceptedOperation()
    {
        var connection = new AutomationConnection("nap-test-" + Guid.NewGuid().ToString("N"), Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new AutomationPipeServer(connection, async (_, _) =>
        { entered.SetResult(); await release.Task; completed.SetResult(); return AutomationProtocol.Payload(true); });
        var client = new AutomationHostClient(connection);
        await Assert.ThrowsAnyAsync<IOException>(() => client.SendAsync(new(99, "command_" + Guid.NewGuid().ToString("N"), AutomationOperation.Status, null, AutomationProtocol.Payload(new { }))));
        using var cancel = new CancellationTokenSource(); var pending = client.CallAsync(AutomationOperation.Status, null, new { }, cancel.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending); release.SetResult(); await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
}

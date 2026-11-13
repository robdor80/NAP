using System.Reflection;
using NAP.Core;
using NAP.Presentation;
using Xunit;

namespace NAP.Tests;

/// <summary>NAP05: narrow, synthetic regressions. No product roots or network calls.</summary>
public sealed class Nap05RecoveryTests
{
    [Fact]
    public async Task FailedStopForOneUniverseStillPersistsOtherUniverseWithoutInventingSuccess()
    {
        using var f = new AutomationHostTests.Fixture();
        var policyA = AutomationHostTests.Fixture.Enable(f.A);
        var policyB = AutomationHostTests.Fixture.Enable(f.B);
        await using var host = new AutomationHost(f.Control, [f.A.Context, f.B.Context], f.Options);
        await host.Started;
        await host.ActivateAsync(f.A.Context.Id, policyA.Reference, resumeOnRestart: true);
        await host.ActivateAsync(f.B.Context.Id, policyB.Reference, resumeOnRestart: true);

        var hook = typeof(AutomationHostControlStore).GetProperty("BeforeCommit", BindingFlags.Instance | BindingFlags.NonPublic)!;
        int attempted = 0;
        hook.SetValue(host.Controls, (Action)(() =>
        {
            if (Interlocked.Increment(ref attempted) == 1)
                throw new IOException("synthetic first-universe STOP commit failure");
        }));
        try
        {
            host.RequestStop();
            await host.Completion.WaitAsync(TimeSpan.FromSeconds(15));

            Assert.Equal(2, attempted);
            Assert.Equal(AutomationHostState.Stopped, host.Snapshot().State);
            Assert.StartsWith("stop_not_persisted:", host.Snapshot().Diagnostic);
            Assert.False(host.ReadControl(f.A.Context.Id).Stopped);
            Assert.True(host.ReadControl(f.B.Context.Id).Stopped);
            Assert.Empty(Directory.EnumerateFiles(f.A.Roots.ProductionRoot, "*.webp", SearchOption.AllDirectories));
            Assert.Empty(Directory.EnumerateFiles(f.B.Roots.ProductionRoot, "*.webp", SearchOption.AllDirectories));
        }
        finally { hook.SetValue(host.Controls, null); }
    }

    [Theory]
    [InlineData("io", "Se perdió la comunicación")]
    [InlineData("timeout", "No se pudo conectar")]
    [InlineData("auth", "El Host rechazó")]
    [InlineData("other", "No se pudo consultar")]
    public async Task ReadErrorsOfferActionWithoutReplayingACommand(string failure, string expected)
    {
        using var f = new AutomationHostTests.Fixture();
        var client = new FailedOverviewClient(failure);
        using var vm = new AutomationControlViewModel(() => f.A.Context, new Confirmation(), client);
        await vm.RefreshAsync();

        Assert.Contains(expected, vm.Status, StringComparison.Ordinal);
        Assert.Equal(1, client.Reads);
        Assert.Equal(0, client.Commands);
        Assert.Equal("—", vm.Completed);
        Assert.False(vm.CanProcessPending);
    }

    [Fact]
    public void StorageIncidentExplainsRecoveryReviewInsteadOfImplicitRetry()
    {
        var message = AutomationControlViewModel.Problem(QueueIncident.StorageUnavailable);
        Assert.Contains("comprueba", message, StringComparison.Ordinal);
        Assert.Contains("antes de reanudar", message, StringComparison.Ordinal);
    }

    private sealed class Confirmation : IUserConfirmation
    {
        public Task<bool> ConfirmAsync(UiConfirmation confirmation, CancellationToken cancellation) => Task.FromResult(false);
    }

    private sealed class FailedOverviewClient(string failure) : IAutomationOverviewClient
    {
        public int Reads { get; private set; }
        public int Commands { get; private set; }
        public Task<AutomationDesktopOverview> ReadAsync(UniverseId universe, CancellationToken cancellation)
        {
            Reads++;
            Exception error = failure switch
            {
                "io" => new IOException("synthetic pipe disconnection"),
                "timeout" => new TimeoutException("synthetic connect timeout"),
                "auth" => new UnauthorizedAccessException("synthetic authentication rejection"),
                _ => new InvalidDataException("synthetic unexpected response")
            };
            return Task.FromException<AutomationDesktopOverview>(error);
        }
        public Task SendAsync(AutomationOperation operation, UniverseId? universe, object payload, CancellationToken cancellation)
        {
            Commands++;
            throw new InvalidOperationException("Read-only tests must not issue commands.");
        }
    }
}

using Microsoft.Data.Sqlite;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class AutomationHostControlTests
{
    [Fact]
    public void CommandRollbackKeepsRevisionAndReplayIdentityUnchanged()
    {
        using var f = new PreparationTestFixture(); var directory = Path.Combine(f.Root, "host"); Directory.CreateDirectory(directory);
        var store = new AutomationHostControlStore(directory, [f.Context]); store.Initialize(); var session = store.BeginSession();
        var command = new AutomationControlCommand("command_" + Guid.NewGuid().ToString("N"), f.Context.Id, 0, AutomationControlAction.Pause, session);
        var hook = typeof(AutomationHostControlStore).GetProperty("BeforeCommit", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        hook.SetValue(store, (Action)(() => throw new PreparationTestFixture.SimulatedCrash()));
        Assert.Throws<PreparationTestFixture.SimulatedCrash>(() => store.Apply(command));
        hook.SetValue(store, null); Assert.Equal(0, store.Read(f.Context.Id).Revision); Assert.Equal(1, store.Apply(command).Revision);
    }

    [Fact]
    public void PauseSurvivesReopenAndCommandReplayCannotChangeMeaning()
    {
        using var f = new PreparationTestFixture(); var directory = Path.Combine(f.Root, "host"); Directory.CreateDirectory(directory);
        var store = new AutomationHostControlStore(directory, [f.Context]); store.Initialize();
        var session = store.BeginSession();
        var command = new AutomationControlCommand("command_" + Guid.NewGuid().ToString("N"), f.Context.Id, 0, AutomationControlAction.Pause, session);
        var result = store.Apply(command); Assert.True(result.Paused);
        var reopened = new AutomationHostControlStore(directory, [f.Context]);
        var replay = reopened.Apply(command); Assert.Equal(result.Revision, replay.Revision); Assert.Equal(result.Paused, replay.Paused);
        Assert.Equal(result.Revision, reopened.Read(f.Context.Id).Revision);
        Assert.Throws<AutomationException>(() => reopened.Apply(command with { Action = AutomationControlAction.Stop }));
        Assert.Throws<AutomationException>(() => reopened.Apply(command with { Id = "command_" + Guid.NewGuid().ToString("N") }));
    }

    [Fact]
    public void CorruptControlIsPreservedAndStorageCannotOverlapUniverse()
    {
        using var f = new PreparationTestFixture();
        Assert.Throws<AutomationException>(() => new AutomationHostControlStore(f.Roots.StateRoot, [f.Context]));
        var directory = Path.Combine(f.Root, "host"); Directory.CreateDirectory(directory);
        var store = new AutomationHostControlStore(directory, [f.Context]); store.Initialize();
        using (var db = new SqliteConnection("Data Source=" + store.DatabasePath + ";Pooling=False"))
        { db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "UPDATE controls SET hash='changed'"; cmd.ExecuteNonQuery(); }
        var bytes = File.ReadAllBytes(store.DatabasePath);
        Assert.Throws<AutomationException>(() => store.Read(f.Context.Id)); Assert.Equal(bytes, File.ReadAllBytes(store.DatabasePath));
    }

    [Fact]
    public void MissingOrUnknownControlArtifactsDoNotGetAdopted()
    {
        using var f = new PreparationTestFixture(); var directory = Path.Combine(f.Root, "host"); Directory.CreateDirectory(directory);
        var store = new AutomationHostControlStore(directory, [f.Context]);
        Assert.Throws<AutomationException>(() => store.Read(f.Context.Id)); Assert.False(File.Exists(store.DatabasePath));
        File.WriteAllText(Path.Combine(directory, "unknown.tmp"), "synthetic evidence");
        Assert.Throws<AutomationException>(() => store.Initialize()); Assert.Equal("synthetic evidence", File.ReadAllText(Path.Combine(directory, "unknown.tmp")));
    }
}

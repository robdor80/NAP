using System.Diagnostics;
using System.Text;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class CatalogConcurrencyTests
{
    [Fact]
    public void SimultaneousSchemaCreationProducesOneCoherentCatalog()
    {
        using var f = new CatalogTestFixture(); using var start = new Barrier(2); var errors = new Exception?[2];
        var workers = Enumerable.Range(0, 2).Select(i => new Thread(() => { Assert.True(start.SignalAndWait(TimeSpan.FromSeconds(10))); errors[i] = Record.Exception(() => f.Catalog.Initialize()); })).ToArray();
        foreach (var worker in workers) worker.Start(); foreach (var worker in workers) Assert.True(worker.Join(TimeSpan.FromSeconds(10)));
        Assert.Contains(errors, e => e is null); foreach (var error in errors.Where(e => e is not null)) CatalogTestFixture.Stop(Assert.IsType<CatalogException>(error), NapIssueCodes.CatalogBusy);
        f.Catalog.Initialize(); f.Catalog.CheckIntegrity(); Assert.Empty(f.Catalog.Query());
    }
    [Fact]
    public void ActualRebuildRacingActualUpdateIsCoherentAfterExplicitBusyRetry()
    {
        using var f = new CatalogTestFixture(); f.Publish("emblem_a_001", automatic: true); var other = f.Publish("emblem_b_002");
        using var start = new Barrier(2); var errors = new Exception?[2];
        Action[] work = [() => new CatalogRebuilder(f.Context).Rebuild(), () => f.Catalog.RegisterVerified(other.Package, other.Plan, other.Archive, other.Production)];
        var workers = Enumerable.Range(0, 2).Select(i => new Thread(() => { Assert.True(start.SignalAndWait(TimeSpan.FromSeconds(10))); errors[i] = Record.Exception(work[i]); })).ToArray();
        foreach (var worker in workers) worker.Start(); foreach (var worker in workers) Assert.True(worker.Join(TimeSpan.FromSeconds(10)));
        for (var i = 0; i < errors.Length; i++) if (errors[i] is not null) { CatalogTestFixture.Stop(Assert.IsType<CatalogException>(errors[i]), NapIssueCodes.CatalogBusy); work[i](); }
        Assert.Equal(2, f.Catalog.Query().Count); f.Catalog.CheckIntegrity();
    }
    [Theory]
    [InlineData("initialize")] [InlineData("update")] [InlineData("rebuild")] [InlineData("import")]
    public void HeldCatalogCoordinationRejectsSecondWriterAndExplicitRetrySucceeds(string operation)
    {
        using var f = new CatalogTestFixture(); var asset = f.Publish(); var importer = new CatalogImporter(f.Context); var plan = importer.Plan();
        using (var held = (IDisposable)CatalogTestFixture.Invoke(null, "CatalogBoundary", "Acquire", f.Context)!)
        {
            var before = ArchiveTestFixture.Snapshot(f.Root); Exception? error = null;
            var worker = new Thread(() => error = Record.Exception(() => Act())); worker.Start(); Assert.True(worker.Join(TimeSpan.FromSeconds(10)));
            CatalogTestFixture.Stop(Assert.IsType<CatalogException>(error), NapIssueCodes.CatalogBusy); ArchiveTestFixture.AssertSnapshot(before, f.Root);
        }
        Act(); f.Catalog.CheckIntegrity();
        void Act()
        {
            switch (operation)
            {
                case "initialize": f.Catalog.Initialize(); break;
                case "update": f.Catalog.RegisterVerified(asset.Package, asset.Plan, asset.Archive, asset.Production); break;
                case "rebuild": new CatalogRebuilder(f.Context).Rebuild(); break;
                case "import": importer.Import(plan); break;
            }
        }
    }
    [Fact]
    public void IndependentSqliteWriterProducesBusyWithoutPartialWritesAndCanRetry()
    {
        using var f = new CatalogTestFixture(); var asset = f.Publish(); f.Catalog.Initialize();
        using (var other = f.Raw())
        {
            CatalogTestFixture.Execute(other, "BEGIN IMMEDIATE");
            CatalogTestFixture.Stop(Assert.Throws<CatalogException>(() => f.Catalog.RegisterVerified(asset.Package, asset.Plan, asset.Archive, asset.Production)), NapIssueCodes.CatalogBusy);
            Assert.Equal(0L, CatalogTestFixture.Scalar(other, "SELECT COUNT(*) FROM assets")); CatalogTestFixture.Execute(other, "ROLLBACK");
        }
        Assert.True(f.Catalog.RegisterVerified(asset.Package, asset.Plan, asset.Archive, asset.Production)); f.Catalog.CheckIntegrity();
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void TwoAssetsOrSameIdentityConcurrentAttemptsHaveNoLostUpdates(bool sameIdentity)
    {
        using var f = new CatalogTestFixture(); var a = f.Publish("emblem_a_001"); var b = sameIdentity ? a : f.Publish("emblem_b_002");
        var failures = new List<Exception?>(); var sync = new object();
        using (var held = (IDisposable)CatalogTestFixture.Invoke(null, "CatalogBoundary", "Acquire", f.Context)!)
        {
            var workers = new[] { a, b }.Select(result => new Thread(() =>
            {
                var error = Record.Exception(() => f.Catalog.RegisterVerified(result.Package, result.Plan, result.Archive, result.Production));
                lock (sync) failures.Add(error);
            })).ToArray();
            foreach (var worker in workers) worker.Start(); foreach (var worker in workers) Assert.True(worker.Join(TimeSpan.FromSeconds(10)));
            Assert.All(failures, failure => CatalogTestFixture.Stop(Assert.IsType<CatalogException>(failure), NapIssueCodes.CatalogBusy));
        }
        Assert.True(f.Catalog.RegisterVerified(a.Package, a.Plan, a.Archive, a.Production));
        Assert.Equal(!sameIdentity, f.Catalog.RegisterVerified(b.Package, b.Plan, b.Archive, b.Production));
        Assert.Equal(sameIdentity ? 1 : 2, f.Catalog.Query().Count); f.Catalog.CheckIntegrity();
    }
    [Fact]
    public async Task CatalogMutexCoordinatesAnotherProcessWithoutLockFiles()
    {
        using var f = new CatalogTestFixture(); var canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(f.Context.Storage.StateRoot));
        if (OperatingSystem.IsWindows()) canonical = canonical.ToUpperInvariant();
        using var bytes = new MemoryStream(Encoding.UTF8.GetBytes(canonical + "\nAssetCatalog.db"));
        var name = "NAP.Execution.Catalog." + new Sha256Hasher().Compute(bytes).Hex;
        var script = "$m=[System.Threading.Mutex]::new($false,'" + name + "'); if (!$m.WaitOne(0)) {exit 2}; try {[Console]::WriteLine('LOCKED'); [Console]::Out.Flush(); [Console]::ReadLine() | Out-Null} finally {$m.ReleaseMutex(); $m.Dispose()}";
        var start = new ProcessStartInfo { FileName = OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh", UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-EncodedCommand"); start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        using var process = Process.Start(start)!;
        try
        {
            Assert.Equal("LOCKED", await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)));
            var before = ArchiveTestFixture.Snapshot(f.Root);
            CatalogTestFixture.Stop(Assert.Throws<CatalogException>(() => f.Catalog.Initialize()), NapIssueCodes.CatalogBusy); ArchiveTestFixture.AssertSnapshot(before, f.Root);
        }
        finally { process.StandardInput.WriteLine("release"); process.StandardInput.Flush(); if (!process.WaitForExit(10000)) { process.Kill(); process.WaitForExit(); } }
        Assert.Equal(0, process.ExitCode); f.Catalog.Initialize(); Assert.DoesNotContain(Directory.GetFiles(f.Context.Storage.StateRoot), p => p.EndsWith(".lock"));
    }
}

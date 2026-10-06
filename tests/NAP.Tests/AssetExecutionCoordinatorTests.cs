using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using NAP.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace NAP.Tests;

public sealed class AssetExecutionCoordinatorTests
{
    [Theory]
    [InlineData(JobState.Planned)] [InlineData(JobState.Audited)] [InlineData(JobState.Executed)] [InlineData(JobState.Verified)]
    public void ActiveCheckpointsResumeToVerifiedCompletionAndTerminalRunIsReadOnly(JobState state)
    {
        using var f = new ProductionTestFixture(); var job = f.Job(state >= JobState.Executed ? JobState.Audited : state); var archivePlan = f.ArchivePlan();
        if (state >= JobState.Executed)
        {
            var archive = f.Archive(); f.Execute(f.Plan(archive, jobId: job));
            var states = new JobStateStore(f.Context); states.Transition(job, JobState.Executed);
            if (state == JobState.Verified) states.Transition(job, JobState.Verified);
        }
        var result = new AssetExecutionCoordinator(f.Context).Execute(job, f.Package, f.Processing, archivePlan, ProductionTestFixture.Pass, ProductionTestFixture.Budget);
        Assert.Equal(job, result.JobId); Assert.Equal(JobState.Completed, result.FinalState);
        Assert.Equal(JobState.Completed, new JobStateStore(f.Context).Load(job).State);
        var before = ArchiveTestFixture.Snapshot(f.Root);
        var second = new AssetExecutionCoordinator(f.Context).Execute(job, f.Package, f.Processing, archivePlan, ProductionTestFixture.Pass, ProductionTestFixture.Budget);
        Assert.Equal(ArchiveMasterOutcome.AlreadyArchived, second.Archive.Outcome);
        Assert.Equal(ProductionAssetOutcome.AlreadyProduced, second.Production.Outcome);
        ArchiveTestFixture.AssertSnapshot(before, f.Root);
        Assert.False(File.Exists(f.Context.Storage.CatalogPath));
        Assert.DoesNotContain(Directory.GetFiles(f.Context.Storage.StateRoot), p => Path.GetFileName(p).Contains("lock", StringComparison.OrdinalIgnoreCase));
        Assert.Single(new JobRecoveryScanner(f.Context).Scan().CompletedJobs);
    }

    [Theory]
    [InlineData(JobState.Detected)] [InlineData(JobState.Staged)] [InlineData(JobState.Validated)] [InlineData(JobState.Failed)]
    public void EarlyAndFailedJobsAreRejectedWithoutChangingAnything(JobState state)
    {
        using var f = new ProductionTestFixture(); var job = f.Job(state); var archive = f.ArchivePlan(); var before = ArchiveTestFixture.Snapshot(f.Root);
        Assert.Throws<InvalidOperationException>(() => new AssetExecutionCoordinator(f.Context).Execute(job, f.Package, f.Processing, archive, ProductionTestFixture.Pass, ProductionTestFixture.Budget));
        ArchiveTestFixture.AssertSnapshot(before, f.Root);
        Assert.Equal(state, new JobStateStore(f.Context).Load(job).State);
    }

    [Theory]
    [InlineData("missing")] [InlineData("corrupt")] [InlineData("unexpected")]
    [InlineData("archive_missing")] [InlineData("index_missing")] [InlineData("root_missing")]
    public void CompletedInconsistencyIsStopAndNeverRepaired(string mutation)
    {
        using var f = new ProductionTestFixture(); var job = f.Job(); var archive = f.ArchivePlan(); var coordinator = new AssetExecutionCoordinator(f.Context);
        var result = coordinator.Execute(job, f.Package, f.Processing, archive, ProductionTestFixture.Pass, ProductionTestFixture.Budget);
        var file = result.Production.FilesVerified[0];
        switch (mutation)
        {
            case "missing": File.Delete(file.DestinationPath); break;
            case "corrupt": File.AppendAllText(file.DestinationPath, "corrupt"); break;
            case "unexpected": File.WriteAllText(Path.Combine(Path.GetDirectoryName(file.DestinationPath)!, "unexpected.txt"), "unknown"); break;
            case "archive_missing": File.Delete(result.Archive.FilesVerified[0].DestinationPath); break;
            case "index_missing": File.Delete(new ArchiveMasterIndexStore(f.Context).IndexPath); break;
            case "root_missing": Directory.Move(f.Context.Storage.ProductionRoot, Path.Combine(f.Root, "moved_production")); break;
        }
        var before = ArchiveTestFixture.Snapshot(f.Root);
        ProductionTestFixture.Stop(Assert.Throws<ProductionStorageException>(() => coordinator.Execute(job, f.Package, f.Processing, archive, ProductionTestFixture.Pass, ProductionTestFixture.Budget)), NapIssueCodes.ProductionCompletedInconsistent);
        ArchiveTestFixture.AssertSnapshot(before, f.Root); Assert.Equal(JobState.Completed, new JobStateStore(f.Context).Load(job).State);
    }

    [Theory]
    [InlineData(JobState.Audited)] [InlineData(JobState.Executed)] [InlineData(JobState.Verified)]
    public void DurableCheckpointAfterPublicationResumesUsingSameJobProvenance(JobState checkpoint)
    {
        using var f = new ProductionTestFixture(); var job = f.Job(JobState.Audited); var archive = f.ArchivePlan();
        var coordinator = new AssetExecutionCoordinator(f.Context);
        // Real publication + durable provenance, then simulated process exit at each legal checkpoint; portable.
        f.Execute(f.Plan(f.Archive(), jobId: job));
        var states = new JobStateStore(f.Context);
        if (checkpoint >= JobState.Executed) states.Transition(job, JobState.Executed);
        if (checkpoint == JobState.Verified) states.Transition(job, JobState.Verified);
        Assert.Equal(checkpoint, states.Load(job).State);
        var physical = f.Plan(f.Archive(), jobId: job); Assert.Equal(ProductionAssetAction.AlreadyProduced, physical.Action);
        var before = ArchiveTestFixture.Snapshot(f.Context.Storage.ProductionRoot);
        var result = coordinator.Execute(job, f.Package, f.Processing, archive, ProductionTestFixture.Pass, ProductionTestFixture.Budget);
        Assert.Equal(ProductionAssetOutcome.AlreadyProduced, result.Production.Outcome);
        ArchiveTestFixture.AssertSnapshot(before, f.Context.Storage.ProductionRoot);
        Assert.Equal(JobState.Completed, new JobStateStore(f.Context).Load(job).State);
    }

    [Fact]
    public void FailureAfterArchiveLeavesAuditedAndCanResumeProduction()
    {
        using var f = new ProductionTestFixture(); var job = f.Job(); var archive = f.ArchivePlan();
        using (var held = (IDisposable)ProductionTestFixture.Invoke(null, "ExecutionMutex", "Acquire", "Production", f.Context.Storage.ProductionRoot, "")!)
            Assert.Throws<IOException>(() => new AssetExecutionCoordinator(f.Context).Execute(job, f.Package, f.Processing, archive, ProductionTestFixture.Pass, ProductionTestFixture.Budget));
        Assert.Equal(JobState.Audited, new JobStateStore(f.Context).Load(job).State);
        Assert.Equal(ArchiveMasterAction.AlreadyArchived, f.ArchivePlan().Action);
        Assert.Empty(Directory.GetFileSystemEntries(f.Context.Storage.ProductionRoot));
        var result = new AssetExecutionCoordinator(f.Context).Execute(job, f.Package, f.Processing, archive, ProductionTestFixture.Pass, ProductionTestFixture.Budget);
        Assert.Equal(ArchiveMasterOutcome.AlreadyArchived, result.Archive.Outcome); Assert.Equal(JobState.Completed, result.FinalState);
    }

    [Fact]
    public void MismatchedFrozenArchivePlanIsRejectedBeforeAudited()
    {
        using var f = new ProductionTestFixture(); var job = f.Job(); var archive = f.ArchivePlan();
        File.AppendAllText(f.Package.FilesByRole["prompt"], "new package bytes");
        var before = ArchiveTestFixture.Snapshot(f.Root);
        ProductionTestFixture.Stop(Assert.Throws<ProductionStorageException>(() => new AssetExecutionCoordinator(f.Context).Execute(job, f.Package, f.Processing, archive, ProductionTestFixture.Pass, ProductionTestFixture.Budget)), NapIssueCodes.ProductionArchiveMismatch);
        ArchiveTestFixture.AssertSnapshot(before, f.Root); Assert.Equal(JobState.Planned, new JobStateStore(f.Context).Load(job).State);
    }

    [Fact]
    public async Task RealNimroelZipThroughExistingPipelineCompletesAndSecondRunNeverRewrites()
    {
        using var f = new ProductionTestFixture(nimroel: true);
        File.WriteAllText(f.Package.FilesByRole["prompt"], "# Portrait prompt\r\n\r\nSynthetic full-frame farmer portrait.\r\n");
        File.WriteAllText(f.Package.FilesByRole["info"], "# Portrait information\r\n\r\nIsolated production integration fixture.\r\n");
        File.WriteAllText(f.Package.FilesByRole["visual_identity"], "{\r\n  \"asset_id\": \"portrait_treskal_farmer_male_040\",\r\n  \"description\": \"Synthetic test portrait\"\r\n}\r\n");
        f.Refresh();
        var master = File.ReadAllBytes(f.Master);
        var inbox = new InboxPreparer().Prepare(f.Context.Storage.InboxRoot);
        var zip = Path.Combine(inbox, f.Package.AssetKey.AssetId + ".zip"); ZipFile.CreateFromDirectory(f.Package.PackageRoot, zip);
        var candidate = Assert.Single(new InboxPackageDetector().Detect(inbox));
        var checker = new InboxPackageReadinessChecker(new InboxPackageReadinessOptions { RequiredSamples = 2, SampleInterval = TimeSpan.Zero });
        var staged = await new InboxPackageStager(checker).StageAsync(candidate, f.Context.Storage.StagingRoot);
        Assert.Equal(InboxPackageStagingStatus.Staged, staged.Status);
        var extracted = await new StagedPackageExtractor().ExtractAsync(staged.FinalStagedPath, Path.Combine(f.Context.Storage.WorkspaceRoot, "extracted"));
        Assert.Equal(StagedPackageExtractionStatus.Extracted, extracted.Status);
        var package = new PackageSemanticValidator().Validate(extracted.FinalPath!, f.Context).Package!;
        Assert.NotNull(package);
        var repository = new ProductionRepositoryValidator().Validate(f.Context).Repository!;
        var route = new ProductionDestinationResolver().Resolve(package, repository);
        var processing = new ProcessingPlanBuilder().Build(package, repository, route);
        var deterministic = new ProcessingPlanValidator().Validate(processing, new ProductionRepositoryScanner().Scan(repository).Snapshot!);
        Assert.True(deterministic.IsClean); new AiAuditRequestBuilder().Build(processing, deterministic);
        var archivePlan = new ArchiveMasterPlanner(f.Context).Plan(package, processing); var job = f.Job();
        var coordinator = new AssetExecutionCoordinator(f.Context);
        var first = coordinator.Execute(job, package, processing, archivePlan, ProductionTestFixture.Pass, ProductionTestFixture.Budget);
        Assert.Equal(JobState.Completed, first.FinalState);
        Assert.Equal("portraits/norgard/treskal/farmer/male/portrait_treskal_farmer_male_040", first.Production.RelativeDirectory);
        Assert.Equal(5, first.Archive.FilesVerified.Count); Assert.Equal(5, first.Production.FilesVerified.Count);
        Assert.Contains(first.Archive.FilesVerified, p => p.FileName.EndsWith(".png"));
        Assert.DoesNotContain(first.Production.FilesVerified, p => p.FileName.EndsWith(".png"));
        foreach (var p in first.Archive.FilesVerified) Assert.Equal(File.ReadAllBytes(p.SourcePath), File.ReadAllBytes(p.DestinationPath));
        foreach (var p in first.Production.FilesVerified.Where(p => p.SourcePath is not null)) Assert.Equal(File.ReadAllBytes(p.SourcePath!), File.ReadAllBytes(p.DestinationPath));
        Assert.Equal(master, File.ReadAllBytes(f.Master)); Assert.Equal(master, File.ReadAllBytes(package.FilesByRole["master"]));
        var webp = first.Production.FilesVerified.Single(p => p.Kind == ProductionAssetFileKind.GeneratedWebp);
        using (var image = Image.Load<Rgba32>(webp.DestinationPath)) { Assert.Equal(768, image.Width); Assert.Equal(960, image.Height); }
        foreach (var path in first.Archive.FilesVerified.Select(p => p.DestinationPath).Concat(first.Production.FilesVerified.Select(p => p.DestinationPath)))
            File.SetLastWriteTimeUtc(path, new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var before = ArchiveTestFixture.Snapshot(f.Root);
        var second = coordinator.Execute(job, package, processing, archivePlan, ProductionTestFixture.Pass, ProductionTestFixture.Budget);
        Assert.Equal(ArchiveMasterOutcome.AlreadyArchived, second.Archive.Outcome); Assert.Equal(ProductionAssetOutcome.AlreadyProduced, second.Production.Outcome);
        ArchiveTestFixture.AssertSnapshot(before, f.Root); Assert.Equal(JobState.Completed, new JobStateStore(f.Context).Load(job).State);
    }

    [Theory]
    [InlineData("Production")] [InlineData("Job")]
    public void OccupiedMutexRejectsSecondThreadWithoutWaitingOrWriting(string scope)
    {
        using var f = new ProductionTestFixture(); var plan = f.Plan(); var job = f.Job();
        var root = scope == "Production" ? f.Context.Storage.ProductionRoot : f.Context.Storage.StateRoot;
        var identity = scope == "Job" ? job.Value : "";
        using var held = (IDisposable)ProductionTestFixture.Invoke(null, "ExecutionMutex", "Acquire", scope, root, identity)!;
        var before = ArchiveTestFixture.Snapshot(f.Root);
        Exception? failure = null;
        var worker = new Thread(() => failure = Record.Exception(() => {
            if (scope == "Production") f.Execute(plan);
            else new AssetExecutionCoordinator(f.Context).Execute(job, f.Package, f.Processing, f.ArchivePlan(), ProductionTestFixture.Pass, ProductionTestFixture.Budget);
        }));
        worker.Start(); Assert.True(worker.Join(TimeSpan.FromSeconds(10))); Assert.IsType<IOException>(failure);
        ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }

    [Theory]
    [InlineData("Production")] [InlineData("Job")]
    public async Task SystemNamedMutexCoordinatesIndependentProcess(string scope)
    {
        using var f = new ProductionTestFixture(); var plan = f.Plan(); var job = f.Job();
        var root = scope == "Production" ? f.Context.Storage.ProductionRoot : f.Context.Storage.StateRoot;
        var canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)); if (OperatingSystem.IsWindows()) canonical = canonical.ToUpperInvariant();
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(canonical + "\n" + (scope == "Job" ? job.Value : "")));
        var name = "NAP.Execution." + scope + "." + new Sha256Hasher().Compute(input).Hex;
        var script = "$m=[System.Threading.Mutex]::new($false,'" + name + "'); if (!$m.WaitOne(0)) {exit 2}; try {[Console]::WriteLine('LOCKED'); [Console]::Out.Flush(); [Console]::ReadLine() | Out-Null} finally {$m.ReleaseMutex(); $m.Dispose()}";
        var start = new ProcessStartInfo { FileName = OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh", UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-EncodedCommand"); start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        using var process = Process.Start(start)!;
        try
        {
            var ready = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)); Assert.Equal("LOCKED", ready);
            var before = ArchiveTestFixture.Snapshot(f.Root);
            Assert.Throws<IOException>(() => {
                if (scope == "Production") f.Execute(plan);
                else new AssetExecutionCoordinator(f.Context).Execute(job, f.Package, f.Processing, f.ArchivePlan(), ProductionTestFixture.Pass, ProductionTestFixture.Budget);
            });
            ArchiveTestFixture.AssertSnapshot(before, f.Root);
        }
        finally
        {
            process.StandardInput.WriteLine("release"); process.StandardInput.Flush();
            if (!process.WaitForExit(10000)) { process.Kill(); process.WaitForExit(); }
        }
        Assert.Equal(0, process.ExitCode);
        if (scope == "Production") Assert.Equal(ProductionAssetOutcome.Produced, f.Execute(plan).Outcome);
        else Assert.Equal(JobState.Completed, new AssetExecutionCoordinator(f.Context).Execute(job, f.Package, f.Processing, f.ArchivePlan(), ProductionTestFixture.Pass, ProductionTestFixture.Budget).FinalState);
    }

    [Fact]
    public void TwoUniversesWithSameAssetIdRemainPhysicallyIsolated()
    {
        using var first = new ProductionTestFixture(universe: "first_world"); using var second = new ProductionTestFixture(universe: "second_world", quality: 91);
        Assert.Equal(first.Package.AssetKey.AssetId, second.Package.AssetKey.AssetId); Assert.NotEqual(first.Package.AssetKey, second.Package.AssetKey);
        var other = ArchiveTestFixture.Snapshot(second.Root); var p1 = first.Plan(); first.Execute(p1); ArchiveTestFixture.AssertSnapshot(other, second.Root);
        var one = ArchiveTestFixture.Snapshot(first.Root); var p2 = second.Plan(); second.Execute(p2); ArchiveTestFixture.AssertSnapshot(one, first.Root);
        Assert.Throws<ArgumentException>(() => new ProductionAssetExecutor(second.Context).Execute(p1, ProductionTestFixture.Pass));
        Assert.Equal(ProductionAssetOutcome.AlreadyProduced, first.Execute(p1).Outcome); Assert.Equal(ProductionAssetOutcome.AlreadyProduced, second.Execute(p2).Outcome);
    }
}

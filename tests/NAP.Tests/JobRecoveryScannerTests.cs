using System.Globalization;
using System.Reflection;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class JobRecoveryScannerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("nap-recovery-").FullName;
    private readonly UniverseContext _context;
    private readonly JobStateStore _store;
    private readonly JobRecoveryScanner _scanner;
    private static JobId Id(int number) => new("job_" + number.ToString("x32", CultureInfo.InvariantCulture));
    private string Journal(JobId id) => Path.Combine(_context.Storage.StateRoot, id.Value + ".json");
    private static string TempName(JobId id, char suffix = 'a') => id.Value + "." + new string(suffix, 32) + ".tmp";

    public JobRecoveryScannerTests()
    {
        _context = Context("nimroel");
        _store = new JobStateStore(_context);
        _scanner = new JobRecoveryScanner(_context);
    }

    [Fact]
    public void ExactPublicApiNullValidationAndConstructorWithoutIo()
    {
        var type = typeof(JobRecoveryScanner);
        Assert.True(type.IsPublic);
        Assert.True(type.IsSealed);
        var parameter = Assert.Single(Assert.Single(type.GetConstructors()).GetParameters());
        Assert.Equal("context", parameter.Name);
        Assert.Equal(typeof(UniverseContext), parameter.ParameterType);
        Assert.Empty(type.GetProperties());
        var method = Assert.Single(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        Assert.Equal("Scan", method.Name);
        Assert.Equal(typeof(JobRecoverySnapshot), method.ReturnType);
        Assert.Empty(method.GetParameters());
        Assert.Equal("context", Assert.Throws<ArgumentNullException>(() => new JobRecoveryScanner(null!)).ParamName);
        Assert.Empty(Directory.GetFileSystemEntries(_root));
    }

    [Fact]
    public void MissingStateRootAndMissingWorkspaceAreCleanWithoutCreatingAnything()
    {
        AssertEmpty(_scanner.Scan());
        Assert.Empty(Directory.GetFileSystemEntries(_root));
        Directory.CreateDirectory(_context.Storage.WorkspaceRoot);
        var before = Tree();
        AssertEmpty(_scanner.Scan());
        AssertTree(before);
        Assert.False(Directory.Exists(_context.Storage.StateRoot));
    }

    [Fact]
    public void ExistingEmptyRootIsCleanAndReadOnly()
    {
        Directory.CreateDirectory(_context.Storage.StateRoot);
        var before = Tree();
        AssertEmpty(_scanner.Scan());
        AssertTree(before);
    }

    public static IEnumerable<object[]> States() => Enum.GetValues<JobState>().Select(state => new object[] { state });

    [Theory]
    [MemberData(nameof(States))]
    public void AllDurableStatesAreRecoveredExactlyAndTerminalsRemainSeparate(JobState state)
    {
        Persist(Id(1), state);
        var before = Tree();
        var result = _scanner.Scan();
        var groups = new[] { result.RecoverableJobs, result.CompletedJobs, result.FailedJobs };
        var category = state == JobState.Completed ? 1 : state == JobState.Failed ? 2 : 0;
        var record = Assert.Single(groups[category]);
        Assert.Equal(Id(1), record.JobId);
        Assert.Same(_context.Id, record.UniverseId);
        Assert.Equal(state, record.State);
        Assert.All(groups.Where((_, index) => index != category), group => Assert.Empty(group));
        Assert.Empty(result.OrphanTemps);
        Assert.True(result.Issues.IsClean);
        Assert.Equal(state, _store.Load(Id(1)).State);
        AssertTree(before);
    }

    [Fact]
    public void MultipleJobsAreClassifiedOrdinalIndependentlyOfCreationOrderAndRepeatedScans()
    {
        var rows = new[] { (9, JobState.Failed), (8, JobState.Completed), (7, JobState.Planned), (6, JobState.Failed),
            (5, JobState.Completed), (4, JobState.Verified), (3, JobState.Detected), (2, JobState.Completed), (1, JobState.Failed) };
        foreach (var (number, state) in rows) Persist(Id(number), state);
        var result = _scanner.Scan();
        Assert.Equal(new[] { Id(3), Id(4), Id(7) }, result.RecoverableJobs.Select(r => r.JobId));
        Assert.Equal(new[] { Id(2), Id(5), Id(8) }, result.CompletedJobs.Select(r => r.JobId));
        Assert.Equal(new[] { Id(1), Id(6), Id(9) }, result.FailedJobs.Select(r => r.JobId));
        Assert.True(result.Issues.IsClean);
        AssertSameSnapshot(result, _scanner.Scan());
        var otherContext = Context("other");
        var otherStore = new JobStateStore(otherContext);
        foreach (var (number, state) in rows.Reverse()) Persist(Id(number), state, otherStore);
        var other = new JobRecoveryScanner(otherContext).Scan();
        Assert.Equal(result.RecoverableJobs.Select(r => (r.JobId, r.State)), other.RecoverableJobs.Select(r => (r.JobId, r.State)));
        Assert.Equal(result.CompletedJobs.Select(r => (r.JobId, r.State)), other.CompletedJobs.Select(r => (r.JobId, r.State)));
        Assert.Equal(result.FailedJobs.Select(r => (r.JobId, r.State)), other.FailedJobs.Select(r => (r.JobId, r.State)));
        Assert.All(other.RecoverableJobs.Concat(other.CompletedJobs).Concat(other.FailedJobs), r => Assert.Same(otherContext.Id, r.UniverseId));
    }

    public static IEnumerable<object[]> CorruptJournals()
    {
        var valid = Json(Id(1), "PLANNED");
        foreach (var document in new[] { "{", "null", "[]", valid.Replace("\"PLANNED\"", "\"planned\""),
            valid.Replace("\"state\":\"PLANNED\"", "\"state\":\"UNKNOWN\""),
            valid.Replace("\"state\":\"PLANNED\"", "\"state\":null"),
            valid.Replace("\"state\":\"PLANNED\"", "\"extra\":true,\"state\":\"PLANNED\""),
            valid.Replace(",\"state\":\"PLANNED\"", ""), valid.Replace(Id(1).Value, Id(2).Value),
            valid.Replace("nimroel", "other"), valid.Replace("\"schema_version\":1", "\"schema_version\":2"),
            valid.Replace("\"schema_version\":1", "\"schema_version\":1.0"),
            valid.Replace("\"schema_version\":1", "\"schema_version\":\"1\""),
            valid.Replace("\"schema_version\":1", "\"schema_version\":1,\"schema_version\":1") })
            yield return new object[] { document };
    }

    [Theory]
    [MemberData(nameof(CorruptJournals))]
    public void KnownInvalidJournalsStopButContinueDiagnosisWithoutRepair(string document)
    {
        Persist(Id(1), JobState.Planned);
        File.WriteAllText(Journal(Id(1)), document);
        Persist(Id(2), JobState.Validated);
        Write("job_BAD.json", "not a journal");
        var before = Tree();
        var result = _scanner.Scan();
        Assert.Equal(Id(2), Assert.Single(result.RecoverableJobs).JobId);
        Assert.Empty(result.CompletedJobs);
        Assert.Empty(result.FailedJobs);
        Assert.Equal(new[] { NapIssueCodes.JobRecoveryInvalidJournal, NapIssueCodes.JobRecoveryInvalidJournalName }, result.Issues.Issues.Select(i => i.Code));
        AssertIssue(result.Issues.Issues[0], NapIssueCodes.JobRecoveryInvalidJournal, Id(1).Value + ".json");
        Assert.True(result.Issues.ShouldStop);
        AssertTree(before);
    }

    [Theory]
    [InlineData("job_BAD.json")]
    [InlineData("job_123.json")]
    [InlineData("job_00000000000000000000000000000000.json")]
    [InlineData("job_00112233445566778899AABBCCDDEEFF.json")]
    public void SuspiciousJournalNamesStopWithoutOpeningEvenLockedFiles(string name)
    {
        var path = Write(name, "would be invalid JSON if opened");
        using var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var result = _scanner.Scan();
        Assert.Empty(result.RecoverableJobs);
        AssertIssue(Assert.Single(result.Issues.Issues), NapIssueCodes.JobRecoveryInvalidJournalName, name);
    }

    [Theory]
    [InlineData("job_BAD.tmp")]
    [InlineData("job_00000000000000000000000000000001.orphan.tmp")]
    [InlineData("job_00000000000000000000000000000001.01234567-89ab-cdef-0123-456789abcdef.tmp")]
    [InlineData("job_00000000000000000000000000000001.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.tmp")]
    public void SuspiciousTempsStopWithoutOpeningOrDeletingEvenLockedFiles(string name)
    {
        var path = Write(name, "do not open");
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var result = _scanner.Scan();
            Assert.Empty(result.OrphanTemps);
            AssertIssue(Assert.Single(result.Issues.Issues), NapIssueCodes.JobRecoveryInvalidTempName, name);
        }
        Assert.Equal("do not open", File.ReadAllText(path));
    }

    [Fact]
    public void FinalPlannedJournalRemainsAuthorityBesideMultipleLockedAuditedTemps()
    {
        Persist(Id(1), JobState.Planned);
        var names = new[] { TempName(Id(1), 'f'), TempName(Id(1), 'a') };
        foreach (var name in names) Write(name, Json(Id(1), "AUDITED"));
        var before = Tree();
        using (var first = new FileStream(Path.Combine(_context.Storage.StateRoot, names[0]), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        using (var second = new FileStream(Path.Combine(_context.Storage.StateRoot, names[1]), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var result = _scanner.Scan();
            Assert.Equal(JobState.Planned, Assert.Single(result.RecoverableJobs).State);
            Assert.Equal(names.OrderBy(n => n, StringComparer.Ordinal), result.OrphanTemps.Select(t => t.FileName));
            Assert.All(result.OrphanTemps, t => Assert.True(t.HasFinalJournal));
            Assert.Equal(2, result.Issues.Issues.Count);
            foreach (var issue in result.Issues.Issues) AssertIssue(issue, NapIssueCodes.JobRecoveryOrphanTemp, issue.SubjectPath!, true);
            Assert.False(result.Issues.ShouldStop);
            Assert.True(result.Issues.CanContinue);
        }
        AssertTree(before);
    }

    [Fact]
    public void TempWithoutFinalIsNeverLoadedPromotedOrRecovered()
    {
        var name = TempName(Id(1));
        var path = Write(name, Json(Id(1), "VERIFIED"));
        var before = Tree();
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var result = _scanner.Scan();
            Assert.Empty(result.RecoverableJobs);
            Assert.Empty(result.CompletedJobs);
            Assert.Empty(result.FailedJobs);
            Assert.False(Assert.Single(result.OrphanTemps).HasFinalJournal);
            AssertIssue(Assert.Single(result.Issues.Issues), NapIssueCodes.JobRecoveryTempWithoutJournal, name);
        }
        AssertTree(before);
        Assert.False(File.Exists(Journal(Id(1))));
    }

    [Fact]
    public void HasFinalMeansEnumeratedCanonicalFileEvenWhenItsContentsAreInvalid()
    {
        Write(Id(1).Value + ".json", "corrupt");
        Write(TempName(Id(1)), "validity never checked");
        var result = _scanner.Scan();
        Assert.True(Assert.Single(result.OrphanTemps).HasFinalJournal);
        Assert.Empty(result.RecoverableJobs);
        Assert.Equal(new[] { NapIssueCodes.JobRecoveryOrphanTemp, NapIssueCodes.JobRecoveryInvalidJournal }, result.Issues.Issues.Select(i => i.Code));
        Assert.True(result.Issues.ShouldStop);
    }

    [Fact]
    public void ForeignLockedFilesAreIgnoredAndSubdirectoriesAreNeitherInterpretedNorTraversed()
    {
        var paths = new[] { "AssetCatalog.db", "notes.txt", "future.dat", "JOB_upper.json", "job_extension.JSON", "job_other.bak" }
            .Select(name => Write(name, "foreign")).ToArray();
        var directory = Path.Combine(_context.Storage.StateRoot, Id(1).Value + ".json");
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(Path.Combine(_context.Storage.StateRoot, TempName(Id(2))));
        File.WriteAllText(Path.Combine(directory, "job_BAD.json"), "ignore nested");
        Write(TempName(Id(1)), "a directory is not a final journal");
        var before = Tree();
        var locks = new List<FileStream>();
        try
        {
            foreach (var path in paths) locks.Add(new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None));
            var result = _scanner.Scan();
            Assert.Empty(result.RecoverableJobs);
            Assert.False(Assert.Single(result.OrphanTemps).HasFinalJournal);
            AssertIssue(Assert.Single(result.Issues.Issues), NapIssueCodes.JobRecoveryTempWithoutJournal, TempName(Id(1)));
        }
        finally { foreach (var locked in locks) locked.Dispose(); }
        AssertTree(before);
        File.Delete(Path.Combine(_context.Storage.StateRoot, TempName(Id(1))));
        AssertEmpty(_scanner.Scan());
    }

    [Fact]
    public void ScanPreservesEveryRootIncludingBytesLastWriteTimesAndDirectoryInventory()
    {
        foreach (var path in new[] { _context.Storage.InboxRoot, _context.Storage.StagingRoot, _context.Storage.CacheRoot,
            _context.Storage.ProductionRoot, _context.Storage.ArchiveRoot })
        {
            Directory.CreateDirectory(path);
            File.WriteAllBytes(Path.Combine(path, "sentinel.bin"), [0, 255, 1, 100]);
        }
        Persist(Id(1), JobState.Planned);
        Write(TempName(Id(1)), "orphan");
        Write("job_BAD.json", "corrupt sentinel");
        var before = Tree();
        _scanner.Scan();
        _scanner.Scan();
        AssertTree(before);
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("ar-SA")]
    public void ParsingClassificationAndIssueOrderingAreInvariantAcrossCultures(string name)
    {
        Persist(Id(15), JobState.Completed);
        Persist(Id(10), JobState.Planned);
        Persist(Id(2), JobState.Failed);
        Write(TempName(Id(10), 'f'), "orphan");
        Write(TempName(Id(1), 'b'), "no final");
        Write("job_BAD.json", "bad name");
        Write("job_bad.tmp", "bad name");
        var baseline = _scanner.Scan();
        Assert.Equal(baseline.Issues.Issues.Select(i => i.SubjectPath).OrderBy(n => n, StringComparer.Ordinal), baseline.Issues.Issues.Select(i => i.SubjectPath));
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
            AssertSameSnapshot(baseline, _scanner.Scan());
        }
        finally { CultureInfo.CurrentCulture = previous; CultureInfo.CurrentUICulture = previousUi; }
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public void PortableReparsePolicyStopsForFilesAndDirectoriesWithoutPublicHooks(bool root, bool directory)
    {
        var method = typeof(JobRecoveryScanner).GetMethod("ReparseIssue", BindingFlags.NonPublic | BindingFlags.Static)!;
        var attributes = FileAttributes.ReparsePoint | (directory ? FileAttributes.Directory : FileAttributes.Normal);
        var name = root ? null : TempName(Id(1));
        var issue = Assert.IsType<NapIssue>(method.Invoke(null, [attributes, name]));
        AssertIssue(issue, root ? NapIssueCodes.JobRecoveryStateRootReparse : NapIssueCodes.JobRecoveryEntryReparse, name);
        Assert.Null(method.Invoke(null, [directory ? FileAttributes.Directory : FileAttributes.Normal, name]));
    }

    [Fact]
    public void RealRootSymlinkStopsWhenPlatformAllowsCreation()
    {
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "job_BAD.json"), "never enumerate");
        Directory.CreateDirectory(_context.Storage.WorkspaceRoot);
        if (!TryCreateLink(_context.Storage.StateRoot, target, true)) return; // Portable policy is always tested above.
        try
        {
            var result = _scanner.Scan();
            Assert.Empty(result.RecoverableJobs);
            Assert.Empty(result.OrphanTemps);
            AssertIssue(Assert.Single(result.Issues.Issues), NapIssueCodes.JobRecoveryStateRootReparse, null);
            Assert.Equal("never enumerate", File.ReadAllText(Path.Combine(target, "job_BAD.json")));
        }
        finally { Directory.Delete(_context.Storage.StateRoot); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RealNamespaceSymlinkIsNotOpenedOrFollowedWhenPlatformAllowsCreation(bool directory)
    {
        Directory.CreateDirectory(_context.Storage.StateRoot);
        var target = Path.Combine(_root, "target");
        if (directory) { Directory.CreateDirectory(target); File.WriteAllText(Path.Combine(target, "job_BAD.json"), "target"); }
        else File.WriteAllText(target, Json(Id(1), "PLANNED"));
        var name = directory ? TempName(Id(1)) : Id(1).Value + ".json";
        var link = Path.Combine(_context.Storage.StateRoot, name);
        if (!TryCreateLink(link, target, directory)) return;
        try
        {
            var result = _scanner.Scan();
            Assert.Empty(result.RecoverableJobs);
            Assert.Empty(result.OrphanTemps);
            AssertIssue(Assert.Single(result.Issues.Issues), NapIssueCodes.JobRecoveryEntryReparse, name);
        }
        finally { if (directory) Directory.Delete(link); else File.Delete(link); }
    }

    [Fact]
    public void DisappearanceMappingUsesKnownFilenameWithoutTimingRaceRetryOrPublicHook()
    {
        Persist(Id(1), JobState.Planned);
        var name = Path.GetFileName(Journal(Id(1)));
        File.Delete(Journal(Id(1)));
        var issues = new List<NapIssue>();
        var method = typeof(JobRecoveryScanner).GetMethod("LoadJournal", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.Null(method.Invoke(_scanner, [Id(1), name, issues]));
        AssertIssue(Assert.Single(issues), NapIssueCodes.JobRecoveryJournalChanged, name);
        Assert.Empty(Directory.GetFileSystemEntries(_context.Storage.StateRoot));
    }

    [Fact]
    public void UnexpectedOperationalErrorsPropagateRatherThanReturningCleanSnapshot()
    {
        Directory.CreateDirectory(_context.Storage.WorkspaceRoot);
        File.WriteAllText(_context.Storage.StateRoot, "a file is not a state directory");
        Assert.ThrowsAny<IOException>(() => _scanner.Scan());
        Assert.Equal("a file is not a state directory", File.ReadAllText(_context.Storage.StateRoot));
    }

    [Fact]
    public void LockedCanonicalJournalOperationalErrorIsNotTranslatedToDomainIssue()
    {
        Persist(Id(1), JobState.Planned);
        using var locked = new FileStream(Journal(Id(1)), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.ThrowsAny<IOException>(() => _scanner.Scan());
    }

    private static bool TryCreateLink(string link, string target, bool directory)
    {
        try
        {
            if (directory) Directory.CreateSymbolicLink(link, target);
            else File.CreateSymbolicLink(link, target);
            return true;
        }
        catch (UnauthorizedAccessException) { return false; }
        catch (PlatformNotSupportedException) { return false; }
        catch (IOException exception) when (OperatingSystem.IsWindows() && (exception.HResult & 0xffff) == 1314) { return false; }
    }

    private UniverseContext Context(string universe)
    {
        var id = new UniverseId(universe);
        return new UniverseContext(new UniverseProfile(id, universe), new UniverseStorageConfig(id,
            Path.Combine(_root, universe, "workspace"), Path.Combine(_root, universe, "production"), Path.Combine(_root, universe, "archive")));
    }

    private void Persist(JobId id, JobState state, JobStateStore? store = null)
    {
        store ??= _store;
        store.Create(id);
        if (state == JobState.Failed) store.Transition(id, state);
        else for (var next = JobState.Staged; next <= state; next++) store.Transition(id, next);
    }

    private static string Json(JobId id, string state) =>
        $"{{\"schema_version\":1,\"job_id\":\"{id.Value}\",\"universe_id\":\"nimroel\",\"state\":\"{state}\"}}\n";

    private string Write(string name, string content)
    {
        Directory.CreateDirectory(_context.Storage.StateRoot);
        var path = Path.Combine(_context.Storage.StateRoot, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static void AssertEmpty(JobRecoverySnapshot result)
    {
        Assert.Empty(result.RecoverableJobs); Assert.Empty(result.CompletedJobs); Assert.Empty(result.FailedJobs);
        Assert.Empty(result.OrphanTemps); Assert.True(result.Issues.IsClean);
    }

    private static void AssertIssue(NapIssue issue, string code, string? name, bool warning = false)
    {
        Assert.Equal(code, issue.Code);
        Assert.Equal(warning ? NapIssueSeverity.Warning : NapIssueSeverity.Error, issue.Severity);
        Assert.Equal(warning ? NapIssueDisposition.Continue : NapIssueDisposition.Stop, issue.Disposition);
        Assert.Equal(name, issue.SubjectPath);
        Assert.Null(issue.Detail);
        if (name is not null) Assert.Equal(Path.GetFileName(name), name);
        Assert.DoesNotContain(":\\", issue.Message);
    }

    private static void AssertSameSnapshot(JobRecoverySnapshot first, JobRecoverySnapshot second)
    {
        Assert.Equal(first.RecoverableJobs, second.RecoverableJobs);
        Assert.Equal(first.CompletedJobs, second.CompletedJobs);
        Assert.Equal(first.FailedJobs, second.FailedJobs);
        Assert.Equal(first.OrphanTemps, second.OrphanTemps);
        Assert.Equal(first.Issues.Issues, second.Issues.Issues);
    }

    private Dictionary<string, (byte[]? Bytes, DateTime Write)> Tree() => Directory.GetFileSystemEntries(_root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => path, path => (Directory.Exists(path) ? null : File.ReadAllBytes(path), File.GetLastWriteTimeUtc(path)), StringComparer.Ordinal);

    private void AssertTree(Dictionary<string, (byte[]? Bytes, DateTime Write)> before)
    {
        var after = Tree();
        Assert.Equal(before.Keys.OrderBy(p => p, StringComparer.Ordinal), after.Keys.OrderBy(p => p, StringComparer.Ordinal));
        foreach (var (path, entry) in before)
        {
            Assert.Equal(entry.Bytes, after[path].Bytes);
            Assert.Equal(entry.Write, after[path].Write);
        }
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}

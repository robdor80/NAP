using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class JobStateStoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("nap-job-states-").FullName;
    private readonly UniverseContext _context;
    private readonly JobStateStore _store;
    private static readonly JobId Id = new("job_00112233445566778899aabbccddeeff");
    private string Journal => Path.Combine(_context.Storage.StateRoot, Id.Value + ".json");

    public JobStateStoreTests()
    {
        _context = Context("nimroel");
        _store = new JobStateStore(_context);
    }

    [Fact]
    public void PublicContractHasOnlyContextConstructorCreateLoadAndTransitionAndConstructorDoesNoIo()
    {
        var type = typeof(JobStateStore);
        Assert.True(type.IsPublic);
        Assert.True(type.IsSealed);
        var parameter = Assert.Single(Assert.Single(type.GetConstructors()).GetParameters());
        Assert.Equal("context", parameter.Name);
        Assert.Equal(typeof(UniverseContext), parameter.ParameterType);
        Assert.Empty(type.GetProperties());
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .OrderBy(method => method.Name, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "Create", "Load", "Transition" }, methods.Select(m => m.Name));
        Assert.All(methods, method => Assert.Equal(typeof(JobStateRecord), method.ReturnType));
        Assert.Equal(new[] { typeof(JobId) }, methods[0].GetParameters().Select(p => p.ParameterType));
        Assert.Equal(new[] { typeof(JobId) }, methods[1].GetParameters().Select(p => p.ParameterType));
        Assert.Equal(new[] { typeof(JobId), typeof(JobState) }, methods[2].GetParameters().Select(p => p.ParameterType));
        Assert.Equal(new[] { "jobId", "nextState" }, methods[2].GetParameters().Select(p => p.Name));
        Assert.Equal("context", Assert.Throws<ArgumentNullException>(() => new JobStateStore(null!)).ParamName);
        Assert.Empty(Directory.GetFileSystemEntries(_root));
    }

    [Fact]
    public void InvalidArgumentsAreRejectedBeforeIoAndLoadDoesNotCreateMissingStateRoot()
    {
        Assert.Equal("jobId", Assert.Throws<ArgumentNullException>(() => _store.Create(null!)).ParamName);
        Assert.Equal("jobId", Assert.Throws<ArgumentNullException>(() => _store.Load(null!)).ParamName);
        Assert.Equal("jobId", Assert.Throws<ArgumentNullException>(() => _store.Transition(null!, (JobState)(-1))).ParamName);
        Assert.Equal("nextState", Assert.Throws<ArgumentOutOfRangeException>(() => _store.Transition(Id, (JobState)9)).ParamName);
        Assert.Equal(Journal, Assert.Throws<FileNotFoundException>(() => _store.Load(Id)).FileName);
        Assert.Throws<FileNotFoundException>(() => _store.Transition(Id, JobState.Staged));
        Assert.Empty(Directory.GetFileSystemEntries(_root));
        Directory.CreateDirectory(_context.Storage.StateRoot);
        Assert.Throws<FileNotFoundException>(() => _store.Load(Id));
        Assert.Empty(Directory.GetFiles(_context.Storage.StateRoot));
    }

    [Fact]
    public void CreatePublishesOnlyDetectedJournalAndNeverCreatesOtherStorageRoots()
    {
        var record = _store.Create(Id);
        Assert.Same(Id, record.JobId);
        Assert.Same(_context.Id, record.UniverseId);
        Assert.Equal(JobState.Detected, record.State);
        Assert.Equal(record, _store.Load(Id));
        Assert.Equal(new[] { Journal }, Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
        Assert.All(OtherRoots(), path => Assert.False(Directory.Exists(path)));
        Assert.Equal(Encoding.UTF8.GetBytes(Json("DETECTED")), File.ReadAllBytes(Journal));
    }

    [Fact]
    public void ExistingJournalIsNeverOverwrittenEvenIfCorruptAndExactCreateMessageIsUsed()
    {
        _store.Create(Id);
        var before = File.ReadAllBytes(Journal);
        Assert.Equal($"A persisted state already exists for Job ID '{Id.Value}'.",
            Assert.Throws<InvalidOperationException>(() => _store.Create(Id)).Message);
        Assert.Equal(before, File.ReadAllBytes(Journal));
        File.WriteAllText(Journal, "corrupt sentinel");
        Assert.Throws<InvalidOperationException>(() => _store.Create(Id));
        Assert.Equal("corrupt sentinel", File.ReadAllText(Journal));
        Assert.Equal(new[] { Journal }, Directory.GetFiles(_context.Storage.StateRoot));
    }

    public static IEnumerable<object[]> States() => JobStateMachineTests.Vocabulary.Select(row => new object[] { row.State, row.Token });

    [Theory]
    [MemberData(nameof(States))]
    public void LoadAcceptsAllExactTokensReadOnlyAndFreshStoresRoundTrip(JobState state, string token)
    {
        Write(Json(token));
        var before = Snapshot();
        var loaded = new JobStateStore(_context).Load(Id);
        Assert.Equal(new JobStateRecord(Id, _context.Id, state), loaded);
        AssertSnapshot(before);
        using var document = JsonDocument.Parse(File.ReadAllBytes(Journal));
        Assert.Equal(new[] { "schema_version", "job_id", "universe_id", "state" }, document.RootElement.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public void LoadAcceptsPropertyOrderAndWhitespaceWithoutRewritingTheSource()
    {
        Write($"{{ \"state\": \"DETECTED\", \"universe_id\": \"nimroel\", \"job_id\": \"{Id.Value}\", \"schema_version\": 1 }}");
        var before = Snapshot();
        Assert.Equal(JobState.Detected, _store.Load(Id).State);
        AssertSnapshot(before);
    }

    public static IEnumerable<object[]> CorruptDocuments()
    {
        var valid = Json("DETECTED").TrimEnd();
        var properties = new Dictionary<string, string>
        {
            ["schema_version"] = "1", ["job_id"] = $"\"{Id.Value}\"", ["universe_id"] = "\"nimroel\"", ["state"] = "\"DETECTED\""
        };
        foreach (var root in new[] { "", "{", "null", "[]", "true", "1", "\"text\"", valid + "{}", valid[..^1] + ",}", "// comment\n" + valid })
            yield return [root];
        foreach (var name in properties.Keys)
        {
            yield return ["{" + string.Join(",", properties.Where(p => p.Key != name).Select(p => $"\"{p.Key}\":{p.Value}")) + "}"];
            yield return [valid[..^1] + $",\"{name}\":{properties[name]}" + "}"];
            foreach (var wrongType in new[] { "null", "false", "[]", "{}" })
                yield return [valid.Replace($"\"{name}\":{properties[name]}", $"\"{name}\":{wrongType}", StringComparison.Ordinal)];
        }
        foreach (var version in new[] { "0", "2", "-1", "1.0", "1e0", "1.000000000000000000001", "2147483648", "\"1\"" })
            yield return [valid.Replace("\"schema_version\":1", "\"schema_version\":" + version, StringComparison.Ordinal)];
        foreach (var state in new[] { "detected", "Detected", "UNKNOWN", "STABLE", "CONVERTED", "READY", "FAILED ", " verified", "VERİFİED", "" })
            yield return [valid.Replace("DETECTED", state, StringComparison.Ordinal)];
        yield return [valid.Replace("\"state\":\"DETECTED\"", "\"state\":0", StringComparison.Ordinal)];
        foreach (var job in new[] { "job_00000000000000000000000000000000", "JOB_00112233445566778899aabbccddeeff", "../outside", "job_00000000000000000000000000000001" })
            yield return [valid.Replace(Id.Value, job, StringComparison.Ordinal)];
        yield return [valid.Replace($"\"job_id\":\"{Id.Value}\"", "\"job_id\":123", StringComparison.Ordinal)];
        foreach (var universe in new[] { "test_universe", "Nimroel", "nimroel ", "../outside", "", "nımroel" })
            yield return [valid.Replace("nimroel", universe, StringComparison.Ordinal)];
        yield return [valid.Replace("\"universe_id\":\"nimroel\"", "\"universe_id\":1", StringComparison.Ordinal)];
        yield return [valid[..^1] + ",\"extra\":true}"];
        yield return [valid.Replace("schema_version", "Schema_version", StringComparison.Ordinal)];
        yield return [valid[..^1] + ",\"st\\u0061te\":\"DETECTED\"}"];
        yield return [valid.Replace("\"state\":\"DETECTED\"", "\"state\":\"\\ud800\"", StringComparison.Ordinal)];
        yield return [valid.Replace("schema_version", "\\ud800", StringComparison.Ordinal)];
    }

    [Theory]
    [MemberData(nameof(CorruptDocuments))]
    public void CorruptOrIncoherentJournalsStopWithoutRepairOrTransitionWrites(string json)
    {
        Write(json);
        var before = Snapshot();
        Assert.Throws<InvalidDataException>(() => _store.Load(Id));
        Assert.Throws<InvalidDataException>(() => _store.Transition(Id, JobState.Staged));
        AssertSnapshot(before);
    }

    [Fact]
    public void InvalidUtf8IsRejectedWithoutRewriting()
    {
        Directory.CreateDirectory(_context.Storage.StateRoot);
        File.WriteAllBytes(Journal, [0xff, 0xfe, 0x7b, 0x7d]);
        var before = Snapshot();
        Assert.Throws<InvalidDataException>(() => _store.Load(Id));
        AssertSnapshot(before);
    }

    [Theory]
    [InlineData("nimroel")]
    [InlineData("schema_version")]
    [InlineData("job_id")]
    [InlineData("DETECTED")]
    [InlineData("job_00112233445566778899aabbccddeeff")]
    public void InvalidUtf8InsideAJsonStringIsRejectedAsInvalidData(string fragment)
    {
        var bytes = Encoding.UTF8.GetBytes(Json("DETECTED"));
        var valueIndex = Json("DETECTED").IndexOf(fragment, StringComparison.Ordinal);
        bytes[valueIndex] = 0xff;
        Directory.CreateDirectory(_context.Storage.StateRoot);
        File.WriteAllBytes(Journal, bytes);
        var before = Snapshot();
        Assert.Throws<InvalidDataException>(() => _store.Load(Id));
        Assert.Throws<InvalidDataException>(() => _store.Transition(Id, JobState.Staged));
        AssertSnapshot(before);
    }

    [Fact]
    public void CompleteNormalFlowPersistsCanonicalBytesAndNewStoresLoadEachDurableState()
    {
        _store.Create(Id);
        foreach (var (state, token) in JobStateMachineTests.Vocabulary.Skip(1).Take(7))
        {
            var result = _store.Transition(Id, state);
            Assert.Equal(state, result.State);
            Assert.Equal(Id, result.JobId);
            Assert.Equal(_context.Id, result.UniverseId);
            Assert.Equal(result, new JobStateStore(_context).Load(Id));
            Assert.Equal(Encoding.UTF8.GetBytes(Json(token)), File.ReadAllBytes(Journal));
            Assert.Equal(new[] { Journal }, Directory.GetFiles(_context.Storage.StateRoot));
        }
    }

    public static IEnumerable<object[]> Matrix() => JobStateMachineTests.Matrix();

    [Theory]
    [MemberData(nameof(Matrix))]
    public void All81StoreTransitionsPersistOnlyValidEdgesAndNeverWriteOnInvalidEdges(JobState from, JobState to, bool allowed, string fromToken, string toToken)
    {
        Write(Json(fromToken));
        var before = Snapshot();
        if (allowed)
        {
            Assert.Equal(to, _store.Transition(Id, to).State);
            Assert.Equal(to, _store.Load(Id).State);
            Assert.Equal(Encoding.UTF8.GetBytes(Json(toToken)), File.ReadAllBytes(Journal));
            Assert.Equal(new[] { Journal }, Directory.GetFiles(_context.Storage.StateRoot));
        }
        else
        {
            Assert.Equal($"Invalid Job state transition from '{fromToken}' to '{toToken}'.",
                Assert.Throws<InvalidOperationException>(() => _store.Transition(Id, to)).Message);
            AssertSnapshot(before);
            Assert.Equal(from, _store.Load(Id).State);
        }
    }

    [Fact]
    public void InvalidNextStateDoesNotTouchAValidJournal()
    {
        _store.Create(Id);
        var before = Snapshot();
        Assert.Equal("nextState", Assert.Throws<ArgumentOutOfRangeException>(() => _store.Transition(Id, (JobState)(-1))).ParamName);
        AssertSnapshot(before);
    }

    [Fact]
    public void IndependentUniverseStoresNeverSearchOtherRootsAndRejectForeignJournalContent()
    {
        var secondContext = Context("test_universe");
        var secondStore = new JobStateStore(secondContext);
        _store.Create(Id);
        Assert.Throws<FileNotFoundException>(() => secondStore.Load(Id));
        Assert.False(Directory.Exists(secondContext.Storage.StateRoot));
        secondStore.Create(Id); // Deliberate controlled reuse to test physical universe isolation.
        _store.Transition(Id, JobState.Staged);
        Assert.Equal(JobState.Staged, _store.Load(Id).State);
        Assert.Equal(JobState.Detected, secondStore.Load(Id).State);
        Assert.Equal(_context.Id, _store.Load(Id).UniverseId);
        Assert.Equal(secondContext.Id, secondStore.Load(Id).UniverseId);
        var secondJournal = Path.Combine(secondContext.Storage.StateRoot, Id.Value + ".json");
        File.Copy(Journal, secondJournal, overwrite: true);
        var before = Snapshot();
        Assert.Throws<InvalidDataException>(() => secondStore.Load(Id));
        Assert.Throws<InvalidDataException>(() => secondStore.Transition(Id, JobState.Staged));
        AssertSnapshot(before);
    }

    [Fact]
    public void PublicationFailureLeavesPreviousFinalAndFullyWrittenSiblingTempIntact()
    {
        _store.Create(Id);
        var before = File.ReadAllBytes(Journal);
        var next = new JobStateRecord(Id, _context.Id, JobState.Staged);
        // Force a publication conflict after writing/flushing/closing temp, without public test hooks.
        var publish = typeof(JobStateStore).GetMethod("Publish", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var failure = Assert.Throws<TargetInvocationException>(() => publish.Invoke(_store, [next, Journal, false]));
        Assert.IsAssignableFrom<IOException>(failure.InnerException);
        Assert.Equal(before, File.ReadAllBytes(Journal));
        Assert.Equal(JobState.Detected, _store.Load(Id).State);
        var temp = Assert.Single(Directory.GetFiles(_context.Storage.StateRoot, "*.tmp"));
        Assert.Equal(Encoding.UTF8.GetBytes(Json("STAGED")), File.ReadAllBytes(temp));
        using var exclusive = new FileStream(temp, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Equal(2, Directory.GetFiles(_context.Storage.StateRoot).Length);
    }

    [Fact]
    public void ReplacementPreservesPreviousFinalWhenBlockedAndDoesNotRewriteExistingReaderBytes()
    {
        _store.Create(Id);
        var before = File.ReadAllBytes(Journal);
        using var reader = new FileStream(Journal, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (OperatingSystem.IsWindows())
        {
            // Windows denies rename/replacement while this handle excludes FileShare.Delete.
            var failure = Record.Exception(() => _store.Transition(Id, JobState.Staged));
            Assert.True(failure is IOException or UnauthorizedAccessException, failure?.ToString());
            Assert.Equal(before, File.ReadAllBytes(Journal));
            Assert.Equal(JobState.Detected, _store.Load(Id).State);
            var temp = Assert.Single(Directory.GetFiles(_context.Storage.StateRoot, "*.tmp"));
            Assert.Equal(Encoding.UTF8.GetBytes(Json("STAGED")), File.ReadAllBytes(temp));
        }
        else
        {
            // On filesystems permitting replacement of open files, the old handle still reads the old snapshot.
            Assert.Equal(JobState.Staged, _store.Transition(Id, JobState.Staged).State);
            Assert.Equal(Encoding.UTF8.GetBytes(Json("STAGED")), File.ReadAllBytes(Journal));
        }
        var observed = new byte[before.Length];
        reader.ReadExactly(observed);
        Assert.Equal(before, observed);
    }

    [Fact]
    public void ExistingOrphanTempIsNotScannedLoadedOrRemoved()
    {
        Directory.CreateDirectory(_context.Storage.StateRoot);
        var orphan = Path.Combine(_context.Storage.StateRoot, Id.Value + ".orphan.tmp");
        File.WriteAllText(orphan, "incomplete prior operation");
        Assert.Throws<FileNotFoundException>(() => _store.Load(Id));
        _store.Create(Id);
        _store.Transition(Id, JobState.Staged);
        Assert.Equal("incomplete prior operation", File.ReadAllText(orphan));
        Assert.Equal(JobState.Staged, _store.Load(Id).State);
        Assert.Equal(2, Directory.GetFiles(_context.Storage.StateRoot).Length);
    }

    [Fact]
    public void JournalOperationsPreserveAllOtherStorageRootsAndSentinels()
    {
        foreach (var path in OtherRoots())
        {
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "sentinel.txt"), path);
        }
        var before = Snapshot();
        _store.Create(Id);
        _store.Transition(Id, JobState.Staged);
        _store.Load(Id);
        Assert.Throws<InvalidOperationException>(() => _store.Transition(Id, JobState.Completed));
        Assert.Throws<InvalidOperationException>(() => _store.Create(Id));
        foreach (var (path, bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal(before.Keys.Append(Journal).OrderBy(path => path, StringComparer.Ordinal), Snapshot().Keys.OrderBy(path => path, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("ar-SA")]
    public void PersistedTokensBytesPathsAndTransitionsAreCultureIndependent(string cultureName)
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            CompleteNormalFlowPersistsCanonicalBytesAndNewStoresLoadEachDurableState();
            Assert.Equal(Id.Value + ".json", Path.GetFileName(Journal));
            Assert.Equal("Invalid Job state transition from 'COMPLETED' to 'FAILED'.",
                Assert.Throws<InvalidOperationException>(() => _store.Transition(Id, JobState.Failed)).Message);
            Write(Json("VERIFIED"));
            _store.Transition(Id, JobState.Failed);
            Assert.Equal(Encoding.UTF8.GetBytes(Json("FAILED")), File.ReadAllBytes(Journal));
        }
        finally { CultureInfo.CurrentCulture = previous; CultureInfo.CurrentUICulture = previousUi; }
    }

    private UniverseContext Context(string universe)
    {
        var id = new UniverseId(universe);
        return new UniverseContext(new UniverseProfile(id, universe), new UniverseStorageConfig(id,
            Path.Combine(_root, universe, "workspace"), Path.Combine(_root, universe, "production"), Path.Combine(_root, universe, "archive")));
    }

    private IEnumerable<string> OtherRoots() =>
        [_context.Storage.InboxRoot, _context.Storage.StagingRoot, _context.Storage.CacheRoot,
            _context.Storage.ProductionRoot, _context.Storage.ArchiveRoot];

    private static string Json(string token) =>
        $"{{\"schema_version\":1,\"job_id\":\"{Id.Value}\",\"universe_id\":\"nimroel\",\"state\":\"{token}\"}}\n";

    private void Write(string json)
    {
        Directory.CreateDirectory(_context.Storage.StateRoot);
        File.WriteAllText(Journal, json, new UTF8Encoding(false));
    }

    private Dictionary<string, byte[]> Snapshot() => Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);

    private void AssertSnapshot(Dictionary<string, byte[]> before)
    {
        var after = Snapshot();
        Assert.Equal(before.Keys.OrderBy(path => path, StringComparer.Ordinal), after.Keys.OrderBy(path => path, StringComparer.Ordinal));
        foreach (var (path, bytes) in before) Assert.Equal(bytes, after[path]);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}

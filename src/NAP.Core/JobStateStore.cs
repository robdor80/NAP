using System.Text.Json;

namespace NAP.Core;

/// <summary>Last-state v1 journals scoped exclusively to the explicit context's StateRoot.</summary>
public sealed class JobStateStore
{
    private readonly UniverseContext _context;
    private readonly JobStateMachine _machine = new();
    private readonly object _writeLock = new();

    public JobStateStore(UniverseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public JobStateRecord Create(JobId jobId)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        lock (_writeLock)
        {
            var path = JournalPath(jobId);
            if (File.Exists(path))
                throw AlreadyExists(jobId);
            var record = _machine.Create(jobId, _context.Id);
            try { Publish(record, path, overwrite: false); }
            catch (IOException) when (File.Exists(path)) { throw AlreadyExists(jobId); }
            return record;
        }
    }

    public JobStateRecord Load(JobId jobId)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        var path = JournalPath(jobId);
        using var stream = OpenJournal(path);
        try
        {
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("The persisted Job state must be an object.");
            var expected = new HashSet<string>(StringComparer.Ordinal)
                { "schema_version", "job_id", "universe_id", "state" };
            foreach (var property in root.EnumerateObject())
                if (!expected.Remove(property.Name))
                    throw new InvalidDataException("Unknown or duplicate persisted Job state property.");
            if (expected.Count != 0)
                throw new InvalidDataException("A required persisted Job state property is missing.");
            var version = root.GetProperty("schema_version");
            if (version.ValueKind != JsonValueKind.Number || version.GetRawText() != "1")
                throw new InvalidDataException("The persisted Job state schema version must be the integer 1.");
            var storedJobId = new JobId(ReadString(root, "job_id"));
            var storedUniverseId = new UniverseId(ReadString(root, "universe_id"));
            var state = JobStateTokens.FromToken(ReadString(root, "state"));
            if (storedJobId != jobId)
                throw new InvalidDataException("The persisted Job ID does not match the requested Job ID.");
            if (storedUniverseId != _context.Id)
                throw new InvalidDataException("The persisted universe does not match the store context.");
            return new JobStateRecord(jobId, _context.Id, state);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The persisted Job state JSON is invalid.", exception);
        }
        catch (InvalidOperationException exception)
        {
            // JsonDocument can defer UTF-8/escaped Unicode decoding until a property/string is read.
            throw new InvalidDataException("The persisted Job state JSON text is invalid.", exception);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("The persisted Job state identity is invalid.", exception);
        }
    }

    public JobStateRecord Transition(JobId jobId, JobState nextState)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        if (!Enum.IsDefined(nextState))
            throw new ArgumentOutOfRangeException(nameof(nextState));
        lock (_writeLock)
        {
            var next = _machine.Transition(Load(jobId), nextState);
            Publish(next, JournalPath(jobId), overwrite: true);
            return next;
        }
    }

    private string JournalPath(JobId jobId) => Path.Combine(_context.Storage.StateRoot, jobId.Value + ".json");

    private static FileStream OpenJournal(string path)
    {
        try { return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete); }
        catch (DirectoryNotFoundException exception)
        {
            throw new FileNotFoundException("The persisted Job state does not exist.", path, exception);
        }
    }

    private static string ReadString(JsonElement root, string name)
    {
        var value = root.GetProperty(name);
        if (value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("A persisted Job state value has the wrong JSON type.");
        return value.GetString()!;
    }

    private static InvalidOperationException AlreadyExists(JobId jobId) =>
        new($"A persisted state already exists for Job ID '{jobId.Value}'.");

    private void Publish(JobStateRecord record, string path, bool overwrite)
    {
        Directory.CreateDirectory(_context.Storage.StateRoot);
        var bytes = Serialize(record);
        var temp = Path.Combine(_context.Storage.StateRoot, record.JobId.Value + "." + Guid.NewGuid().ToString("N") + ".tmp");
        using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        // Same-directory publication after closing a fully written temp; never delete the old final first.
        // Failed/crashed publications can leave a temp for 6.5; do not scan or clean up orphans here.
        File.Move(temp, path, overwrite);
    }

    private static byte[] Serialize(JobStateRecord record)
    {
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", 1);
            writer.WriteString("job_id", record.JobId.Value);
            writer.WriteString("universe_id", record.UniverseId.Value);
            writer.WriteString("state", JobStateTokens.ToToken(record.State));
            writer.WriteEndObject();
            writer.Flush();
        }
        output.WriteByte((byte)'\n');
        return output.ToArray();
    }
}

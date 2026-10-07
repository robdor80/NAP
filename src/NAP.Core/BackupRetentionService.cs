namespace NAP.Core;

/// <summary>Per-kind UTC policy: newest N plus newest representative of each of the most recent nonempty calendar buckets.</summary>
public sealed record BackupRetentionPolicy
{
    public BackupRetentionPolicy(int newest, int daily = 0, int weekly = 0, int monthly = 0)
    {
        if (newest < 0 || daily < 0 || weekly < 0 || monthly < 0) throw new ArgumentOutOfRangeException(nameof(newest));
        Newest = newest; Daily = daily; Weekly = weekly; Monthly = monthly;
    }
    public int Newest { get; }
    public int Daily { get; }
    public int Weekly { get; }
    public int Monthly { get; }
}
public enum BackupRetentionAction { Keep, Delete }
public sealed record BackupRetentionDecision(BackupHistoryEntry Backup, BackupRetentionAction Action);
public sealed class BackupRetentionPlan
{
    internal BackupRetentionPlan(UniverseContext c, BackupRetentionPolicy policy, Sha256Digest inventory, IEnumerable<BackupRetentionDecision> decisions)
    { Binding = BackupStorage.Binding(c); Policy = policy; Inventory = inventory; Decisions = Array.AsReadOnly(decisions.ToArray()); UniverseId = c.Id; }
    public UniverseId UniverseId { get; }
    public BackupRetentionPolicy Policy { get; }
    public IReadOnlyList<BackupRetentionDecision> Decisions { get; }
    internal string Binding { get; }
    internal Sha256Digest Inventory { get; }
}
public sealed record BackupRetentionResult(IReadOnlyList<BackupId> DeletedBackupIds);

public sealed class BackupRetentionService
{
    internal Action<string>? Observer { get; set; }
    public BackupRetentionPlan Plan(UniverseContext context, BackupRetentionPolicy policy) => BackupStorage.Run(() =>
    {
        ArgumentNullException.ThrowIfNull(policy);
        using var archive = BackupStorage.Archive(context, false);
        var before = BackupHistoryReader.Inventory(context); var history = BackupHistoryReader.ReadLocked(context);
        var decisions = Decide(history.Entries, policy);
        if (before != BackupHistoryReader.Inventory(context)) throw BackupException.Stop(NapIssueCodes.RetentionStalePlan, "Backup history changed while planning.");
        return new BackupRetentionPlan(context, policy, before, decisions);
    });
    public BackupRetentionResult Execute(UniverseContext context, BackupRetentionPlan plan) => BackupStorage.Run(() =>
    {
        ArgumentNullException.ThrowIfNull(plan); using var archive = BackupStorage.Archive(context, true);
        if (plan.Binding != BackupStorage.Binding(context) || plan.Inventory != BackupHistoryReader.Inventory(context))
            throw BackupException.Stop(NapIssueCodes.RetentionStalePlan, "Backup history or context changed after retention planning.");
        var history = BackupHistoryReader.ReadLocked(context);
        var current = Decide(history.Entries, plan.Policy);
        if (!current.Select(d => (d.Backup.BackupId, d.Action, d.Backup.ManifestSha256)).SequenceEqual(plan.Decisions.Select(d => (d.Backup.BackupId, d.Action, d.Backup.ManifestSha256))))
            throw BackupException.Stop(NapIssueCodes.RetentionStalePlan, "Retention decisions no longer match verified history.");
        Observer?.Invoke("revalidated");
        if (plan.Inventory != BackupHistoryReader.Inventory(context)) throw BackupException.Stop(NapIssueCodes.RetentionStalePlan, "Backup history changed before retention execution.");
        var completed = new List<BackupId>();
        try
        {
            foreach (var decision in plan.Decisions.Where(d => d.Action == BackupRetentionAction.Delete))
            {
                var entry = decision.Backup; var original = BackupStorage.DirectoryPath(context, entry.Kind, entry.BackupId);
                var verified = BackupStorage.Verify(context, original, entry.Kind, entry.BackupId);
                if (verified.ManifestSha256 != entry.ManifestSha256) throw BackupException.Stop(NapIssueCodes.RetentionStalePlan, "A selected backup changed before removal.");
                var quarantine = Path.Combine(BackupStorage.Root(context, entry.Kind), ".deleting-" + Guid.NewGuid().ToString("N"));
                BackupStorage.Context(context); BackupStorage.Check(quarantine); Directory.Move(original, quarantine);
                // The directory rename removes the artifact+manifest as one unit from recognized history.
                // Reverify before physical cleanup; never recursively delete arbitrary paths or follow links.
                BackupStorage.Verify(context, quarantine, entry.Kind, entry.BackupId);
                Observer?.Invoke("quarantined"); RemoveControlled(quarantine, entry.Manifest);
                completed.Add(entry.BackupId); Observer?.Invoke("deleted");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        { throw BackupException.Stop(NapIssueCodes.RetentionExecutionFailed, "Retention was interrupted; completed IDs are reported and any quarantined unit remains for explicit review.", completed); }
        return new BackupRetentionResult(Array.AsReadOnly(completed.ToArray()));
    });
    private static BackupRetentionDecision[] Decide(IReadOnlyList<BackupHistoryEntry> history, BackupRetentionPolicy p)
    {
        var keep = new HashSet<BackupId>();
        foreach (var group in history.GroupBy(e => e.Kind))
        {
            var sorted = group.OrderByDescending(e => e.CreatedUtc).ThenBy(e => e.BackupId.Value, StringComparer.Ordinal).ToArray();
            foreach (var e in sorted.Take(Math.Max(1, p.Newest))) keep.Add(e.BackupId);
            Buckets(p.Daily, e => e.CreatedUtc.UtcDateTime.Date);
            Buckets(p.Weekly, e => { var d = e.CreatedUtc.UtcDateTime.Date; return d.AddDays(-(((int)d.DayOfWeek + 6) % 7)); });
            Buckets(p.Monthly, e => new DateTime(e.CreatedUtc.Year, e.CreatedUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc));
            void Buckets(int count, Func<BackupHistoryEntry, DateTime> bucket)
            { foreach (var e in sorted.GroupBy(bucket).Take(count).Select(g => g.First())) keep.Add(e.BackupId); }
        }
        return history.Select(e => new BackupRetentionDecision(e, keep.Contains(e.BackupId) ? BackupRetentionAction.Keep : BackupRetentionAction.Delete)).ToArray();
    }
    private static void RemoveControlled(string root, BackupManifest m)
    {
        if (m.Kind == BackupKind.RepositorySnapshot)
        {
            var tree = Path.Combine(root, m.Artifact);
            foreach (var f in m.Files) { var path = BackupStorage.Resolve(tree, f.RelativePath); BackupStorage.VerifyFile(path, f.Size, f.Sha256); File.Delete(path); }
            foreach (var d in m.Directories.OrderByDescending(d => d.Count(c => c == '/')).ThenByDescending(d => d, StringComparer.Ordinal))
            { var path = BackupStorage.Resolve(tree, d); BackupStorage.RequireDirectory(path); Directory.Delete(path, recursive: false); }
            BackupStorage.RequireDirectory(tree); Directory.Delete(tree, recursive: false);
        }
        else
        { var path = Path.Combine(root, m.Artifact); BackupStorage.VerifyFile(path, m.Size, m.Sha256); File.Delete(path); }
        var manifest = Path.Combine(root, "manifest.json"); BackupStorage.Fingerprint(manifest); File.Delete(manifest);
        BackupStorage.RequireDirectory(root); Directory.Delete(root, recursive: false);
    }
}

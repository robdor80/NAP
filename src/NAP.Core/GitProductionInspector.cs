namespace NAP.Core;

public sealed class GitProductionInspector
{
    internal GitProductionProcess Git { get; set; } = new();
    public GitRepositoryStatus Inspect(UniverseContext context) => GitProductionSafety.Run(() =>
    { using var lease = GitProductionSafety.Acquire(context); return InspectLocked(context); });

    internal GitRepositoryStatus InspectLocked(UniverseContext c)
    {
        var config = GitProductionSafety.Configuration(c, Git); GitProductionSafety.NoLocalScripts(c, false); GitProductionSafety.Operation(c);
        var porcelain = Git.Status(c); var parsed = Parse(porcelain);
        if (parsed.Branch == "(detached)") throw GitProductionException.Stop(NapIssueCodes.GitDetachedHead, "Detached HEAD cannot publish production.");
        if (parsed.Head == "(initial)") throw GitProductionException.Stop(NapIssueCodes.GitUnbornBranch, "An unborn branch cannot publish production.");
        GitProductionSafety.Sha(parsed.Head); GitProductionSafety.RefName(parsed.Branch);
        var upstream = Git.Upstream(c).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r').Split('\0')).SingleOrDefault(p => p.Length == 4 && p[0] == "refs/heads/" + parsed.Branch);
        if (upstream is null || upstream[1].Length == 0 || upstream[2].Length == 0 || upstream[2] == "." || !upstream[3].StartsWith("refs/heads/", StringComparison.Ordinal))
            throw GitProductionException.Stop(NapIssueCodes.GitUpstreamMissing, "Branch requires an existing remote upstream.");
        var remote = upstream[2]; var remoteBranch = upstream[3][11..]; GitProductionSafety.RefName(remote); GitProductionSafety.RefName(remoteBranch);
        if (parsed.Branch != remoteBranch) throw GitProductionException.Stop(NapIssueCodes.GitRemoteInvalid, "Local and upstream branch names must match.");
        var fingerprint = GitProductionSafety.RemoteFingerprint(config, remote, Git.AllowLocalTestTransport);
        var tip = ReadTip(c, remote, remoteBranch); var sync = GitSynchronization.Unknown;
        if (tip is null) sync = GitSynchronization.RemoteBranchMissing;
        else if (tip == parsed.Head) sync = GitSynchronization.Synchronized;
        else
        {
            // Only classify a remotely observed tip when that object is already available locally. Never fetch.
            var remoteInLocal = false;
            try { Git.ObjectSize(c, tip); remoteInLocal = true; } catch (GitProductionException) { }
            if (remoteInLocal) sync = Git.Ancestor(c, tip, parsed.Head) ? GitSynchronization.Ahead :
                Git.Ancestor(c, parsed.Head, tip) ? GitSynchronization.Behind : GitSynchronization.Diverged;
        }
        return new(c.Id, GitProductionSafety.Binding(c), parsed.Head, parsed.Branch, upstream[1], remote, remoteBranch,
            fingerprint, tip, sync, GitProductionSafety.ConfigurationHash(config), porcelain, parsed.Changes);
    }
    internal string? ReadTip(UniverseContext c, string remote, string branch)
    {
        var output = Git.RemoteTip(c, remote, branch);
        var rows = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (rows.Length == 0) return null;
        if (rows.Length != 1) throw GitProductionException.Stop(NapIssueCodes.GitRemoteInvalid, "Remote returned an ambiguous branch tip.");
        var parts = rows[0].TrimEnd('\r').Split('\t');
        if (parts.Length != 2 || parts[1] != "refs/heads/" + branch) throw GitProductionException.Stop(NapIssueCodes.GitRemoteInvalid, "Remote returned an unexpected ref.");
        GitProductionSafety.Sha(parts[0]); return parts[0];
    }
    internal sealed record Parsed(string Head, string Branch, IReadOnlyList<GitChangeEntry> Changes);
    internal static Parsed Parse(string porcelain)
    {
        var records = porcelain.Split('\0'); var head = ""; var branch = ""; var changes = new List<GitChangeEntry>();
        for (var i = 0; i < records.Length; i++)
        {
            var line = records[i]; if (line.Length == 0) { if (i != records.Length - 1) Invalid(); continue; }
            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                if (line.StartsWith("# branch.oid ", StringComparison.Ordinal)) { if (head.Length != 0) Invalid(); head = line[13..]; }
                else if (line.StartsWith("# branch.head ", StringComparison.Ordinal)) { if (branch.Length != 0) Invalid(); branch = line[14..]; }
                else if (!line.StartsWith("# branch.upstream ", StringComparison.Ordinal) && !line.StartsWith("# branch.ab ", StringComparison.Ordinal)) Invalid();
                continue;
            }
            if (line.StartsWith("? ", StringComparison.Ordinal)) { Path(line[2..]); changes.Add(new(line[2..], null, '?', '?', GitChangeKind.Untracked, false)); continue; }
            var count = line[0] switch { '1' => 9, '2' => 10, 'u' => 11, _ => 0 }; if (count == 0) Invalid();
            var fields = line.Split(' ', count); if (fields.Length != count || fields[1].Length != 2 || fields[2].Length != 4) Invalid();
            var path = fields[^1]; Path(path); string? original = null;
            if (line[0] == '2') { if (++i >= records.Length) Invalid(); original = records[i]; Path(original); }
            var xy = fields[1];
            if (xy.Any(c => !".MADRCUT".Contains(c))) Invalid();
            var kind = line[0] == 'u' ? GitChangeKind.Conflicted : line[0] == '2' ? (xy.Contains('C') ? GitChangeKind.Copied : GitChangeKind.Renamed) :
                xy.Contains('D') ? GitChangeKind.Deleted : xy.Contains('A') ? GitChangeKind.Added : xy.Contains('T') ? GitChangeKind.TypeChanged : GitChangeKind.Modified;
            changes.Add(new(path, original, xy[0], xy[1], kind, fields[2][0] == 'S'));
        }
        if (head.Length == 0 || branch.Length == 0 || changes.Select(c => c.RelativePath).Distinct(StringComparer.Ordinal).Count() != changes.Count) Invalid();
        return new(head, branch, Array.AsReadOnly(changes.OrderBy(c => c.RelativePath, StringComparer.Ordinal).ToArray()));
        static void Path(string path)
        { if (path.Length == 0 || path.StartsWith('/') || path.Split('/').Any(p => p is "" or "." or ".." || p.Equals(".git", StringComparison.OrdinalIgnoreCase))) Invalid(); }
        static void Invalid() => throw GitProductionException.Stop(NapIssueCodes.GitRepositoryInvalid, "Git porcelain output is malformed or unsupported.");
    }
}

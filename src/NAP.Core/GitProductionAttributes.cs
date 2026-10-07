namespace NAP.Core;

/// <summary>Fail closed before staging: v1 publishes physical bytes without Git transformations.</summary>
internal static class GitProductionAttributes
{
    internal static readonly string[] Names = ["filter", "working-tree-encoding", "ident", "text", "eol", "crlf"];

    internal static void Require(UniverseContext context, GitProductionProcess git, IReadOnlyList<string> paths)
    {
        var output = git.Attributes(context, paths);
        var records = output.Split('\0');
        if (records.Length != paths.Count * Names.Length * 3 + 1 || records[^1] != "") Stop();
        var offset = 0;
        foreach (var path in paths)
            foreach (var name in Names)
            {
                if (records[offset] != path || records[offset + 1] != name || records[offset + 2] is not ("unspecified" or "unset")) Stop();
                offset += 3;
            }
        GitProductionSafety.NoLocalScripts(context, true);
    }

    private static void Stop() => throw GitProductionException.Stop(NapIssueCodes.GitStagedContentMismatch,
        "Active filters, encoding, ident or text/EOL transformations are unsupported; no output may be staged.");
}

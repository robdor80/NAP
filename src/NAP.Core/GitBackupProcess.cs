using System.Diagnostics;
using System.ComponentModel;
using System.Text;

namespace NAP.Core;

/// <summary>Private, fixed Git backup allowlist. No public executable, arguments, SQL or arbitrary repository paths.</summary>
internal sealed class GitBackupProcess
{
    internal string Executable { get; set; } = "git";
    internal TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(2);
    internal const int OutputLimit = 1024 * 1024;
    internal string Root(UniverseContext c) => Run(c, "rev-parse", "--show-toplevel").Trim();
    internal string GitDirectory(UniverseContext c) => Run(c, "rev-parse", "--absolute-git-dir").Trim();
    internal string Head(UniverseContext c) => Run(c, "rev-parse", "--verify", "HEAD").Trim();
    internal bool Dirty(UniverseContext c) => Run(c, "status", "--porcelain=v1", "--untracked-files=all").Length != 0;
    internal string Refs(UniverseContext c) => Run(c, "show-ref", "--head");
    internal void Create(UniverseContext c, string controlledTemp) => Run(c, "bundle", "create", controlledTemp, "--all");
    internal void Verify(UniverseContext c, string controlledTemp) => Run(c, "bundle", "verify", controlledTemp);

    private string Run(UniverseContext c, params string[] args)
    {
        // Call sites above are the allowlist; caller-supplied names never become switches.
        var start = new ProcessStartInfo { FileName = Executable, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = c.Storage.ProductionRoot };
        foreach (var value in new[] { "--no-pager", "-c", "core.fsmonitor=false", "-c", "core.untrackedCache=false", "-c", "gc.auto=0", "-c", "maintenance.auto=false", "-C", c.Storage.ProductionRoot }.Concat(args))
            start.ArgumentList.Add(value);
        foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
        start.Environment["GIT_TERMINAL_PROMPT"] = "0"; start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1"; start.Environment["GIT_CONFIG_GLOBAL"] = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
        start.Environment["GIT_NO_REPLACE_OBJECTS"] = "1"; start.Environment["GIT_NO_LAZY_FETCH"] = "1";
        start.Environment["GIT_ALLOW_PROTOCOL"] = ""; // Even an accidental object lookup cannot use a transport.
        using var process = new Process { StartInfo = start };
        try { if (!process.Start()) throw BackupException.Stop(NapIssueCodes.GitUnavailable, "Git could not be started."); }
        catch (Win32Exception) { throw BackupException.Stop(NapIssueCodes.GitUnavailable, "Git is unavailable; install it locally or correct PATH."); }
        using var timeout = new CancellationTokenSource(Timeout);
        var output = Drain(process.StandardOutput);
        var errors = Drain(process.StandardError);
        try
        {
            Task.WhenAll(output, errors, process.WaitForExitAsync(timeout.Token)).GetAwaiter().GetResult();
            if (process.ExitCode != 0) throw BackupException.Stop(NapIssueCodes.GitBundleFailed, "A permitted local Git backup command returned an error.");
            return output.Result;
        }
        catch (OperationCanceledException)
        { throw BackupException.Stop(NapIssueCodes.GitTimeout, "Git backup command exceeded its bounded timeout."); }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                process.WaitForExit(5000);
            }
        }
        async Task<string> Drain(StreamReader reader)
        {
            try { return await ReadBounded(reader, timeout.Token).ConfigureAwait(false); }
            catch (BackupException) { timeout.Cancel(); throw; }
        }
    }
    private static async Task<string> ReadBounded(StreamReader reader, CancellationToken token)
    {
        var result = new StringBuilder(); var buffer = new char[4096];
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false); if (count == 0) break;
            if (result.Length + count > OutputLimit) throw BackupException.Stop(NapIssueCodes.GitOutputLimit, "Git output exceeds the bounded backup command limit.");
            result.Append(buffer, 0, count);
        }
        return result.ToString();
    }
}

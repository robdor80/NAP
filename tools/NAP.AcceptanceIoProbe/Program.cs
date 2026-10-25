using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

// Runner-only observer; never shipped with the acceptance application.
if (!OperatingSystem.IsWindows() || args.Length < 2) return 64;
var executable = Path.GetFullPath(args[0]);
var report = Path.GetFullPath(args[1]);
using var session = new TraceEventSession("NAP-Acceptance-" + Guid.NewGuid().ToString("N"));
var events = new ConcurrentQueue<(int Pid, string Operation, string Path)>();
void Record(int pid, string operation, string path) { if (!string.IsNullOrEmpty(path)) events.Enqueue((pid, operation, path)); }
session.Source.Kernel.FileIOCreate += e => Record(e.ProcessID, "open", e.FileName);
session.Source.Kernel.FileIORead += e => Record(e.ProcessID, "read", e.FileName);
session.Source.Kernel.FileIOWrite += e => Record(e.ProcessID, "write", e.FileName);
session.Source.Kernel.FileIODelete += e => Record(e.ProcessID, "delete", e.FileName);
session.Source.Kernel.FileIORename += e => Record(e.ProcessID, "rename", e.FileName);
session.EnableKernelProvider(KernelTraceEventParser.Keywords.FileIO | KernelTraceEventParser.Keywords.FileIOInit | KernelTraceEventParser.Keywords.Process);
var observing = Task.Run(() => session.Source.Process());
var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)! };
foreach (var arg in args.Skip(2)) start.ArgumentList.Add(arg);
using var process = Process.Start(start)!;
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
try { await process.WaitForExitAsync(timeout.Token); }
catch { process.Kill(true); throw; }
await Task.Delay(1500); var lost = session.EventsLost; session.Stop(); await observing;
var own = events.Where(e => e.Pid == process.Id).Select(e => new { operation = e.Operation, path = e.Path }).Distinct().ToArray();
var forbidden = own.Where(e => e.path.Contains(@"\NAP\", StringComparison.OrdinalIgnoreCase) &&
    (e.path.Contains(@"\AppData\Local\", StringComparison.OrdinalIgnoreCase) || e.path.Contains(@"\ProgramData\", StringComparison.OrdinalIgnoreCase)) ||
    e.path.Contains("NAP-coordination-v2-", StringComparison.OrdinalIgnoreCase)).ToArray();
var writes = own.Where(e => e.operation is "write" or "delete" or "rename").ToArray();
var outsideLab = writes.Where(e => !e.path.Contains(@"\laboratorio\", StringComparison.OrdinalIgnoreCase)).ToArray();
File.WriteAllText(report, JsonSerializer.Serialize(new { processId = process.Id, exitCode = process.ExitCode, capturedFileEvents = own.Length,
    forbiddenNapAccess = forbidden, writesOutsideLab = outsideLab, events = own, eventsLost = lost }, new JsonSerializerOptions { WriteIndented = true }));
if (own.Length == 0 || lost != 0 || forbidden.Length != 0 || outsideLab.Length != 0 ||
    !writes.Any(e => e.path.EndsWith(@"\laboratorio\aceptacion.json", StringComparison.OrdinalIgnoreCase))) return 7;
return process.ExitCode;

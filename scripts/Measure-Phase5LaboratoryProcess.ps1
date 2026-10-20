param([Parameter(Mandatory)][int]$ProcessId,
      [Parameter(Mandatory)][string]$LaboratoryRoot)
$ErrorActionPreference = 'Stop'
if (-not [OperatingSystem]::IsWindows()) { throw 'This read-only OS counter probe requires Windows.' }
$taskLabRoot = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($LaboratoryRoot))
$taskTempRoot = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath([IO.Path]::GetTempPath())) + [IO.Path]::DirectorySeparatorChar
if (-not $taskLabRoot.StartsWith($taskTempRoot, [StringComparison]::OrdinalIgnoreCase) -or
    -not [IO.Path]::GetFileName($taskLabRoot).StartsWith('nap-host-', [StringComparison]::Ordinal) -or
    -not [IO.Directory]::Exists($taskLabRoot) -or
    ([IO.File]::GetAttributes($taskLabRoot) -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw 'Only an existing generated TEMP laboratory may be measured.'
}
$taskLaunchPath = [IO.Path]::Combine($taskLabRoot, 'launch.json')
$taskIdentity = Get-CimInstance Win32_Process -Filter "ProcessId=$ProcessId"
if ($null -eq $taskIdentity -or $taskIdentity.Name -ne 'dotnet.exe' -or
    -not $taskIdentity.CommandLine.Contains('NAP.AutomationHost.dll --laboratory-config ', [StringComparison]::Ordinal) -or
    -not $taskIdentity.CommandLine.Contains($taskLaunchPath, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'PID does not identify the specified synthetic AutomationHost.'
}
if (-not ('Phase5ReadOnlyCounters.Native' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
namespace Phase5ReadOnlyCounters {
    [StructLayout(LayoutKind.Sequential)]
    public struct Io {
        public ulong ReadOperations, WriteOperations, OtherOperations;
        public ulong ReadBytes, WriteBytes, OtherBytes;
    }
    public static class Native {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetProcessIoCounters(IntPtr process, out Io counters);
        public static Io Read(IntPtr process) {
            if (!GetProcessIoCounters(process, out var counters))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return counters;
        }
    }
}
'@
}
$taskProcess = Get-Process -Id $ProcessId
try {
    # Bind the managed handle to the observed process incarnation; never sample a reused PID.
    if ([Math]::Abs(($taskProcess.StartTime - $taskIdentity.CreationDate).TotalSeconds) -gt 1) {
        throw 'Process incarnation changed during observation.'
    }
    $taskIo = [Phase5ReadOnlyCounters.Native]::Read($taskProcess.Handle)
    [ordered]@{
        observedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        processId = $ProcessId
        laboratory = [IO.Path]::GetFileName($taskLabRoot)
        startedUtc = $taskProcess.StartTime.ToUniversalTime().ToString('O')
        cpuSeconds = $taskProcess.TotalProcessorTime.TotalSeconds
        workingBytes = $taskProcess.WorkingSet64
        peakWorkingBytes = $taskProcess.PeakWorkingSet64
        privateBytes = $taskProcess.PrivateMemorySize64
        handles = $taskProcess.HandleCount
        threads = $taskProcess.Threads.Count
        io = $taskIo
        note = 'Read-only Windows process counters; IO bytes are logical transfer counters, not a physical-disk benchmark.'
    } | ConvertTo-Json -Depth 5
} finally { $taskProcess.Dispose() }

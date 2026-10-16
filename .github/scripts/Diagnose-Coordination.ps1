param(
    [Parameter(Mandatory=$true)][string]$ProbePath,
    [Parameter(Mandatory=$true)][string]$RuntimeVersions,
    [Parameter(Mandatory=$true)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$probe = [IO.Path]::GetFullPath($ProbePath)
$versions = $RuntimeVersions.Split(',')
$root = Join-Path ([IO.Path]::GetTempPath()) ('nap-coordination-probe-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root) | Out-Null
$runtimeInfo = @()
$results = @()
function Start-Peer([string]$version, [string[]]$arguments) {
    $start = [Diagnostics.ProcessStartInfo]::new('dotnet')
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true; $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    foreach ($part in (@('--fx-version', $version, $probe) + $arguments)) { $start.ArgumentList.Add($part) }
    return [Diagnostics.Process]::Start($start)
}
function Invoke-Peer([string]$version, [string[]]$arguments) {
    $peer = Start-Peer $version $arguments
    try {
        $stdout = $peer.StandardOutput.ReadToEndAsync(); $stderr = $peer.StandardError.ReadToEndAsync()
        if (-not $peer.WaitForExit(10000)) { $peer.Kill($true); throw 'Probe deadline exceeded.' }
        return @{ Code=$peer.ExitCode; Output=$stdout.GetAwaiter().GetResult(); Error=$stderr.GetAwaiter().GetResult() }
    } finally { $peer.Dispose() }
}
try {
    foreach ($version in $versions) {
        $info = Invoke-Peer $version @('info')
        if ($info.Code -ne 0) { throw ('Probe runtime unavailable: ' + $version + ' ' + $info.Error) }
        $runtimeInfo += ($info.Output | ConvertFrom-Json)
    }
    foreach ($owner in $versions) {
      foreach ($scope in @('AutomationOwner','Job','Production','Catalog')) {
        foreach ($mode in @('mutex','file','dual')) {
            $identity = if ($mode -ne 'file') { 'NAP.Execution.' + $scope + '.' + [guid]::NewGuid().ToString('N') } else { Join-Path $root 'probe.lock' }
            $ownerArguments = @('hold', $mode, $identity)
            if ($mode -eq 'dual') { $ownerArguments += (Join-Path $root 'probe.lock') }
            $held = Start-Peer $owner $ownerArguments
            try {
                $ready = $held.StandardOutput.ReadLineAsync().WaitAsync([TimeSpan]::FromSeconds(10)).GetAwaiter().GetResult()
                if ($ready -ne 'LOCKED') { throw ('Owner did not acquire: ' + $held.StandardError.ReadToEnd()) }
                foreach ($contender in $versions) {
                    $contenderMode = if ($mode -eq 'dual') { 'mutex' } else { $mode }
                    $attempt = Invoke-Peer $contender @('try', $contenderMode, $identity)
                    if ($attempt.Code -notin @(0,10)) { throw ('Contender failed: ' + $attempt.Error) }
                    $observationMode = if ($mode -eq 'dual') { 'dual-vs-legacy-mutex' } else { $mode }
                    $results += [pscustomobject]@{ Scope=$scope; Mode=$observationMode; Owner=$owner; Contender=$contender; Excluded=($attempt.Code -eq 10) }
                }
                if ($mode -eq 'mutex') {
                    $other = [Threading.Mutex]::new($false, $identity)
                    try {
                        $acquired = $other.WaitOne(0)
                        $results += [pscustomobject]@{ Scope=$scope; Mode='mutex'; Owner=$owner; Contender='PowerShell'; Excluded=(-not $acquired) }
                        if ($acquired) { $other.ReleaseMutex() }
                    } finally { $other.Dispose() }
                }
            } finally {
                if (-not $held.HasExited) { $held.StandardInput.WriteLine('release'); $held.StandardInput.Flush() }
                if (-not $held.WaitForExit(10000)) { $held.Kill($true); $held.WaitForExit() }
                $held.Dispose()
            }
        }
      }
    }
    $report = [pscustomobject]@{
        PowerShell=$PSVersionTable.PSVersion.ToString()
        PowerShellRuntime=[Runtime.InteropServices.RuntimeInformation]::FrameworkDescription
        OperatingSystem=[Runtime.InteropServices.RuntimeInformation]::OSDescription
        DotnetInfo=(& dotnet --info | Out-String)
        GitVersion=(& git --version | Out-String).Trim()
        Runtimes=$runtimeInfo
        Observations=$results
    }
    $fullOutput = [IO.Path]::GetFullPath($OutputPath)
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($fullOutput)) | Out-Null
    $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $fullOutput -Encoding utf8
    $report | ConvertTo-Json -Depth 6
} finally {
    # Deliberately retain this synthetic directory and probe.lock as evidence; never unlink a live lock.
}

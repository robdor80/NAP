param(
    [Parameter(Mandatory=$true)][string]$ProbePath,
    [Parameter(Mandatory=$true)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
# Account/ACL experiments are deliberately confined to disposable CI, never the user's MSI.
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'This account/permission experiment runs only on a disposable Actions runner.' }
$probe = [IO.Path]::GetFullPath($ProbePath)
$dotnet = (Get-Command dotnet).Source
$lab = Join-Path ([IO.Path]::GetTempPath()) ('nap-cutover-' + [guid]::NewGuid().ToString('N'))
$public = Join-Path $lab 'peer'
$data = Join-Path $lab 'protected-data'
$startup = Join-Path $lab 'controlled-startup'
foreach ($directory in @($lab,$public,$data,$startup)) { [IO.Directory]::CreateDirectory($directory) | Out-Null }
Copy-Item -LiteralPath ([IO.Path]::GetDirectoryName($probe)) -Destination (Join-Path $public 'bin') -Recurse
$publicProbe = Join-Path $public ('bin/' + [IO.Path]::GetFileName($probe))
$oldName = 'napold' + [guid]::NewGuid().ToString('N').Substring(0,8)
$password = $null
$oldSid = $null
$results = @()
function Set-LabAccess([string]$path, [bool]$allowOld, [bool]$oldWrites = $false) {
    if ($IsWindows) {
        $owner = [Security.Principal.WindowsIdentity]::GetCurrent().User
        $acl = [Security.AccessControl.DirectorySecurity]::new()
        $acl.SetOwner($owner); $acl.SetAccessRuleProtection($true,$false)
        $inherit = [Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit'
        foreach ($sid in @($owner,[Security.Principal.SecurityIdentifier]::new('S-1-5-18'))) {
            $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,'FullControl',$inherit,'None','Allow'))
        }
        if ($allowOld) {
            $rights = if ($oldWrites) { 'ReadAndExecute,Write' } else { 'ReadAndExecute' }
            $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($oldSid,$rights,$inherit,'None','Allow'))
        }
        Set-Acl -LiteralPath $path -AclObject $acl
    } else {
        & chmod $(if ($allowOld) {'777'} else {'700'}) -- $path
        if ($LASTEXITCODE -ne 0) { throw 'Synthetic permissions could not be set.' }
    }
}
function Old-Write([string]$path) {
    $start = [Diagnostics.ProcessStartInfo]::new($(if ($IsWindows) {$dotnet} else {'sudo'}))
    $start.UseShellExecute=$false; $start.CreateNoWindow=$true; $start.RedirectStandardOutput=$true; $start.RedirectStandardError=$true
    if ($IsWindows) { $start.UserName=$oldName; $start.Domain='.'; $start.Password=$password }
    else { foreach ($part in @('-n','-u','nobody','--','env',('TMPDIR=' + $lab),$dotnet)) { $start.ArgumentList.Add($part) } }
    $start.Environment['TEMP']=$lab; $start.Environment['TMP']=$lab
    foreach ($part in @('--fx-version','8.0.31',$publicProbe,'legacy-write',$path)) { $start.ArgumentList.Add($part) }
    $process = [Diagnostics.Process]::Start($start)
    try {
        $stdout=$process.StandardOutput.ReadToEndAsync(); $stderr=$process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(15000)) { $process.Kill($true); throw 'Old-role process did not exit.' }
        $answer = [pscustomobject]@{ Code=$process.ExitCode; Output=$stdout.GetAwaiter().GetResult().Trim(); Error=$stderr.GetAwaiter().GetResult() }
        return $answer
    } finally { $process.Dispose() }
}
try {
    if ($IsWindows) {
        $password = ConvertTo-SecureString ('aA1!' + [guid]::NewGuid().ToString('N')) -AsPlainText -Force
        $user = New-LocalUser -Name $oldName -Password $password -AccountNeverExpires -PasswordNeverExpires
        $oldSid = $user.SID
        Add-LocalGroupMember -Group (Get-LocalGroup -SID 'S-1-5-32-545').Name -Member $oldName
        Set-LabAccess $lab $true; Set-LabAccess $public $true
    } else {
        foreach ($directory in @($lab,$public)) { & chmod 755 -- $directory; if ($LASTEXITCODE -ne 0) { throw 'Public synthetic peer unavailable.' } }
    }
    foreach ($directory in @($data,$startup)) { Set-LabAccess $directory $true $true }
    $file = Join-Path $data 'legacy-write-probe'
    [IO.File]::WriteAllText($file,'synthetic baseline')
    if (-not $IsWindows) { & chmod 666 -- $file; if ($LASTEXITCODE -ne 0) { throw 'Baseline permissions unavailable.' } }
    $baseline = Old-Write $file
    if ($baseline.Code -ne 0 -or $baseline.Output -ne 'WROTE') { throw ('Old-role baseline failed: ' + $baseline.Error) }
    # The old process has exited: no live write handles survive the cutover. Existing paths/data are retained.
    [IO.File]::WriteAllText($file,'preserved synthetic asset')
    $launch = Join-Path $startup 'legacy-write-probe'
    [IO.File]::WriteAllText($launch,('protocol=2;binary_sha256=' + (Get-FileHash -LiteralPath $publicProbe -Algorithm SHA256).Hash))
    $beforeData = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
    $beforeLaunch = (Get-FileHash -LiteralPath $launch -Algorithm SHA256).Hash
    foreach ($directory in @($data,$startup)) { Set-LabAccess $directory $false }
    # POSIX files do not inherit directory permissions, so the directory barrier controls traversal.
    # Windows removes the old inherited ACE from existing files as well as protecting the parent.
    if ($IsWindows) {
        foreach ($path in @($file,$launch)) {
            $acl = Get-Acl -LiteralPath $path
            $acl.SetAccessRuleProtection($false,$false)
            foreach ($rule in @($acl.Access | Where-Object { -not $_.IsInherited })) { [void]$acl.RemoveAccessRuleSpecific($rule) }
            Set-Acl -LiteralPath $path -AclObject $acl
        }
    } else {
        foreach ($path in @($file,$launch)) { & chmod 600 -- $path; if ($LASTEXITCODE -ne 0) { throw 'File access barrier unavailable.' } }
    }
    foreach ($scope in @('AutomationOwner','Job','Production','Catalog')) {
        $start=[Diagnostics.ProcessStartInfo]::new($dotnet)
        $start.UseShellExecute=$false; $start.CreateNoWindow=$true; $start.RedirectStandardInput=$true; $start.RedirectStandardOutput=$true; $start.RedirectStandardError=$true
        foreach ($part in @('--fx-version','8.0.32',$probe,'core-hold',$scope,$data,'synthetic-cutover')) { $start.ArgumentList.Add($part) }
        $owner=[Diagnostics.Process]::Start($start)
        try {
            if ($owner.StandardOutput.ReadLineAsync().WaitAsync([TimeSpan]::FromSeconds(15)).GetAwaiter().GetResult() -ne 'LOCKED') { throw 'New-role owner did not lock.' }
            foreach ($path in @($file,$launch)) {
                $attempt=Old-Write $path
                if ($attempt.Code -ne 11 -or $attempt.Output -ne 'DENIED') { throw ('Old-role access was not denied: ' + $attempt.Error) }
            }
            if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $beforeData -or (Get-FileHash -LiteralPath $launch -Algorithm SHA256).Hash -ne $beforeLaunch) { throw 'Cutover changed existing data or startup evidence.' }
            $results += [pscustomobject]@{ Scope=$scope; OldRuntime='8.0.31'; NewRuntime='8.0.32'; OldWritesDenied=$true; StartupMutationDenied=$true; ExistingHashesPreserved=$true }
        } finally {
            if (-not $owner.HasExited) { $owner.StandardInput.WriteLine('release'); $owner.StandardInput.Flush() }
            if (-not $owner.WaitForExit(15000)) { $owner.Kill($true); $owner.WaitForExit() }
            $owner.Dispose()
        }
    }
    $report=[pscustomobject]@{ Lab=$lab; BaselineOldWriteSucceeded=$true; OldProcessesDrainedBeforeCutover=$true; Observations=$results; Limitation='Synthetic access/startup-file barrier; does not configure or attest the real MSI or its launch policy.' }
    $destination=[IO.Path]::GetFullPath($OutputPath)
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
    $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $destination -Encoding utf8
    $report | ConvertTo-Json -Depth 6
} finally {
    if ($IsWindows -and $null -ne $oldSid) { Remove-LocalUser -Name $oldName }
    # Retain the synthetic paths, data and evidence for the lifetime of this disposable runner.
}

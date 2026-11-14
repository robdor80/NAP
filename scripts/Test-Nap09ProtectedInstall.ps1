# NAP09: exercise protected unsigned installer on a disposable Windows GitHub runner.
# NEVER invoke this on a user's MSI. It installs ONLY binaries in Program Files.
[CmdletBinding()]
param(
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(\.[0-9]+)?$')]
    [string]$Version = '2.0.3'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows -or $env:GITHUB_ACTIONS -ne 'true' -or
    $env:RUNNER_OS -ne 'Windows' -or $env:PROCESSOR_ARCHITECTURE -ne 'AMD64') {
    throw 'NAP09 protected install simulation requires an isolated GitHub-hosted Windows runner.'
}
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$setup = Join-Path $repo ('artifacts\nap06\output\NAP-ProtectedSetup-' + $Version + '.exe')
$sourceInventory = Join-Path $repo 'artifacts\nap06\inventory.json'
$installed = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)) 'NAP'
$data = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)) 'NAP-Production-v1'
$oldInstall = Join-Path $env:LOCALAPPDATA 'Programs\NAP'
if (-not (Test-Path -LiteralPath $setup -PathType Leaf) -or
    -not (Test-Path -LiteralPath $sourceInventory -PathType Leaf)) { throw 'NAP09 package/inventory missing.' }
if ((Test-Path -LiteralPath $installed) -or (Test-Path -LiteralPath $data) -or
    (Test-Path -LiteralPath $oldInstall)) {
    throw 'This runner is not isolated: NAP installation or production data already exists.'
}
$uninstallAttempted = $false
try {
    $process = Start-Process -FilePath $setup -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw 'Synthetic protected installer exited with failure.' }
    $installedInventory = Join-Path $installed 'production-inventory.json'
    $expectedHash = (Get-FileHash -LiteralPath $sourceInventory -Algorithm SHA256).Hash.ToLowerInvariant()
    if (-not (Test-Path -LiteralPath $installedInventory -PathType Leaf) -or
        (Get-FileHash -LiteralPath $installedInventory -Algorithm SHA256).Hash.ToLowerInvariant() -cne $expectedHash) {
        throw 'Installed inventory checksum mismatch.'
    }
    $manifest = Get-Content -LiteralPath $installedInventory -Raw | ConvertFrom-Json
    if ($manifest.count -ne $manifest.files.Count -or $manifest.count -lt 20) {
        throw 'Installed manifest count differs.'
    }
    $root = [IO.Path]::GetFullPath($installed)
    foreach ($entry in @($manifest.files)) {
        $relative = [string]$entry.path
        $target = [IO.Path]::GetFullPath((Join-Path $root ($relative.Replace('/', [IO.Path]::DirectorySeparatorChar))))
        if (-not $target.StartsWith(($root.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar), [StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-Path -LiteralPath $target -PathType Leaf) -or
            (Get-Item -LiteralPath $target).Length -ne [long]$entry.bytes -or
            (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant() -cne [string]$entry.sha256) {
            throw 'Installed protected payload diverges from the manifest.'
        }
    }
    foreach ($forbidden in @('production-launch.json','launch.json','HostControl.db','LegacyPortraits.db','AssetCatalog.db','LegacyMasterEvidence.db')) {
        if (Test-Path -LiteralPath (Join-Path $installed $forbidden)) {
            throw 'The installer unexpectedly installed a configuration or user database.'
        }
    }
    if ((Test-Path -LiteralPath $data) -or (Test-Path -LiteralPath $oldInstall)) {
        throw 'The installer must not provision data or change the old per-user installation.'
    }
    Write-Host ('NAP09 isolated installation PASS: ' + $manifest.count + ' files hash-verified.')
}
finally {
    $uninstall = Join-Path $installed 'unins000.exe'
    if (Test-Path -LiteralPath $uninstall -PathType Leaf) {
        $uninstallAttempted = $true
        $result = Start-Process -FilePath $uninstall -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -Wait -PassThru
        if ($result.ExitCode -ne 0) { throw 'NAP09 isolated uninstaller failed.' }
    }
}
if (-not $uninstallAttempted -or
    (Test-Path -LiteralPath (Join-Path $installed 'host\NAP.AutomationHost.exe')) -or
    (Test-Path -LiteralPath $data) -or (Test-Path -LiteralPath $oldInstall)) {
    throw 'NAP09 uninstall either failed or affected unrelated data.'
}
Write-Host 'NAP09 isolated uninstall PASS; no host, user data or CUTOVER was created.'

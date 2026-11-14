# NAP09 contract smoke test: no real installer execution or MSI mutation.
# Runs on Windows PowerShell 7 in a disposable CI checkout.
[CmdletBinding()]
param(
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(\.[0-9]+)?$')]
    [string]$Version = '2.0.3'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$iss = Get-Content -LiteralPath (Join-Path $repo 'installer\NAP09.Protected.iss') -Raw
$old = Get-Content -LiteralPath (Join-Path $repo 'installer\NAP.iss') -Raw
function Assert-Contains([string]$pattern, [string]$message) {
    if ($iss -notmatch $pattern) { throw $message }
}
Assert-Contains '(?m)^PrivilegesRequired=admin\s*$' 'Protected package must require elevation.'
Assert-Contains '(?m)^DefaultDirName=\{autopf\}\\NAP\s*$' 'Protected package must install under Program Files.'
Assert-Contains '(?m)^DisableDirPage=yes\s*$' 'Protected package must not allow a custom destination.'
Assert-Contains '(?m)^UsePreviousAppDir=no\s*$' 'Protected package cannot re-use per-user install.'
Assert-Contains 'DestName:\s*"production-inventory\.json"' 'Protected package needs the pinned inventory.'
Assert-Contains '(?m)^CloseApplications=no\s*$' 'Protected install must not stop running MSI applications.'
Assert-Contains '(?m)^RestartApplications=no\s*$' 'Protected install cannot restart old writers.'
Assert-Contains 'OutputBaseFilename=NAP-ProtectedSetup-' 'Protected package must be named separately.'
if ($iss -match '(?im)^\s*(\[Run\]|\[UninstallRun\]|\[Dirs\]|\[Registry\]|\[UninstallDelete\])' -or
    $iss -match '(?i)(sc\.exe|schtasks\.exe|net\s+user|New-LocalUser)') {
    throw 'The protected binaries installer cannot provision writer identities or CUTOVER.'
}
$protectedId = [regex]::Match($iss, '(?im)^AppId=(.+)$').Groups[1].Value.Trim()
$oldId = [regex]::Match($old, '(?im)^AppId=(.+)$').Groups[1].Value.Trim()
if (-not $protectedId -or $protectedId -eq $oldId) { throw 'Protected and per-user installers cannot share AppId.' }

$work = Join-Path $repo 'artifacts\nap06'
$inventory = Join-Path $work 'inventory.json'
$protected = Join-Path $work ('output\NAP-ProtectedSetup-' + $Version + '.exe')
$report = Join-Path $work 'nap09-protected-provenance.json'
if (Test-Path -LiteralPath $inventory -PathType Leaf) {
    $manifest = Get-Content -LiteralPath $inventory -Raw | ConvertFrom-Json
    if ($manifest.count -lt 20 -or $manifest.count -ne $manifest.files.Count) {
        throw 'NAP06 manifest invalid.'
    }
    if (-not (Test-Path -LiteralPath $protected -PathType Leaf) -or
        -not (Test-Path -LiteralPath $report -PathType Leaf)) {
        throw 'NAP09 protected package or provenance missing.'
    }
    $p = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    if ($p.cutover_executed -or $p.writer_created -or $p.service_installed -or
        $p.application_data_migrated -or $p.payload_files -ne $manifest.count -or
        $p.artifact_sha256 -cne (Get-FileHash -LiteralPath $protected -Algorithm SHA256).Hash.ToLowerInvariant() -or
        $p.inventory_sha256 -cne (Get-FileHash -LiteralPath $inventory -Algorithm SHA256).Hash.ToLowerInvariant()) {
        throw 'NAP09 provenance differs from the actual binaries or falsely asserts CUTOVER.'
    }
    Write-Host ('NAP09 packaged fixture PASS: ' + $manifest.count + ' inventory entries, verified installer SHA256.')
} else {
    Write-Host 'NAP09 contract-only fixture PASS (no installation performed).'
}

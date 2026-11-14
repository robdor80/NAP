# NAP09 — pruebas de contrato sin tocar ProgramData, NAP del usuario ni catalogos reales.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$scanner = Join-Path $repo 'scripts\NAP09.CoordinationInventory.ps1'
$root = Join-Path $env:TEMP ('nap09-fixture-' + [guid]::NewGuid().ToString('N'))
$coord = Join-Path $root 'coord'
$out = Join-Path $root 'reports'
try {
    [void][IO.Directory]::CreateDirectory($coord)
    $validName = { param($scope, $value) 'NAP.Execution.' + $scope + '.' + ($value * 64) + '.lease' }
    [IO.File]::WriteAllBytes((Join-Path $coord (& $validName 'AutomationOwner' 'a')), [byte[]]@())
    [IO.File]::WriteAllBytes((Join-Path $coord (& $validName 'Catalog' 'b')), [byte[]]@())
    [IO.File]::WriteAllText((Join-Path $coord (& $validName 'AutomationOwner' 'c')), 'unexpected', [Text.Encoding]::UTF8)
    [IO.File]::WriteAllText((Join-Path $coord 'invalid-name.lease'), 'x', [Text.Encoding]::UTF8)
    # Fuera del patrón *.lease no se debe enumerar.
    [IO.File]::WriteAllText((Join-Path $coord 'unrelated.txt'), 'untouched', [Text.Encoding]::UTF8)
    $original = @{}
    foreach ($f in [IO.Directory]::EnumerateFiles($coord)) {
        $original[[IO.Path]::GetFileName($f)] = [Convert]::ToBase64String([IO.File]::ReadAllBytes($f))
    }
    & $scanner -CoordinationRoot $coord -OutputDirectory $out
    $files = @([IO.Directory]::GetFiles($out, '*.json'))
    if ($files.Count -ne 1) { throw 'Expected one inventory JSON.' }
    $json = Get-Content -LiteralPath $files[0] -Raw | ConvertFrom-Json
    if ($json.files_total -ne 4 -or $json.empty_leases -ne 2 -or $json.nonempty_leases -ne 2 -or
        $json.invalid_names -ne 1 -or $json.counts_by_scope.Count -ne 3) {
        throw 'Inventory totals/scopes are incorrect.'
    }
    if ($json.cutover_status -ne 'BLOQUEADO' -or $json.safe_to_delete_or_repair -ne $false) {
        throw 'The inventory must not authorize CUTOVER or cleanup.'
    }
    foreach ($f in [IO.Directory]::EnumerateFiles($coord)) {
        $name = [IO.Path]::GetFileName($f)
        $hash = [Convert]::ToBase64String([IO.File]::ReadAllBytes($f))
        if ($original[$name] -cne $hash) { throw 'A synthetic lease was modified.' }
    }
    Write-Host 'NAP09 coordination fixture: PASS (4 leases, 3 scopes, no modifications).'
} finally {
    $expected = [IO.Path]::GetFullPath($root)
    if ($expected.StartsWith([IO.Path]::GetFullPath($env:TEMP), [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($expected).StartsWith('nap09-fixture-', [StringComparison]::Ordinal)) {
        if ([IO.Directory]::Exists($expected)) { [IO.Directory]::Delete($expected, $true) }
    }
}

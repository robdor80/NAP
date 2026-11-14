# NAP09 — activar el Host personal usando las carpetas YA configuradas en NAP.
# No crea cuentas/servicios, no mueve assets y no activa trabajos automáticamente.
# PowerShell 5.1; ejecutar sólo tras instalar la versión NAP que incluye PersonalMode.
[CmdletBinding()]
param(
    [switch]$Aplicar,
    [string]$BandejaComun = "$env:USERPROFILE\Documents\NAP\Entrada"
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'NAP personal requiere Windows.' }
$local = Join-Path $env:LOCALAPPDATA 'NAP'
$settingsPath = Join-Path $local 'settings.json'
$hostConfigDir = Join-Path $local 'Host'
$configPath = Join-Path $hostConfigDir 'launch.json'
$hostRoot = Join-Path $local 'PersonalHost'
$controlRoot = Join-Path $hostRoot 'control'
$binaryRoot = Join-Path $env:LOCALAPPDATA 'Programs\NAP'
$builtInProfiles = Join-Path $binaryRoot 'config\universes'
$importedProfiles = Join-Path $local 'universes'

if (-not (Test-Path -LiteralPath $settingsPath -PathType Leaf)) {
    throw 'NAP no tiene rutas de universos guardadas. Configura primero las carpetas en la interfaz habitual.'
}
$settings = Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($settings.schema_version -ne 1 -or @($settings.universes).Count -eq 0) {
    throw 'La configuración de carpetas no es válida o está vacía.'
}
if (-not (Test-Path -LiteralPath (Join-Path $binaryRoot 'host\NAP.AutomationHost.exe') -PathType Leaf)) {
    throw 'No está instalado el Host de NAP. No se modifica ninguna carpeta.'
}
$definitions = @()
$ids = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
foreach ($item in @($settings.universes)) {
    $id = [string]$item.universe_id
    if ($id -notmatch '^[a-z][a-z0-9_]{1,63}$' -or -not $ids.Add($id)) {
        throw 'UniverseId repetido o inválido.'
    }
    $roots = @([string]$item.workspace_root, [string]$item.production_root, [string]$item.archive_root)
    foreach ($p in $roots) {
        if (-not [IO.Path]::IsPathRooted($p) -or -not (Test-Path -LiteralPath $p -PathType Container)) {
            throw ('Falta una carpeta ya configurada para ' + $id + ': ' + $p)
        }
    }
    $builtIn = Join-Path (Join-Path $builtInProfiles $id) 'profile.json'
    $imported = Join-Path (Join-Path $importedProfiles $id) 'profile.json'
    if ((Test-Path -LiteralPath $builtIn -PathType Leaf) -and (Test-Path -LiteralPath $imported -PathType Leaf)) {
        throw ('El perfil aparece duplicado para ' + $id + '.')
    }
    $profile = if (Test-Path -LiteralPath $builtIn -PathType Leaf) { $builtIn } elseif (Test-Path -LiteralPath $imported -PathType Leaf) { $imported } else { $null }
    if ($null -eq $profile) { throw ('Falta el perfil instalado para ' + $id + '. No se inventará uno.') }
    $json = [IO.File]::ReadAllText($profile, [Text.Encoding]::UTF8)
    $profileObject = $json | ConvertFrom-Json
    if ($profileObject.universe_id -cne $id -or $profileObject.schema_version -ne 4) {
        throw ('El perfil no corresponde al universo ' + $id)
    }
    $definitions += [ordered]@{
        Storage = [ordered]@{
            UniverseId = [ordered]@{ Value = $id }
            WorkspaceRoot = $roots[0]
            ProductionRoot = $roots[1]
            ArchiveRoot = $roots[2]
        }
        ProfileJson = $json
    }
}
if ($definitions.Count -gt 64) { throw 'Demasiados universos configurados.' }
if (-not [IO.Path]::IsPathRooted($BandejaComun)) { throw 'La bandeja común debe ser una ruta completa.' }
function SameOrDescendant([string]$candidate, [string]$root) {
    $a = [IO.Path]::GetFullPath($candidate).TrimEnd('\')
    $b = [IO.Path]::GetFullPath($root).TrimEnd('\')
    return $a.Equals($b, [StringComparison]::OrdinalIgnoreCase) -or
        $a.StartsWith($b + '\', [StringComparison]::OrdinalIgnoreCase)
}
foreach ($row in $definitions) {
    foreach ($p in @($row.Storage.WorkspaceRoot, $row.Storage.ProductionRoot, $row.Storage.ArchiveRoot)) {
        if ((SameOrDescendant $p $hostRoot) -or (SameOrDescendant $hostRoot $p) -or
            (SameOrDescendant $BandejaComun $p) -or (SameOrDescendant $p $BandejaComun)) {
            throw 'Host o bandeja común se solapan con una raíz existente. Sin cambios.'
        }
    }
}
if ((SameOrDescendant $hostRoot $BandejaComun) -or (SameOrDescendant $BandejaComun $hostRoot)) {
    throw 'La bandeja común se solapa con las carpetas del Host.'
}

Write-Host ('NAP personal: ' + $definitions.Count + ' universo(s) configurado(s): ' +
    (($definitions | ForEach-Object { $_.Storage.UniverseId.Value }) -join ', '))
Write-Host ('Bandeja común: ' + $BandejaComun)
Write-Host ('Host: ' + $hostRoot)
if (Test-Path -LiteralPath $configPath) {
    Write-Host 'NAP ya tiene una configuración de Host. No se sobrescribe ni se cambia.'
    return
}
if (-not $Aplicar) {
    Write-Host 'Vista previa completada. Para habilitar, repite el comando con -Aplicar tras instalar la versión compatible.'
    return
}
foreach ($directory in @($hostRoot, $controlRoot, $hostConfigDir, $BandejaComun)) {
    if (-not (Test-Path -LiteralPath $directory)) {
        [void][IO.Directory]::CreateDirectory($directory)
    }
}
$keyBytes = New-Object byte[] 32
$rng = [Security.Cryptography.RandomNumberGenerator]::Create()
try { $rng.GetBytes($keyBytes) } finally { $rng.Dispose() }
$launch = [ordered]@{
    SchemaVersion = 1
    LaboratoryRoot = $hostRoot
    ControlRoot = $controlRoot
    Connection = [ordered]@{
        PipeName = ('nap-personal-' + [Guid]::NewGuid().ToString('N'))
        KeyBase64 = [Convert]::ToBase64String($keyBytes)
    }
    Universes = @($definitions)
    SyntheticAudit = $false
    LaboratoryCheckpoint = $null
    Admission = $null
    MixedInboxRoot = $BandejaComun
    PersonalMode = $true
}
$text = $launch | ConvertTo-Json -Depth 15
$temporary = $configPath + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
try {
    [IO.File]::WriteAllText($temporary, $text, [Text.UTF8Encoding]::new($false))
    if (Test-Path -LiteralPath $configPath) { throw 'Ya existe una configuración. No se sobrescribe.' }
    [IO.File]::Move($temporary, $configPath)
} finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary }
}
Write-Host ('Host personal configurado: ' + $configPath)
Write-Host 'Al abrir NAP, el Host se conectará bajo tu usuario habitual y permanecerá pausado hasta autorización desde la interfaz.'

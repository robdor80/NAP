# NAP08 — Conectar el AutomationHost del NAP 2.0.2 instalado a un laboratorio TEMP.
# Misma cuenta Windows, sin otra instalacion y sin modificar %LOCALAPPDATA%\NAP.
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$installed = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'Programs\NAP'
$app = Join-Path $installed 'NAP.App.exe'
$hostExe = Join-Path $installed 'host\NAP.AutomationHost.exe'
$profilePath = Join-Path $installed 'config\universes\nimroel\profile.json'

foreach ($file in @($app, $hostExe, $profilePath)) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw "Falta un componente de NAP 2.0.2 instalado: $file"
    }
}
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($app).ProductVersion
if ($version -notmatch '^2\.0\.2(?:\+|$)') {
    throw "La prueba necesita la version NAP 2.0.2 instalada. Encontrada: $version"
}
if (@(Get-Process -Name 'NAP.App' -ErrorAction SilentlyContinue).Count -gt 0) {
    throw 'NAP ya esta abierto. Cierralo desde su icono en la bandeja (Salir) y vuelve a hacer doble clic.'
}

# El Host actual solo admite roots bajo TEMP con prefijo nap-host-.
$lab = Join-Path ([IO.Path]::GetTempPath()) 'nap-host-nap08-msi'
$configPath = Join-Path $lab 'launch.json'
$control = Join-Path $lab 'control'
$workspace = Join-Path $lab 'nimroel\workspace'
$production = Join-Path $lab 'nimroel\production'
$archive = Join-Path $lab 'nimroel\archive'

if (Test-Path -LiteralPath $configPath -PathType Leaf) {
    $existing = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
    if ($existing.SchemaVersion -ne 1 -or $existing.SyntheticAudit -ne $true -or
        $existing.LaboratoryRoot -ne $lab -or $existing.ControlRoot -ne $control -or
        @($existing.Universes).Count -ne 1 -or
        $existing.Universes[0].Storage.WorkspaceRoot -ne $workspace -or
        $existing.Universes[0].Storage.ProductionRoot -ne $production -or
        $existing.Universes[0].Storage.ArchiveRoot -ne $archive) {
        throw 'Ya existe una configuracion distinta en el laboratorio NAP08. No se ha sobrescrito.'
    }
} else {
    if (Test-Path -LiteralPath $lab) {
        throw 'La carpeta de laboratorio existe sin launch.json. Revisarla antes de continuar; no se borra nada.'
    }
    foreach ($folder in @($control, $workspace, (Join-Path $workspace 'inbox'),
        (Join-Path $workspace 'staging'), $production, $archive)) {
        [void][IO.Directory]::CreateDirectory($folder)
    }
    $profile = Get-Content -LiteralPath $profilePath -Raw | ConvertFrom-Json
    if ($profile.schema_version -ne 4 -or $profile.universe_id -ne 'nimroel') {
        throw 'El perfil Nimroel de la instalacion no es el esperado.'
    }
    $profile.display_name = 'Nimroel (laboratorio NAP08)'
    $profileJson = $profile | ConvertTo-Json -Depth 32 -Compress

    $bytes = New-Object byte[] 32
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    $pipe = 'nap-nap08-' + [Guid]::NewGuid().ToString('N')
    $config = [ordered]@{
        SchemaVersion = 1
        LaboratoryRoot = $lab
        ControlRoot = $control
        Connection = [ordered]@{
            PipeName = $pipe
            KeyBase64 = [Convert]::ToBase64String($bytes)
        }
        Universes = @(
            [ordered]@{
                Storage = [ordered]@{
                    UniverseId = [ordered]@{ Value = 'nimroel' }
                    WorkspaceRoot = $workspace
                    ProductionRoot = $production
                    ArchiveRoot = $archive
                }
                ProfileJson = $profileJson
            }
        )
        SyntheticAudit = $true
    }
    [IO.File]::WriteAllText($configPath, ($config | ConvertTo-Json -Depth 32),
        [Text.UTF8Encoding]::new($false))
}

# El Host sintetico no debe heredar credenciales Gemini del perfil habitual.
# Se eliminan solo del proceso que crea el hijo; las variables persistentes
# del usuario y del sistema no se modifican.
$key = [Environment]::GetEnvironmentVariable('NAP_GEMINI_API_KEY', 'Process')
$model = [Environment]::GetEnvironmentVariable('NAP_GEMINI_MODEL', 'Process')
try {
    [Environment]::SetEnvironmentVariable('NAP_GEMINI_API_KEY', $null, 'Process')
    [Environment]::SetEnvironmentVariable('NAP_GEMINI_MODEL', $null, 'Process')
    Write-Host 'NAP08: abriendo NAP 2.0.2 con AutomationHost de laboratorio.'
    Write-Host 'Universo: Nimroel (laboratorio NAP08). Sin acceso a rutas reales.'
    Start-Process -FilePath $app -WorkingDirectory $installed -ArgumentList ('--automation-managed-config="' + $configPath + '"') -ErrorAction Stop
} finally {
    [Environment]::SetEnvironmentVariable('NAP_GEMINI_API_KEY', $key, 'Process')
    [Environment]::SetEnvironmentVariable('NAP_GEMINI_MODEL', $model, 'Process')
}

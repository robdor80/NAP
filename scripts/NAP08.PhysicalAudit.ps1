# NAP08 — Auditoria de solo lectura del equipo fisico (Windows 11).
# No instala, desinstala, inicia procesos NAP, lee secretos ni altera catalogos.
# Unicamente escribe un JSON de evidencia en Descargas\NAP08_Evidencias.
[CmdletBinding()]
param(
    [ValidateSet('Antes','Instalado','Desinstalado')][string]$Etapa = 'Antes',
    [string]$SetupPath,
    [ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$TrustedSha256,
    [string]$InventoryPath,
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$ExpectedVersion = '2.0.0'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not [Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::Windows)) {
    throw 'NAP08 requiere Windows. No se ha modificado nada.'
}
$stops = New-Object 'System.Collections.Generic.List[string]'
$warnings = New-Object 'System.Collections.Generic.List[string]'
function Stop-Gate([string]$reason) { [void]$script:stops.Add($reason) }
function Warn-Gate([string]$reason) { [void]$script:warnings.Add($reason) }
function Is-Reparse([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return $false }
    $item = Get-Item -LiteralPath $path -Force
    return (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)
}
$local = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
$install = Join-Path $local 'Programs\NAP'
$data = Join-Path $local 'NAP'
$launch = Join-Path $data 'Host\launch.json'
$settings = Join-Path $data 'settings.json'
$marker = Join-Path $data 'NAP08-PRESERVAR.txt'
$expectedMarker = 'NAP08-LAB-SOLO'
$os = Get-CimInstance Win32_OperatingSystem
$winBuild = [int]$os.BuildNumber
if ($winBuild -lt 22000) { Stop-Gate 'Se requiere Windows 11 (build 22000 o posterior).' }
if (-not [Environment]::Is64BitOperatingSystem -or -not [Environment]::Is64BitProcess) {
    Stop-Gate 'Ejecutar desde PowerShell x64 en Windows x64.'
}
$processes = @(Get-CimInstance Win32_Process -Filter "Name='NAP.App.exe' OR Name='NAP.AutomationHost.exe'" -ErrorAction SilentlyContinue)
$hostProcesses = @($processes | Where-Object { $_.Name -eq 'NAP.AutomationHost.exe' })
if ($processes.Count -gt 0 -and $Etapa -ne 'Instalado') {
    Stop-Gate 'Existen procesos NAP abiertos: no instalar/desinstalar con procesos o writers activos.'
}
if ($hostProcesses.Count -gt 0) { Stop-Gate 'AutomationHost en ejecucion: detener solo desde su control autorizado antes de continuar.' }
foreach ($p in @($install,$data,$launch,$settings)) {
    if (Is-Reparse $p) { Stop-Gate 'Se ha detectado un reparse point en la instalacion/configuracion NAP.' }
}
if (Test-Path -LiteralPath $launch) { Stop-Gate 'Existe launch.json: riesgo de iniciar Host con una configuracion preexistente.' }
if (Test-Path -LiteralPath $settings) { Stop-Gate 'Existe settings.json: configuracion de universos preexistente.' }
$keyPresent = -not [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable('NAP_GEMINI_API_KEY'))
$modelPresent = -not [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable('NAP_GEMINI_MODEL'))
if ($keyPresent -or $modelPresent) { Stop-Gate 'Hay configuracion Gemini en el entorno. Utilizar sesion de laboratorio sin credenciales.' }
$setupHash = $null
$signature = 'NoComprobada'
$inventoryFilesChecked = 0
$inventoryMismatches = New-Object 'System.Collections.Generic.List[string]'
if ($Etapa -eq 'Antes') {
    if (Test-Path -LiteralPath $install) { Stop-Gate 'Ya existe la instalacion NAP en este perfil de Windows.' }
    if (Test-Path -LiteralPath $data) { Stop-Gate 'Ya existe %LOCALAPPDATA%\NAP en este perfil de Windows: no mezclar datos.' }
    if (-not $SetupPath -or -not $TrustedSha256) {
        Stop-Gate 'Se requieren -SetupPath y -TrustedSha256 independiente para certificar el instalador.'
    } elseif (-not (Test-Path -LiteralPath $SetupPath -PathType Leaf)) {
        Stop-Gate 'No se encuentra el instalador.'
    } elseif (Is-Reparse $SetupPath) {
        Stop-Gate 'El instalador es un enlace/reparse y no se ejecutara.'
    } else {
        $setupHash = (Get-FileHash -LiteralPath $SetupPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($setupHash -cne $TrustedSha256.ToLowerInvariant()) { Stop-Gate 'SHA-256 del instalador distinto del pin independiente.' }
        $signature = [string](Get-AuthenticodeSignature -LiteralPath $SetupPath).Status
        if ($signature -eq 'NotSigned') { Warn-Gate 'NAP no dispone de firma Authenticode: registrar SmartScreen, sin desactivar protecciones.' }
        elseif ($signature -ne 'Valid') { Stop-Gate 'La firma Authenticode presenta un estado no aceptable.' }
    }
    if (Test-Path -LiteralPath $marker) { Stop-Gate 'Existe previamente el marcador del laboratorio: perfil no limpio.' }
} elseif ($Etapa -eq 'Instalado') {
    foreach ($file in @('NAP.App.exe','host\NAP.AutomationHost.exe','updater\NAP07.Update.ps1','config\universes\nimroel\profile.json')) {
        if (-not (Test-Path -LiteralPath (Join-Path $install $file) -PathType Leaf)) { Stop-Gate "Falta componente de la distribucion: $file" }
    }
    $app = Join-Path $install 'NAP.App.exe'
    if (Test-Path -LiteralPath $app -PathType Leaf) {
        $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($app).ProductVersion
        if ($version -notmatch ('^' + [regex]::Escape($ExpectedVersion) + '(\D|$)')) {
            Stop-Gate 'Version App distinta de la esperada.'
        }
    }
    if (-not (Test-Path -LiteralPath $marker -PathType Leaf)) {
        Stop-Gate 'Falta el marcador sintetico para probar la conservacion de datos.'
    } elseif ((Get-Content -LiteralPath $marker -Raw).Trim() -cne $expectedMarker) {
        Stop-Gate 'El marcador de prueba ha cambiado.'
    }
    if (-not $InventoryPath -or -not (Test-Path -LiteralPath $InventoryPath -PathType Leaf)) {
        Stop-Gate 'Falta inventario independiente del build para verificar binarios instalados.'
    } else {
        try {
            $inventory = Get-Content -LiteralPath $InventoryPath -Raw | ConvertFrom-Json
            if ($inventory.version -ne $ExpectedVersion -or $inventory.target -ne 'win-x64' -or @($inventory.files).Count -lt 20) {
                Stop-Gate 'El inventario no corresponde a esta version/plataforma.'
            } else {
                foreach ($f in @($inventory.files)) {
                    $relative = [string]$f.path
                    if ($relative -match '(^[\\/]|^[A-Za-z]:|\.\.|:)' ) {
                        Stop-Gate 'El inventario contiene una ruta no permitida.'
                        break
                    }
                    $path = Join-Path $install $relative
                    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Is-Reparse $path)) {
                        [void]$inventoryMismatches.Add($relative)
                    } elseif ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne ([string]$f.sha256).ToLowerInvariant()) {
                        [void]$inventoryMismatches.Add($relative)
                    }
                    $inventoryFilesChecked++
                }
                if ($inventoryMismatches.Count -gt 0) { Stop-Gate 'Algunos archivos instalados no coinciden con el inventario SHA-256.' }
            }
        } catch { Stop-Gate 'Error al interpretar/verificar el inventario del paquete.' }
    }
} else {
    if (Test-Path -LiteralPath (Join-Path $install 'NAP.App.exe')) {
        Stop-Gate 'Los binarios NAP.App siguen instalados.'
    }
    if (-not (Test-Path -LiteralPath $marker -PathType Leaf)) {
        Stop-Gate 'La desinstalacion no ha conservado el marcador.'
    } elseif ((Get-Content -LiteralPath $marker -Raw).Trim() -cne $expectedMarker) {
        Stop-Gate 'La desinstalacion ha alterado el marcador.'
    }
}
$reportDir = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)) 'Downloads\NAP08_Evidencias'
if (Is-Reparse $reportDir) { throw 'Carpeta de evidencias enlazada; no se guarda el informe.' }
[IO.Directory]::CreateDirectory($reportDir) | Out-Null
$report = [ordered]@{
    schema_version = 1
    phase = 'NAP08'
    stage = $Etapa
    timestamp_utc = [DateTime]::UtcNow.ToString('o')
    machine = 'MSI fisico: declarar manualmente, no inferido'
    os_name = [string]$os.Caption
    os_build = $winBuild
    powerShell = [string]$PSVersionTable.PSVersion
    is_64_bit_os = [Environment]::Is64BitOperatingSystem
    running_nap_count = $processes.Count
    launch_configuration_present = (Test-Path -LiteralPath $launch)
    universe_settings_present = (Test-Path -LiteralPath $settings)
    gemini_environment_present = ($keyPresent -or $modelPresent)
    installer_sha256 = $setupHash
    installer_signature_status = $signature
    installed_files_checked = $inventoryFilesChecked
    installed_mismatch_names = @($inventoryMismatches)
    marker_present = (Test-Path -LiteralPath $marker -PathType Leaf)
    decision = $(if ($stops.Count -eq 0) { 'PASO_AUTOMATICO_OK_PENDIENTE_VALIDACION_HUMANA' } else { 'BLOQUEADO' })
    blocking_reasons = @($stops)
    warnings = @($warnings)
    no_automatic_install_or_cutover = $true
}
$filename = 'NAP08-' + $Etapa + '-' + (Get-Date -Format 'yyyyMMdd-HHmmssfff') + '.json'
$destination = Join-Path $reportDir $filename
[IO.File]::WriteAllText($destination, ($report | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
Write-Host "NAP08: $($report.decision)"
Write-Host "Informe de diagnostico: $destination"
foreach ($reason in $stops) { Write-Warning $reason }
foreach ($warning in $warnings) { Write-Warning $warning }
if ($stops.Count -ne 0) { exit 2 }

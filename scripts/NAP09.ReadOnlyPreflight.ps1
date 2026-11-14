# NAP09 - Diagnostico de solo lectura para decidir la puesta en marcha real.
# Compatible con Windows PowerShell 5.1. No requiere administrador.
# SOLO escribe un informe en Descargas\NAP09_Evidencias.
# NO inicia NAP, Host, Gemini, Git, backups, CUTOVER, ni cambia cuentas o ACL.
[CmdletBinding()]
param(
    [string]$WorkspaceRoot = "$env:USERPROFILE\Documents\NAP\Nimroel",
    [string]$ProductionRoot = "$env:USERPROFILE\Documents\GitHub\Videojuego_Nimroel\Worldbuilding\Direccion artistica\Assets",
    [string]$ArchiveRoot = "$env:USERPROFILE\Documents\Nimroel_Maestros_PNG"
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not [Environment]::Is64BitOperatingSystem -or $env:OS -ne 'Windows_NT') {
    throw 'NAP09: este diagnostico requiere Windows x64.'
}
if (-not [Environment]::Is64BitProcess) {
    throw 'NAP09: abre PowerShell de 64 bits.'
}

$warnings = New-Object 'System.Collections.Generic.List[string]'
function Warn([string]$message) { [void]$script:warnings.Add($message) }

function Get-PathRecord([string]$name, [string]$path) {
    $exists = Test-Path -LiteralPath $path -PathType Container
    $reparse = $false
    $aclOwner = $null
    $aclProtected = $null
    $aclRead = $false
    $current = [IO.Path]::GetFullPath($path)
    # Inspeccionar el destino y todos sus padres; nunca seguir enlaces para escribir.
    while ($current) {
        if (Test-Path -LiteralPath $current) {
            try {
                $item = Get-Item -LiteralPath $current -Force -ErrorAction Stop
                if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                    $reparse = $true
                }
            } catch {
                Warn ("No se pudo inspeccionar un componente de " + $name)
            }
        }
        $parent = [IO.Path]::GetDirectoryName($current)
        if (-not $parent -or $parent -eq $current) { break }
        $current = $parent
    }
    if ($exists) {
        try {
            $acl = Get-Acl -LiteralPath $path -ErrorAction Stop
            $aclOwner = [string]$acl.Owner
            $aclProtected = [bool]$acl.AreAccessRulesProtected
            $aclRead = $true
        } catch {
            Warn ("No se puede leer la ACL de " + $name)
        }
    } else {
        Warn ("No existe la raiz esperada: " + $name)
    }
    if ($reparse) { Warn ("Se detecto enlace/reparse en ruta o padres: " + $name) }
    return [pscustomobject]@{
        name = $name; path = $path; directory_exists = $exists
        reparse_in_chain = $reparse
        acl_read = $aclRead; owner = $aclOwner; acl_inheritance_disabled = $aclProtected
        acl_and_parent_security_certified = $false
    }
}
function Get-FileRecord([string]$path, [bool]$hash) {
    $exists = Test-Path -LiteralPath $path -PathType Leaf
    $size = $null; $sha = $null; $version = $null
    if ($exists) {
        try {
            $item = Get-Item -LiteralPath $path -Force -ErrorAction Stop
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                Warn ("Archivo enlazado: " + $item.Name)
            }
            $size = $item.Length
            if ($hash) {
                $sha = (Get-FileHash -LiteralPath $path -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant()
            }
            if ($item.Extension -eq '.exe') {
                $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($path).ProductVersion
            }
        } catch {
            Warn ("No se pudo inspeccionar un archivo esperado: " + [IO.Path]::GetFileName($path))
        }
    }
    return [pscustomobject]@{
        name = [IO.Path]::GetFileName($path); exists = $exists; bytes = $size
        sha256 = $sha; product_version = $version
    }
}

$installedRoot = Join-Path $env:LOCALAPPDATA 'Programs\NAP'
$stateRoot = Join-Path $WorkspaceRoot 'state'
$base = @(
    (Get-PathRecord 'workspace_nimroel' $WorkspaceRoot),
    (Get-PathRecord 'production_nimroel' $ProductionRoot),
    (Get-PathRecord 'archive_nimroel' $ArchiveRoot),
    (Get-PathRecord 'installed_binary_directory' $installedRoot)
)
$catalogFiles = @(
    (Get-FileRecord (Join-Path $stateRoot 'AssetCatalog.db') $true),
    (Get-FileRecord (Join-Path $stateRoot 'LegacyPortraits.db') $true),
    (Get-FileRecord (Join-Path $stateRoot 'LegacyMasterEvidence.db') $true)
)
$binaries = @(
    (Get-FileRecord (Join-Path $installedRoot 'NAP.App.exe') $true),
    (Get-FileRecord (Join-Path $installedRoot 'host\NAP.AutomationHost.exe') $true)
)

# Inspeccion de nombres/PID solamente. No leer argumentos, variables de entorno ni secretos.
$processState = 'Leido'
$processes = @()
try {
    $processes = @(Get-CimInstance Win32_Process -Filter "Name='NAP.App.exe' OR Name='NAP.AutomationHost.exe' OR Name='NAP.LiveBootstrap.exe'" -ErrorAction Stop |
        Select-Object Name, ProcessId)
} catch {
    $processState = 'NoDisponible'
    Warn 'No se pudo consultar los procesos NAP.'
}
$taskState = 'Leido'
$tasks = @()
try {
    $tasks = @(Get-ScheduledTask -ErrorAction Stop |
        Where-Object { $_.TaskName -match '(?i)NAP|Nexus.Asset' } |
        Select-Object TaskName, TaskPath, State)
} catch {
    $taskState = 'NoDisponible'
    Warn 'No se pudo consultar el programador de tareas.'
}
$serviceState = 'Leido'
$services = @()
try {
    $services = @(Get-CimInstance Win32_Service -ErrorAction Stop |
        Where-Object { $_.Name -match '(?i)NAP|Nexus.Asset' -or $_.DisplayName -match '(?i)NAP|Nexus.Asset' } |
        Select-Object Name, State, StartMode)
} catch {
    $serviceState = 'NoDisponible'
    Warn 'No se pudieron consultar los servicios.'
}

# Directorio de coordinacion calculado a partir del mismo contrato de ExecutionFileLease.
# NO abrir locks ni manipularlos. No recorrer directorios de otros usuarios.
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$coord = Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) ("NAP-coordination-v2-" + $sid)
$coordFound = Test-Path -LiteralPath $coord -PathType Container
$leaseCount = $null
$nonEmptyLeaseCount = $null
if ($coordFound) {
    $leaseCount = 0
    $nonEmptyLeaseCount = 0
    try {
        foreach ($file in Get-ChildItem -LiteralPath $coord -Force -File -Filter '*.lease' -ErrorAction Stop) {
            $leaseCount++
            if ($file.Length -ne 0) { $nonEmptyLeaseCount++ }
        }
    } catch {
        $leaseCount = $null; $nonEmptyLeaseCount = $null
        Warn 'No se pudieron enumerar los leases de la identidad actual.'
    }
}
if ($nonEmptyLeaseCount -gt 0) { Warn 'Hay leases no vacios. No borrar ni reparar.' }

$report = [ordered]@{
    schema_version = 1
    phase = 'NAP09'
    stage = 'PRE_CUTOVER_READ_ONLY'
    timestamp_utc = [DateTime]::UtcNow.ToString('o')
    run_as_administrator_required = $false
    source_change_performed = $false
    host_activation_performed = $false
    cutover_authorized = $false
    installed_and_data_roots = @($base)
    native_and_legacy_catalog_files = @($catalogFiles)
    installed_binaries = @($binaries)
    process_inventory = [ordered]@{ status=$processState; found=@($processes) }
    scheduled_task_inventory = [ordered]@{ status=$taskState; name_based_only=$true; found=@($tasks) }
    service_inventory = [ordered]@{ status=$serviceState; name_based_only=$true; found=@($services) }
    current_sid_coordination = [ordered]@{
        directory_exists=$coordFound; lease_count=$leaseCount; nonempty_leases=$nonEmptyLeaseCount
        other_sid_directories_not_inspected=$true; file_handles_not_inspected=$true
    }
    external_backup_restoration_accredited = $false
    old_writers_and_parent_acl_exclusion_accredited = $false
    cutover_verdict = 'BLOQUEADO_PENDIENTE_DE_AUTORIZACION_Y_BARRERAS_EXPLICITAS'
    warnings = @($warnings)
}

$outdir = Join-Path $env:USERPROFILE 'Downloads\NAP09_Evidencias'
if (Test-Path -LiteralPath $outdir) {
    $outAttrs = (Get-Item -LiteralPath $outdir -Force -ErrorAction Stop).Attributes
    if (($outAttrs -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'NAP09: la carpeta de informes es un enlace; no se guarda evidencia.'
    }
} else {
    [void][IO.Directory]::CreateDirectory($outdir)
}
$outfile = Join-Path $outdir ("NAP09_Preflight_" + (Get-Date -Format 'yyyyMMdd_HHmmssfff') + '.json')
$json = $report | ConvertTo-Json -Depth 7
# Crear exclusivamente un informe nuevo: nunca sobrescribir otro resultado.
$utf8 = New-Object System.Text.UTF8Encoding($false)
$writer = New-Object System.IO.StreamWriter($outfile, $false, $utf8)
try { $writer.Write($json) } finally { $writer.Dispose() }
Write-Host 'NAP09: diagnostico de solo lectura completado.'
Write-Host ("Catalogos detectados: " + @($catalogFiles | Where-Object { $_.exists }).Count + "/3.")
Write-Host ("Binarios instalados detectados: " + @($binaries | Where-Object { $_.exists }).Count + "/2.")
Write-Host ("Procesos NAP: " + $processes.Count + "; tareas por nombre: " + $tasks.Count + "; servicios por nombre: " + $services.Count + ".")
Write-Host ("Leases SID actual: " + $(if ($null -eq $leaseCount) {'no disponibles'} else {$leaseCount}) +
    "; no vacios: " + $(if ($null -eq $nonEmptyLeaseCount) {'no disponibles'} else {$nonEmptyLeaseCount}) + ".")
Write-Host ("Avisos: " + $warnings.Count + ".")
Write-Host 'CUTOVER: BLOQUEADO. No se ha iniciado produccion ni cambiado datos.'
Write-Host ("Informe guardado: " + $outfile)

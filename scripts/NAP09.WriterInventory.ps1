# NAP09 — Inventario de posibles escritores y padres de rutas; SOLO LECTURA.
# No detiene procesos ni modifica cuentas, tareas, permisos, servicios o archivos NAP.
[CmdletBinding()]
param(
    [switch]$SelfTest,
    [string]$WorkspaceRoot = "$env:USERPROFILE\Documents\NAP\Nimroel",
    [string]$ProductionRoot = "$env:USERPROFILE\Documents\GitHub\Videojuego_Nimroel\Worldbuilding\Direccion artistica\Assets",
    [string]$ArchiveRoot = "$env:USERPROFILE\Documents\Nimroel_Maestros_PNG"
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
function Test-NapWriterReference([string]$value) {
    if ([string]::IsNullOrWhiteSpace($value)) { return $false }
    return [regex]::IsMatch($value, '(?i)(?:NAP\.App(?:\.exe|\.dll)?|NAP\.AutomationHost(?:\.exe|\.dll)?|NAP\.LiveBootstrap(?:\.exe|\.dll)?|[\\/]NAP[\\/]|Nexus\s+Asset\s+Platform)')
}
if ($SelfTest) {
    $cases = @(
        @('C:\Apps\NAP.App.exe', $true),
        @('C:\Apps\NAP.AutomationHost.exe', $true),
        @('dotnet C:\Apps\NAP.AutomationHost.dll', $true),
        @('powershell -File C:\GitHub\NAP\scripts\job.ps1', $true),
        @('C:\Windows\System32\svchost.exe', $false),
        @('C:\Tools\Notepad.exe', $false)
    )
    foreach ($case in $cases) {
        if ((Test-NapWriterReference $case[0]) -ne $case[1]) {
            throw 'NAP09: fallo de clasificacion sintetica de escritores.'
        }
    }
    Write-Host 'NAP09 WriterInventory: 6/6 clasificadores sinteticos PASS.'
    return
}
if ($env:OS -ne 'Windows_NT' -or -not [Environment]::Is64BitProcess) {
    throw 'NAP09 WriterInventory requiere Windows x64.'
}
$warnings = New-Object 'System.Collections.Generic.List[string]'
$processes = @()
$tasks = @()
$services = @()

try {
    $processes = @(Get-CimInstance Win32_Process -ErrorAction Stop | Where-Object {
        (Test-NapWriterReference ([string]$_.Name)) -or
        (Test-NapWriterReference ([string]$_.ExecutablePath)) -or
        (Test-NapWriterReference ([string]$_.CommandLine))
    } | ForEach-Object {
        [ordered]@{ pid=$_.ProcessId; name=$_.Name
            identified_by='process_name_executable_path_or_commandline'
            cmdline_and_environment_not_exported=$true }
    })
} catch {
    [void]$warnings.Add('Inventario de procesos no disponible o incompleto.')
}
try {
    $tasks = @(Get-ScheduledTask -ErrorAction Stop | Where-Object {
        $task = $_; $match = Test-NapWriterReference ([string]$task.TaskName)
        foreach ($action in @($task.Actions)) {
            if ((Test-NapWriterReference ([string]$action.Execute)) -or
                (Test-NapWriterReference ([string]$action.Arguments))) { $match = $true }
        }
        $match
    } | ForEach-Object {
        [ordered]@{ task_name=$_.TaskName; state=[string]$_.State
            path_and_arguments_not_exported=$true }
    })
} catch {
    [void]$warnings.Add('Inventario de tareas no disponible o incompleto.')
}
try {
    $services = @(Get-CimInstance Win32_Service -ErrorAction Stop | Where-Object {
        (Test-NapWriterReference ([string]$_.Name)) -or
        (Test-NapWriterReference ([string]$_.DisplayName)) -or
        (Test-NapWriterReference ([string]$_.PathName))
    } | ForEach-Object {
        [ordered]@{ service_name=$_.Name; state=$_.State; start_mode=$_.StartMode
            binary_path_not_exported=$true }
    })
} catch {
    [void]$warnings.Add('Inventario de servicios no disponible o incompleto.')
}

$parents = New-Object 'System.Collections.Generic.List[object]'
$seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach ($root in @($WorkspaceRoot, $ProductionRoot, $ArchiveRoot,
    (Join-Path $env:LOCALAPPDATA 'Programs\NAP'))) {
    $path = [IO.Path]::GetFullPath($root)
    while (-not [string]::IsNullOrEmpty($path)) {
        if ($seen.Add($path)) {
            $exists = Test-Path -LiteralPath $path -PathType Container
            $reparse = $false; $owner = $null; $protected = $null
            if ($exists) {
                try {
                    $reparse = ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0
                    $acl = Get-Acl -LiteralPath $path -ErrorAction Stop
                    $owner = [string]$acl.Owner
                    $protected = [bool]$acl.AreAccessRulesProtected
                } catch {
                    [void]$warnings.Add('No se pudo inspeccionar un padre de almacenamiento.')
                }
            }
            [void]$parents.Add([ordered]@{
                path=$path; exists=$exists; is_reparse=$reparse; owner=$owner;
                acl_inheritance_disabled=$protected; effective_write_denial_proven=$false
            })
        }
        $next = [IO.Path]::GetDirectoryName($path)
        if ([string]::IsNullOrEmpty($next) -or $next -eq $path) { break }
        $path = $next
    }
}

$outdir = Join-Path $env:USERPROFILE 'Downloads\NAP09_Evidencias'
if (-not (Test-Path -LiteralPath $outdir)) { [void][IO.Directory]::CreateDirectory($outdir) }
if (((Get-Item -LiteralPath $outdir).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw 'No guardar informes en enlaces.'
}
$report = [ordered]@{
    schema_version=1
    phase='NAP09'
    kind='read_only_writer_and_parent_inventory'
    timestamp_utc=[DateTime]::UtcNow.ToString('o')
    nap_processes=@($processes)
    scheduled_tasks_with_nap_reference=@($tasks)
    services_with_nap_reference=@($services)
    roots_and_existing_parents=@($parents.ToArray())
    writer_detection_is_heuristic=$true
    effective_acl_and_old_writer_exclusion_proven=$false
    open_file_handles_inspected=$false
    no_writer_stopped=$true
    no_permissions_modified=$true
    cutover_status='BLOQUEADO'
    warnings=@($warnings.ToArray())
}
$file = Join-Path $outdir ('NAP09_Writers_' + (Get-Date -Format 'yyyyMMdd_HHmmssfff') + '.json')
[IO.File]::WriteAllText($file, ($report | ConvertTo-Json -Depth 7), [Text.UTF8Encoding]::new($false))
Write-Host ('NAP09: procesos candidatos=' + $processes.Count +
    '; tareas candidatas=' + $tasks.Count + '; servicios candidatos=' + $services.Count +
    '; padres inventariados=' + $parents.Count)
Write-Host ('Informe: ' + $file)
Write-Host 'Sin operaciones productivas; CUTOVER BLOQUEADO.'

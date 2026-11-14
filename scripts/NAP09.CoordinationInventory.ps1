# NAP09 — Inventario forense NO destructivo de leases v2 del SID actual.
# Compatible con Windows PowerShell 5.1. No abre locks para escritura.
# No borra, vacia, renombra, desbloquea ni toma posesion de ningun lease.
[CmdletBinding()]
param(
    [string]$CoordinationRoot,
    [string]$OutputDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($env:OS -ne 'Windows_NT' -or -not [Environment]::Is64BitProcess) {
    throw 'Requiere Windows PowerShell x64.'
}
if ([string]::IsNullOrWhiteSpace($CoordinationRoot)) {
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $CoordinationRoot = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)) ('NAP-coordination-v2-' + $sid)
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $env:USERPROFILE 'Downloads\NAP09_Evidencias'
}
$CoordinationRoot = [IO.Path]::GetFullPath($CoordinationRoot)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)

if (-not [IO.Directory]::Exists($CoordinationRoot)) {
    throw 'El directorio de coordinacion indicado no existe. Sin cambios.'
}
if (([IO.File]::GetAttributes($CoordinationRoot) -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw 'No se examinan directorios de coordinacion enlazados.'
}

$scopeCounts = @{}
$anomalies = New-Object 'System.Collections.Generic.List[object]'
$nonEmpty = New-Object 'System.Collections.Generic.List[object]'
$count = 0
$empty = 0
$nonemptyCount = 0
$badName = 0
$linked = 0
$unreadable = 0
$now = [DateTime]::UtcNow
# Este formato coincide con ExecutionMutex.Name() y ExecutionFileLease.PathFor().
$pattern = '^NAP\.Execution\.([A-Za-z0-9]+)\.([a-fA-F0-9]{64})\.lease$'

foreach ($path in [IO.Directory]::EnumerateFiles($CoordinationRoot, '*.lease', [IO.SearchOption]::TopDirectoryOnly)) {
    $count++
    $item = New-Object IO.FileInfo($path)
    $match = [regex]::Match($item.Name, $pattern)
    $scope = if ($match.Success) { $match.Groups[1].Value } else { '<invalid-name>' }
    if (-not $scopeCounts.ContainsKey($scope)) { $scopeCounts[$scope] = 0 }
    $scopeCounts[$scope]++
    if (-not $match.Success) { $badName++ }

    try {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            $linked++
            [void]$anomalies.Add([ordered]@{ scope=$scope; issue='reparse'; name=$item.Name })
            continue
        }
        $size = [long]$item.Length
        if ($size -eq 0) {
            $empty++
        } else {
            $nonemptyCount++
            # La fecha es orientativa; no demuestra si un lock esta vivo.
            $ageDays = [Math]::Round(($now - $item.LastWriteTimeUtc).TotalDays, 2)
            [void]$nonEmpty.Add([ordered]@{
                scope=$scope
                filename=$item.Name
                bytes=$size
                age_days=$ageDays
                meaning='foreign_or_noncanonical_contents_not_necessarily_active'
            })
        }
    } catch {
        $unreadable++
        [void]$anomalies.Add([ordered]@{ scope=$scope; issue='unreadable'; name=$item.Name })
    }
}
$scopes = @($scopeCounts.GetEnumerator() | Sort-Object Name | ForEach-Object {
    [ordered]@{ scope=[string]$_.Key; count=[int]$_.Value }
})
$report = [ordered]@{
    schema_version=1
    phase='NAP09'
    kind='read_only_coordination_inventory'
    timestamp_utc=$now.ToString('o')
    path=$CoordinationRoot
    files_total=$count
    empty_leases=$empty
    nonempty_leases=$nonemptyCount
    linked_entries=$linked
    unreadable_entries=$unreadable
    invalid_names=$badName
    counts_by_scope=$scopes
    nonempty_details=@($nonEmpty.ToArray())
    structural_anomalies=@($anomalies.ToArray())
    current_or_other_writer_activity_confirmed=$false
    production_mode_enabled=$false
    writes_to_coordination=$false
    safe_to_delete_or_repair=$false
    cutover_status='BLOQUEADO'
}
if ([IO.Directory]::Exists($OutputDirectory)) {
    if (([IO.File]::GetAttributes($OutputDirectory) -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'La carpeta destino del informe es un enlace.'
    }
} else {
    [void][IO.Directory]::CreateDirectory($OutputDirectory)
}
$file = Join-Path $OutputDirectory ('NAP09_Leases_' + (Get-Date -Format 'yyyyMMdd_HHmmssfff') + '.json')
[IO.File]::WriteAllText($file, ($report | ConvertTo-Json -Depth 7), [Text.UTF8Encoding]::new($false))
Write-Host ('NAP09: total=' + $count + '; vacios=' + $empty + '; no_vacios=' + $nonemptyCount +
    '; nombres_invalidos=' + $badName + '; enlaces=' + $linked + '; inaccesibles=' + $unreadable)
Write-Host ('Ambitos distintos: ' + $scopeCounts.Count)
Write-Host ('Informe: ' + $file)
Write-Host 'CUTOVER BLOQUEADO; ningun lease modificado.'

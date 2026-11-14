[CmdletBinding()]
param([Parameter(Mandatory)][string]$PreviousSetup, [Parameter(Mandatory)][string]$NewSetup)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or -not $IsWindows) { throw 'Esta prueba instala solo en el runner Windows de GitHub.' }
$installed = Join-Path $env:LOCALAPPDATA 'Programs/NAP'
$data = Join-Path $env:LOCALAPPDATA 'NAP'
$fixture = Join-Path $env:LOCALAPPDATA ('NAP-upgrade-check-' + [guid]::NewGuid().ToString('N'))
if ((Test-Path -LiteralPath $installed) -or (Test-Path -LiteralPath $data)) {
    throw 'El runner ya tiene una instalación o datos de NAP.'
}

function Install([string]$setup, [string]$version, [string]$expected) {
    $actual = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash
    if ($actual -ine $expected) { throw "SHA-256 inesperado: $version" }
    $log = Join-Path $fixture "setup-$version.log"
    $process = Start-Process -FilePath $setup -WindowStyle Hidden -ArgumentList @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$log`"") -PassThru
    if (-not $process.WaitForExit(90000)) {
        throw "El instalador $version no terminó. Revisa $log."
    }
    if ($process.ExitCode -ne 0) {
        Get-Content -LiteralPath $log -Tail 30
        throw "Instalación $version falló ($($process.ExitCode))."
    }
    foreach ($file in @('NAP.App.exe', 'host/NAP.AutomationHost.exe')) {
        $product = (Get-Item -LiteralPath (Join-Path $installed $file)).VersionInfo.ProductVersion
        if ($product.Split('+')[0] -ne $version) { throw "Versión errónea de $file" }
    }
}

New-Item -ItemType Directory -Path $fixture | Out-Null
Install $PreviousSetup '2.0.6' '4c728e55f2252c8dd0a41f2262f59abc40e94f6556d6fd6d210dd4ac331fb1d9'

$workspace = Join-Path $fixture 'workspace'
$production = Join-Path $fixture 'production'
$archive = Join-Path $fixture 'archive'
$mixed = Join-Path $fixture 'entrada'
foreach ($path in @($data, $workspace, $production, $archive, $mixed)) {
    New-Item -ItemType Directory -Path $path | Out-Null
}
$settingsPath = Join-Path $data 'settings.json'
@{ schema_version = 1; universes = @(@{
    universe_id = 'nimroel'; workspace_root = $workspace; production_root = $production; archive_root = $archive
}) } | ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8 -LiteralPath $settingsPath
$configure = Join-Path $installed 'updater/NAP09.ActivarHostPersonal.ps1'
& $configure -Aplicar -BandejaComun $mixed
$config = Join-Path $data 'Host/launch.json'
$keep = Join-Path $data 'upgrade-keep.txt'
Set-Content -Encoding utf8 -LiteralPath $keep -Value 'NAP synthetic user data'
$hashes = Get-FileHash -LiteralPath $settingsPath, $config, $keep

# Reproduce the installed Host holding clrjit.dll after the desktop closes.
$hostExe = Join-Path $installed 'host/NAP.AutomationHost.exe'
$stdout = Join-Path $fixture 'host.out'
$stderr = Join-Path $fixture 'host.err'
$runningHost = Start-Process -FilePath $hostExe -WindowStyle Hidden -ArgumentList @('--personal-config', "`"$config`"") `
    -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
$deadline = (Get-Date).AddSeconds(20)
do {
    if ($runningHost.HasExited) { throw 'El Host 2.0.6 salió antes de la actualización.' }
    $ready = (Get-Content -LiteralPath $stdout -Raw -ErrorAction SilentlyContinue) -match 'NAP personal host ready:'
    if (-not $ready) { Start-Sleep -Milliseconds 200 }
} until ($ready -or (Get-Date) -ge $deadline)
if (-not $ready) { throw 'El Host de referencia no inició.' }
if ('clrjit.dll' -notin $runningHost.Modules.ModuleName) { throw 'El Host de referencia no cargó clrjit.dll.' }

$newHash = ((Get-Content -LiteralPath "$NewSetup.sha256" -Raw).Trim() -split '\s+')[0]
Install $NewSetup '2.0.7' $newHash
if (-not $runningHost.WaitForExit(5000)) { throw 'Restart Manager no cerró el Host 2.0.6.' }
foreach ($entry in $hashes) {
    if ((Get-FileHash -LiteralPath $entry.Path).Hash -ne $entry.Hash) { throw 'Cambió un archivo de datos del usuario.' }
}
$uninstall = Get-ItemProperty -LiteralPath 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/{B2F9E5B8-9D72-4DB2-964E-7E3D3E3F5F68}_is1'
if ($uninstall.DisplayVersion -ne '2.0.7') { throw 'No se registró la actualización per-user.' }
@{ result = 'PASS'; from = '2.0.6'; to = '2.0.7'; oldHostClosed = $true; userDataUnchanged = $true; setupSha256 = $newHash } |
    ConvertTo-Json | Set-Content -Encoding utf8 -LiteralPath (Join-Path (Split-Path (Split-Path $NewSetup -Parent) -Parent) 'upgrade-result.json')
Write-Host 'PASS: 2.0.6 a 2.0.7, Host detenido por Restart Manager, configuración intacta.'

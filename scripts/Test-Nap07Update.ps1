# NAP07: CI Windows hosted TEMP, dos instaladores sinteticos, sin Release real, sin CUTOVER.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OldWork,[Parameter(Mandatory)][string]$NewWork)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or -not $IsWindows) { throw 'Solo runner temporal Windows GitHub Actions.' }
$oldExe=Join-Path $OldWork 'output/NAP-Setup-2.0.0.exe'
$newExe=Join-Path $NewWork 'output/NAP-Setup-2.0.1.exe'
$oldSha=(Get-FileHash -LiteralPath $oldExe -Algorithm SHA256).Hash.ToLowerInvariant()
$newSha=(Get-FileHash -LiteralPath $newExe -Algorithm SHA256).Hash.ToLowerInvariant()
$script=Join-Path $PSScriptRoot 'NAP07.Update.ps1'
$installed=Join-Path $env:LOCALAPPDATA 'Programs/NAP'
$userData=Join-Path $env:LOCALAPPDATA 'NAP'
$marker=Join-Path $userData 'nap07-keep.txt'
$updateRoot=Join-Path $userData 'Updates'
if ((Test-Path -LiteralPath $installed) -or (Test-Path -LiteralPath $userData)) {
    throw 'El runner no esta limpio: se rechaza tocar carpetas preexistentes.'
}
function Must-Fail([scriptblock]$block,[string]$reason) {
    $didFail=$false
    try { & $block } catch { $didFail=$true; Write-Host "EXPECTED FAIL [$reason]: $($_.Exception.Message)" }
    if (-not $didFail) { throw "NAP07 no bloqueo $reason" }
}
function Install([string]$path) {
    $p=Start-Process -FilePath $path -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOCLOSEAPPLICATIONS') -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "Inno fixture fallo $($p.ExitCode)" }
}
function Check([string]$version) {
    $app=Join-Path $installed 'NAP.App.exe'
    $hostExe=Join-Path $installed 'host/NAP.AutomationHost.exe'
    if (-not (Test-Path -LiteralPath $app) -or -not (Test-Path -LiteralPath $hostExe)) {
        throw 'Faltan binarios instalados.'
    }
    $a=[Diagnostics.FileVersionInfo]::GetVersionInfo($app).ProductVersion
    $h=[Diagnostics.FileVersionInfo]::GetVersionInfo($hostExe).ProductVersion
    if ($a -notmatch "^$([regex]::Escape($version))(\.|$)" -or $h -notmatch "^$([regex]::Escape($version))(\.|$)") {
        throw "Versiones no coinciden con $version, App=$a, Host=$h"
    }
    if ([IO.File]::ReadAllText($marker) -cne 'NAP07 USER DATA NEVER MODIFIED') {
        throw 'El marcador de datos de usuario se altero.'
    }
}
[IO.Directory]::CreateDirectory($userData) | Out-Null
[IO.File]::WriteAllText($marker,'NAP07 USER DATA NEVER MODIFIED')
try {
    Install $oldExe
    Check '2.0.0'
    # Alteracion de bytes antes del pin: debe fallar, incluso con permiso unsigned.
    $tampered=Join-Path $env:RUNNER_TEMP 'NAP-Setup-2.0.1-corrupt.exe'
    Copy-Item -LiteralPath $newExe -Destination $tampered
    $handle=[IO.File]::Open($tampered,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    try { $handle.Position=1024; $b=$handle.ReadByte(); $handle.Position=1024; $handle.WriteByte([byte]($b -bxor 0x3F)) }
    finally { $handle.Dispose() }
    Must-Fail { & $script -Action Stage -Version 2.0.1 -TrustedSha256 $newSha -AllowUnsigned -FixtureSetup $tampered } 'descarga manipulada'
    Check '2.0.0'
    Must-Fail { & $script -Action Stage -Version 2.0.1 -TrustedSha256 $newSha -AllowUnsigned -FixtureSetup $newExe -Fault DiskFull } 'disco lleno'
    Check '2.0.0'
    & $script -Action Stage -Version 2.0.1 -TrustedSha256 $newSha -AllowUnsigned -FixtureSetup $newExe
    $external=Join-Path $updateRoot 'tools/NAP07.Update.ps1'
    if (-not (Test-Path -LiteralPath $external)) { throw 'No se creo copia externa del actualizador.' }
    Must-Fail { & $external -Action Apply -Version 2.0.1 -TrustedSha256 $newSha -PreviousSetup $oldExe -PreviousSha256 $oldSha -AllowUnsigned -Confirm 'NO' } 'sin confirmacion'
    Check '2.0.0'
    Must-Fail { & $external -Action Apply -Version 2.0.1 -TrustedSha256 $newSha -PreviousSetup $oldExe -PreviousSha256 $oldSha -AllowUnsigned -Confirm 'APLICAR-2.0.1' -Fault BeforeSetup } 'fallo antes de instalacion'
    & $external -Action Recover -AllowUnsigned -Confirm 'RECUPERAR-2.0.0'
    Check '2.0.0'
    Must-Fail { & $external -Action Apply -Version 2.0.1 -TrustedSha256 $newSha -PreviousSetup $oldExe -PreviousSha256 $oldSha -AllowUnsigned -Confirm 'APLICAR-2.0.1' -Fault AfterSetup } 'fallo tras instalar: reversión automatica'
    Check '2.0.0'
    $tx=Get-Content -LiteralPath (Join-Path $updateRoot 'transaction.json') -Raw | ConvertFrom-Json
    if ($tx.State -ne 'rolled_back') { throw "Rollback no quedo documentado: $($tx.State)" }
    & $external -Action Apply -Version 2.0.1 -TrustedSha256 $newSha -PreviousSetup $oldExe -PreviousSha256 $oldSha -AllowUnsigned -Confirm 'APLICAR-2.0.1'
    Check '2.0.1'
    Must-Fail { & $external -Action Rollback -AllowUnsigned -Confirm 'REVERTIR-2.0.0' } 'rollback sin reconocimiento de schemas'
    & $external -Action Rollback -AllowUnsigned -AcknowledgeSchemaRisk -Confirm 'REVERTIR-2.0.0'
    Check '2.0.0'
    [IO.File]::WriteAllText((Join-Path $NewWork 'nap07-test-results.json'),
        ([ordered]@{result='PASS'; scope='Windows 2022 / TEMP / offline fixture, NO real Releases';
        checks=@('N install','altered download rejected','disk full rejected','stage + independent SHA',
        'no-confirmation rejected','pre-install crash recoverable','after-install rollback',
        'N+1 verified App/Host','schema acknowledgement gate','manual rollback','user data preserved');
        old_sha256=$oldSha; new_sha256=$newSha} | ConvertTo-Json -Depth 5))
    Write-Host 'NAP07 ALL FOCUSED LAB TESTS PASS'
}
finally {
    # No se destruyen datos fuera del marcador sintentico ni se intenta limpiar
    # si existen operaciones interrumpidas: el runner se desecha tras el job.
    if (Test-Path -LiteralPath $marker) { Remove-Item -LiteralPath $marker -Force }
}

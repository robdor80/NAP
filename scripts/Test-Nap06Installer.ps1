[CmdletBinding()]
param([ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(\.[0-9]+)?$')][string]$Version='2.0.0')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows -or $env:GITHUB_ACTIONS -ne 'true') { throw 'Prueba NAP06 exclusivamente en runner Windows GitHub aislado.' }
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$work = Join-Path $repo 'artifacts/nap06'
$setup = Join-Path $work "output/NAP-Setup-$Version.exe"
$installed = Join-Path $env:LOCALAPPDATA 'Programs/NAP'
$userData = Join-Path $env:LOCALAPPDATA 'NAP'
$marker = Join-Path $userData 'nap06-ci-preserve-marker.txt'
if ((Test-Path -LiteralPath $installed) -or (Test-Path -LiteralPath $userData)) {
    throw 'El runner no esta limpio. Se rechaza tocar instalaciones o datos preexistentes.'
}
$inventory = Get-Content -LiteralPath (Join-Path $work 'inventory.json') -Raw | ConvertFrom-Json
if ($inventory.version -ne $Version -or $inventory.target -ne 'win-x64' -or $inventory.mode -ne 'self-contained') {
    throw 'Inventario no coincide con el paquete.'
}
foreach ($record in $inventory.files) {
    $path = Join-Path $work ('payload/' + $record.path)
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $record.sha256) {
        throw "Hash del payload no coincide: $($record.path)"
    }
}
$expected = (Get-Content -LiteralPath "$setup.sha256" -Raw).Trim().Split(' ')[0]
if ((Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) {
    throw 'Hash del instalador no coincide.'
}
& (Join-Path $work 'payload/host/NAP.AutomationHost.exe') --unconfigured
if ($LASTEXITCODE -ne 2) { throw 'El Host no rechazo argumentos sin configuracion.' }

[System.IO.Directory]::CreateDirectory($userData) | Out-Null
[System.IO.File]::WriteAllText($marker, 'NAP06: los datos del usuario deben sobrevivir')
try {
    # An explicit /DIR redirect must never co-locate binaries with personal data.
    $refused = Start-Process -FilePath $setup -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES',
        '/NORESTART','/NOCLOSEAPPLICATIONS',"/DIR=$userData") -Wait -PassThru
    if ($refused.ExitCode -eq 0) { throw 'La instalacion acepto /DIR hacia datos personales.' }
    if (-not (Test-Path -LiteralPath $marker)) { throw 'Rechazo de /DIR altero los datos.' }
    $p = Start-Process -FilePath $setup -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOCLOSEAPPLICATIONS') -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "Instalacion aislada fallo: $($p.ExitCode)" }
    foreach ($relative in @('NAP.App.exe','host/NAP.AutomationHost.exe','config/universes/nimroel/profile.json')) {
        if (-not (Test-Path -LiteralPath (Join-Path $installed $relative) -PathType Leaf)) { throw "Falta: $relative" }
    }
    $previewOut = Join-Path $work 'preview'
    $app = Join-Path $installed 'NAP.App.exe'
    $previewStdout = Join-Path $work 'preview-stdout.txt'
    $previewStderr = Join-Path $work 'preview-stderr.txt'
    $smoke = Start-Process -FilePath $app -ArgumentList @('--phase6-preview=active',
        "--phase6-output=$previewOut") -RedirectStandardOutput $previewStdout -RedirectStandardError $previewStderr -Wait -PassThru
    $screenshots = @(Get-ChildItem -LiteralPath $previewOut -Filter '*.png' -ErrorAction SilentlyContinue)
    Write-Host "NAP06 WPF diagnostic: exit=$($smoke.ExitCode), PNG=$($screenshots.Count)"
    if ($smoke.ExitCode -ne 0 -or $screenshots.Count -eq 0) {
        $errorText = if (Test-Path -LiteralPath $previewStderr) { Get-Content -LiteralPath $previewStderr -Raw } else { '' }
        throw "La app WPF instalada no supero la vista previa sintetica. Exit=$($smoke.ExitCode); detalle=$errorText"
    }
    $uninstall = Join-Path $installed 'unins000.exe'
    if (-not (Test-Path -LiteralPath $uninstall)) { throw 'No existe desinstalador.' }
    $u = Start-Process -FilePath $uninstall -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -Wait -PassThru
    if ($u.ExitCode -ne 0) { throw "Desinstalacion fallida: $($u.ExitCode)" }
    if (-not (Test-Path -LiteralPath $marker -PathType Leaf) -or
        [System.IO.File]::ReadAllText($marker) -ne 'NAP06: los datos del usuario deben sobrevivir') {
        throw 'La desinstalacion altero los datos del usuario.'
    }
    if (Test-Path -LiteralPath (Join-Path $installed 'NAP.App.exe')) {
        throw 'La desinstalacion dejo binarios NAP.App instalados.'
    }
    [System.IO.File]::WriteAllText((Join-Path $work 'test-results.json'),
        ([ordered]@{ result = 'PASS'; context = 'GitHub-hosted Windows 2022 TEMP user';
         checks = @('payload SHA256','setup SHA256','Host refusal without config',
          'reject unsafe directory override','per-user install','WPF synthetic preview','uninstall','user data preservation') } | ConvertTo-Json))
    Write-Host 'NAP06 lab install/uninstall and data preservation: PASS'
}
finally {
    if (Test-Path -LiteralPath $marker) { Remove-Item -LiteralPath $marker -Force }
}

[CmdletBinding()]
param(
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(\.[0-9]+)?$')]
    [string]$Version = '2.0.0',
    [Parameter(Mandatory)][string]$InnoCompiler
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'NAP06 requiere Windows x64.' }
if ($env:PROCESSOR_ARCHITECTURE -ne 'AMD64') { throw 'NAP06 requiere un proceso Windows x64.' }
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$compiler = (Resolve-Path -LiteralPath $InnoCompiler).Path
$work = Join-Path $repo 'artifacts/nap06'
if (Test-Path -LiteralPath $work) { throw 'El directorio de trabajo de NAP06 ya existe. Use un checkout limpio para evitar artefactos antiguos.' }
$payload = Join-Path $work 'payload'
$hostStage = Join-Path $work 'host-staging'
$output = Join-Path $work 'output'
foreach ($dir in @($payload, $hostStage, $output)) { [System.IO.Directory]::CreateDirectory($dir) | Out-Null }
$icon = Join-Path $work 'NAP.ico'
& (Join-Path $PSScriptRoot 'New-Nap06Icon.ps1') -SourcePng (Join-Path $repo 'src/NAP.App/Assets/RoDo/rodo_v0_1.png') -DestinationIco $icon

function Invoke-BuildCommand([string[]]$Arguments) {
    if ($env:GITHUB_ACTIONS -eq 'true') {
        & (Join-Path $repo '.github/scripts/Invoke-LicensedDotnet.ps1') -DotnetArguments $Arguments
    } else {
        & dotnet @Arguments
    }
    if ($LASTEXITCODE -ne 0) { throw 'Fallo el comando .NET de publicacion autorizada.' }
}
# La rutina de licencia borra la variable del entorno tras cada invocacion.
# Conservamos la cadena solo en memoria local para el segundo publish, sin logs ni archivos.
$licenseForSecondPublish = if ($env:GITHUB_ACTIONS -eq 'true') { $env:SIXLABORS_LICENSE_B64 } else { $null }
Push-Location $repo
try {
    & dotnet restore 'src/NAP.App/NAP.App.csproj' -r win-x64 --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw 'Fallo restauracion WPF win-x64.' }
    & dotnet restore 'src/NAP.AutomationHost/NAP.AutomationHost.csproj' -r win-x64 --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw 'Fallo restauracion Host win-x64.' }
    Invoke-BuildCommand -Arguments @('publish','src/NAP.App/NAP.App.csproj','-c','Release','-r','win-x64','--self-contained','true','--no-restore','-o',$payload,'-p:NapReleaseBundle=true',"-p:Version=$Version",'-p:PublishSingleFile=false','-p:PublishReadyToRun=false','-p:DebugType=None','-p:DebugSymbols=false',"-p:ApplicationIcon=$icon")
    if ($env:GITHUB_ACTIONS -eq 'true') { $env:SIXLABORS_LICENSE_B64 = $licenseForSecondPublish }
    Invoke-BuildCommand -Arguments @('publish','src/NAP.AutomationHost/NAP.AutomationHost.csproj','-c','Release','-r','win-x64','--self-contained','true','--no-restore','-o',$hostStage,'-p:NapReleaseBundle=true',"-p:Version=$Version",'-p:PublishSingleFile=false','-p:PublishReadyToRun=false','-p:DebugType=None','-p:DebugSymbols=false')
}
finally {
    Remove-Item Env:SIXLABORS_LICENSE_B64 -ErrorAction SilentlyContinue
    $licenseForSecondPublish = $null
    Pop-Location
}

$updaterDirectory = Join-Path $payload 'updater'
[System.IO.Directory]::CreateDirectory($updaterDirectory) | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'scripts/NAP07.Update.ps1') -Destination (Join-Path $updaterDirectory 'NAP07.Update.ps1')
$installedHost = Join-Path $payload 'host'
[System.IO.Directory]::CreateDirectory($installedHost) | Out-Null
Copy-Item -Path (Join-Path $hostStage '*') -Destination $installedHost -Recurse -Force
foreach ($file in @('NAP.AutomationHost.exe','NAP.AutomationHost.dll','NAP.AutomationHost.deps.json','NAP.AutomationHost.runtimeconfig.json')) {
    $orphan = Join-Path $payload $file
    if (Test-Path -LiteralPath $orphan) { Remove-Item -LiteralPath $orphan -Force }
}
foreach ($relative in @('NAP.App.exe','NAP.App.dll','NAP.App.runtimeconfig.json',
    'config/universes/nimroel/profile.json','host/NAP.AutomationHost.exe',
    'host/NAP.AutomationHost.dll','host/NAP.AutomationHost.runtimeconfig.json',
    'updater/NAP07.Update.ps1')) {
    if (-not (Test-Path -LiteralPath (Join-Path $payload $relative) -PathType Leaf)) {
        throw "Falta componente obligatorio del paquete: $relative"
    }
}
$files = @(Get-ChildItem -LiteralPath $payload -File -Recurse)
if ($files.Count -lt 20) { throw 'El paquete parece incompleto.' }
foreach ($f in $files) {
    if ($f.Name -match '(?i)(^sixlabors\.lic($|\.)|\.lic$|\.pfx$|\.p12$|\.pem$|\.key$|\.pdb$|\.secret$|^launch\.json$|^settings\.json$|^LegacyPortraits\.db$|^AssetCatalog\.db$)') {
        throw "Se detecto un archivo prohibido en el payload: $($f.Name)"
    }
}
$inventory = @($files | ForEach-Object {
    [ordered]@{
        path = [System.IO.Path]::GetRelativePath($payload, $_.FullName).Replace('\','/')
        bytes = $_.Length
        sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
} | Sort-Object { $_.path })
$manifest = [ordered]@{
    schema_version = 1
    app = 'Nexus Asset Platform'
    version = $Version
    target = 'win-x64'
    mode = 'self-contained'
    data_policy = 'No data stored or removed under the installation directory'
    unsigned = $true
    count = $files.Count
    files = $inventory
}
$manifestPath = Join-Path $work 'inventory.json'
[System.IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 6))
& $compiler "/DAppVersion=$Version" "/DPayloadDir=$payload" "/DReleaseOutput=$output" "/DAppIcon=$icon" (Join-Path $repo 'installer/NAP.iss')
if ($LASTEXITCODE -ne 0) { throw 'ISCC no pudo compilar el instalador NAP06.' }
$setup = Join-Path $output "NAP-Setup-$Version.exe"
if (-not (Test-Path -LiteralPath $setup -PathType Leaf)) { throw 'ISCC no genero el instalador esperado.' }
$setupHash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
[System.IO.File]::WriteAllText("$setup.sha256", ($setupHash + '  ' + (Split-Path -Leaf $setup) + [Environment]::NewLine))
[System.IO.File]::WriteAllText((Join-Path $work 'provenance.json'), (([ordered]@{
    schema_version = 1; version = $Version; source_commit = $env:GITHUB_SHA
    target = 'windows-2022/win-x64'; build = 'dotnet 10 self-contained WPF and Host'
    inno_compiler = $compiler; setup_file = (Split-Path -Leaf $setup)
    setup_sha256 = $setupHash; signing = 'unsigned (no code-signing certificate)'
}) | ConvertTo-Json))
Write-Host "NAP06 package created: $(Split-Path -Leaf $setup)"
Write-Host "NAP06 SHA256: $setupHash"

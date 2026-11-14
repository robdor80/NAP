# NAP09 protected binary distribution: prepare installable CABINET only, NEVER CUTOVER.
# Calls the existing licensed NAP06 release pipeline without altering the user's MSI.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$InnoCompiler,
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(\.[0-9]+)?$')]
    [string]$Version = '2.0.3'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows -or $env:PROCESSOR_ARCHITECTURE -ne 'AMD64') {
    throw 'NAP09 requires an isolated Windows x64 publishing runner.'
}
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$iscc = (Resolve-Path -LiteralPath $InnoCompiler).Path
$build = Join-Path $PSScriptRoot 'Build-Nap06Release.ps1'
if (-not (Test-Path -LiteralPath $build -PathType Leaf)) { throw 'NAP06 build entry is missing.' }
$work = Join-Path $repo 'artifacts\nap06'
if (Test-Path -LiteralPath $work) {
    throw 'NAP09 refuses reusing an existing payload: run in a clean checkout.'
}

# Explicitly preserve the NAP06 per-user package build semantics.
& $build -InnoCompiler $iscc -Version $Version
if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw 'NAP06 pipeline did not complete.' }

$payload = Join-Path $work 'payload'
$inventory = Join-Path $work 'inventory.json'
$icon = Join-Path $work 'NAP.ico'
$output = Join-Path $work 'output'
$expected = Join-Path $output ('NAP-Setup-' + $Version + '.exe')
foreach ($item in @($payload, $inventory, $icon, $expected)) {
    if (-not (Test-Path -LiteralPath $item)) { throw 'NAP09 requires a complete NAP06 build and inventory.' }
}
$manifest = Get-Content -LiteralPath $inventory -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.schema_version -ne 1 -or $manifest.app -ne 'Nexus Asset Platform' -or
    $manifest.target -ne 'win-x64' -or $manifest.mode -ne 'self-contained' -or
    $manifest.count -lt 20 -or $manifest.files.Count -ne $manifest.count) {
    throw 'NAP06 inventory has invalid headers or counts.'
}
$files = @(Get-ChildItem -LiteralPath $payload -File -Recurse)
if ($files.Count -ne $manifest.count) { throw 'Payload differs from inventory count.' }
$root = [IO.Path]::GetFullPath($payload)
$paths = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach ($item in @($manifest.files)) {
    $relative = [string]$item.path
    if ([string]::IsNullOrWhiteSpace($relative) -or $relative.StartsWith('/') -or
        $relative.Contains('\') -or $relative.Contains(':') -or
        @($relative.Split('/')) -contains '..' -or @($relative.Split('/')) -contains '.') {
        throw 'Unsafe relative path in NAP06 manifest.'
    }
    $absolute = [IO.Path]::GetFullPath((Join-Path $root ($relative.Replace('/', [IO.Path]::DirectorySeparatorChar))))
    if (-not $absolute.StartsWith(($root.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar), [StringComparison]::OrdinalIgnoreCase) -or
        -not $paths.Add($absolute)) { throw 'Duplicate or out-of-bounds inventory entry.' }
    $info = Get-Item -LiteralPath $absolute -ErrorAction Stop
    if ($info.Length -ne [long]$item.bytes -or
        (Get-FileHash -LiteralPath $absolute -Algorithm SHA256).Hash.ToLowerInvariant() -cne [string]$item.sha256) {
        throw 'Payload hash differs from signed-off inventory.'
    }
}
foreach ($file in $files) {
    if (-not $paths.Contains([IO.Path]::GetFullPath($file.FullName))) {
        throw 'Unlisted payload file.'
    }
}
$compilerFlags = @(
    "/DAppVersion=$Version",
    "/DPayloadDir=$payload",
    "/DReleaseOutput=$output",
    "/DAppIcon=$icon",
    "/DInventoryPath=$inventory",
    (Join-Path $repo 'installer\NAP09.Protected.iss')
)
& $iscc @compilerFlags
if ($LASTEXITCODE -ne 0) { throw 'Protected installer compilation failed.' }
$protected = Join-Path $output ('NAP-ProtectedSetup-' + $Version + '.exe')
if (-not (Test-Path -LiteralPath $protected -PathType Leaf)) { throw 'Protected installer was not created.' }
$sha = (Get-FileHash -LiteralPath $protected -Algorithm SHA256).Hash.ToLowerInvariant()
$inventorySha = (Get-FileHash -LiteralPath $inventory -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText(($protected + '.sha256'), ($sha + '  ' + [IO.Path]::GetFileName($protected) + [Environment]::NewLine),
    [Text.UTF8Encoding]::new($false))
$report = [ordered]@{
    schema_version = 1; phase = 'NAP09'; target = 'Windows x64'
    artifact = [IO.Path]::GetFileName($protected); artifact_sha256 = $sha
    inventory_file = 'production-inventory.json'; inventory_sha256 = $inventorySha
    payload_files = $manifest.count
    installation_mode = 'admin_only_program_files_binaries_only'
    service_installed = $false; writer_created = $false
    application_data_migrated = $false; cutover_executed = $false
    signer = 'unsigned'
}
[IO.File]::WriteAllText((Join-Path $work 'nap09-protected-provenance.json'),
    ($report | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
Write-Host ('NAP09 protected installer built: ' + $protected)
Write-Host ('SHA256: ' + $sha)
Write-Host 'No accounts, services, ACLs, catalogs, real installation or CUTOVER were changed.'

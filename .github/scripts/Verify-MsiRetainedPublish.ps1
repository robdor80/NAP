$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:GITHUB_REF -ne 'refs/heads/codex/nap-2-fase-6-ux') { throw 'Disposable authorized runner required.' }
$publish = Join-Path $env:RUNNER_TEMP 'nap-retained-publish'
$evidence = Join-Path $PWD 'artifacts/msi-preparation'
New-Item -ItemType Directory -Force $evidence | Out-Null
$fixtureTemp = Join-Path $env:RUNNER_TEMP 'nap-retained-fixtures'
New-Item -ItemType Directory -Force $fixtureTemp | Out-Null
$env:TEMP = $fixtureTemp; $env:TMP = $fixtureTemp
$env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $fixtureTemp 'bundles'
foreach ($name in @('SIXLABORS_LICENSE_B64','SIXLABORS_LICENSE_KEY','SixLaborsLicenseFile','SixLaborsLicenseKey')) {
    if ([Environment]::GetEnvironmentVariable($name)) { throw 'License variable survived cleanup.' }
}
if (Test-Path (Join-Path $env:RUNNER_TEMP 'nap-sixlabors-license')) { throw 'Temporary license survived cleanup.' }
$required = @('NAP.App.exe','NAP.App.dll','NAP.Core.dll','NAP.Presentation.dll','NAP.AI.dll','NAP.App.deps.json','NAP.App.runtimeconfig.json','SixLabors.ImageSharp.dll','Microsoft.Data.Sqlite.dll','SQLitePCLRaw.core.dll','e_sqlite3.dll','coreclr.dll','hostfxr.dll','hostpolicy.dll','System.Private.CoreLib.dll','PresentationFramework.dll','PresentationCore.dll','wpfgfx_cor3.dll','D3DCompiler_47_cor3.dll','config/universes/nimroel/profile.json')
foreach ($file in $required) { if (-not (Test-Path (Join-Path $publish $file) -PathType Leaf)) { throw "Required dependency missing: $file" } }
$runtime = Get-Content (Join-Path $publish 'NAP.App.runtimeconfig.json') -Raw | ConvertFrom-Json
if (-not $runtime.runtimeOptions.includedFrameworks -or $runtime.runtimeOptions.framework -or $runtime.runtimeOptions.frameworks) { throw 'Self-contained runtime contract missing.' }
$exeBytes = [IO.File]::ReadAllBytes((Join-Path $publish 'NAP.App.exe'))
$pe = [BitConverter]::ToInt32($exeBytes, 0x3c)
if ([BitConverter]::ToUInt16($exeBytes, $pe + 4) -ne 0x8664) { throw 'Application host is not x64.' }
# Record public package license metadata and notices from exactly the resolved NuGet graph.
$assets = Get-Content src/NAP.App/obj/project.assets.json -Raw | ConvertFrom-Json -AsHashtable
$packageRoot = @($assets.packageFolders.Keys)[0]
$notices = Join-Path $publish 'third-party-notices'
New-Item -ItemType Directory -Force $notices | Out-Null
$licenses = @()
foreach ($name in @($assets.libraries.Keys | Sort-Object)) {
    $library = $assets.libraries[$name]
    if ($library.type -ne 'package') { continue }
    $root = Join-Path $packageRoot $library.path
    $nuspec = @(Get-ChildItem $root -Filter '*.nuspec' -File)[0]
    [xml]$metadata = Get-Content $nuspec.FullName -Raw
    $node = $metadata.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='license']")
    $entry = @{ package=$name; licenseType=if($node){$node.type}else{'unspecified'}; license=if($node){$node.InnerText}else{'review required'} }
    $licenses += $entry
    $destination = Join-Path $notices ($name.Replace('/','-'))
    New-Item -ItemType Directory -Force $destination | Out-Null
    $publicNotices = @(Get-ChildItem $root -File | Where-Object { $_.Name -match '^(LICENSE|NOTICE|THIRD.PARTY)' -and $_.Extension -ne '.lic' })
    foreach ($file in $publicNotices) { Copy-Item $file.FullName $destination }
    if ($node -and $node.type -eq 'file') {
        $file = Join-Path $root $node.InnerText
        if (-not [IO.Path]::GetFullPath($file).StartsWith([IO.Path]::GetFullPath($root) + [IO.Path]::DirectorySeparatorChar) -or [IO.Path]::GetExtension($file) -eq '.lic') { throw 'Unexpected notice path.' }
        Copy-Item $file $destination
    }
}
$licenses | ConvertTo-Json -Depth 8 | Out-File (Join-Path $publish 'third-party-packages.json') -Encoding utf8
# Include application notice without assuming that dependency notices grant a distribution clearance.
if (Test-Path LICENSE) { Copy-Item LICENSE (Join-Path $publish 'NAP-LICENSE.txt') }
@('Retained synthetic candidate: NOT APPROVED FOR MSI EXECUTION OR DISTRIBUTION.',
  'Ordinary startup is not isolated. No private transport from runner to Drive is configured.',
  'No AutomationHost, Git executable, installer, updater or production configuration is bundled.',
  'Third-party inventory is evidence; legal notice completeness requires review before distribution.',
  'NU1902/NU1903 remain visible; security follow-up is issue #49.') | Out-File (Join-Path $publish 'LIMITATIONS.txt') -Encoding utf8
$files = @(Get-ChildItem $publish -File -Recurse)
$deny = @($files | Where-Object { $_.Name -match '(?i)(sixlabors\.lic|license-reminders\.json|settings\.json|\.pdb$|\.pfx$|\.pem$|\.key$|\.lic$|\.env$)' -or $_.FullName -match '[\\/](obj|\.git)[\\/]' })
if ($deny.Count) { throw 'Private or developer file found; no filename/content is emitted.' }
foreach ($file in $files) {
    $bytes = [IO.File]::ReadAllBytes($file.FullName)
    foreach ($encoding in @([Text.Encoding]::UTF8,[Text.Encoding]::Unicode)) {
        $text = $encoding.GetString($bytes)
        if ($text -match '(?i)Id=[^;\r\n]{1,160};Kind=(Community|Commercial);' -or $text -match 'gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,}|-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----') { throw 'Secret-like material detected; contents withheld.' }
    }
}
# Only compare named NAP locations. This is not OS-wide write tracing or proof of MSI containment.
function Snapshot-NapLocations {
    $locations = @((Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'NAP'))
    $locations += @(Get-ChildItem ([Environment]::GetFolderPath('CommonApplicationData')) -Filter 'NAP-coordination-v2-*' -Directory | ForEach-Object FullName)
    $entries = @()
    foreach ($root in $locations) {
        if (Test-Path $root) {
            $entries += @{ location=$root; directory=$true }
            foreach ($file in @(Get-ChildItem $root -File -Recurse)) { $entries += @{ location=$file.FullName; sha256=(Get-FileHash $file.FullName -Algorithm SHA256).Hash; length=$file.Length } }
        }
    }
    return ConvertTo-Json -InputObject $entries -Depth 6 -Compress
}
$before = Snapshot-NapLocations
$probe = & dotnet tools/NAP.PrivatePackageProbe/bin/Release/net8.0/NAP.PrivatePackageProbe.dll $publish
if ($LASTEXITCODE -ne 0) { throw 'Runtime probe failed.' }
$probe | Out-File (Join-Path $evidence 'runtime-probe.json') -Encoding utf8
# Explicit exclusions avoid all persistent register/queue writers, account/ACL work and host activation.
$filter = '(FullyQualifiedName~Phase6OverviewTests&FullyQualifiedName!~AuthenticatedOverview)|(FullyQualifiedName~Phase6WorkflowTests&FullyQualifiedName!~AuthenticatedPaging&FullyQualifiedName!~OneSyntheticAsset&FullyQualifiedName!~RevokedPolicyBlocks)|(FullyQualifiedName~Phase6PolishTests&FullyQualifiedName!~DurableStreams&FullyQualifiedName!~ExtendedReadContract)|FullyQualifiedName~Phase6HistoricalTests|FullyQualifiedName~ThresholdsCatchUpWithoutChangingTheMachineClock|FullyQualifiedName~OfflineReportUsesClosedVocabulary|FullyQualifiedName~HostReadFailuresAndForeignOrLateResponses'
& dotnet test tests/NAP.Tests/NAP.Tests.csproj -c Release --no-build --no-restore --filter $filter --results-directory $evidence --logger 'trx;LogFileName=focused.trx'
if ($LASTEXITCODE -ne 0) { throw 'Focused tests failed.' }
[xml]$trx = Get-Content (Join-Path $evidence 'focused.trx') -Raw
$counters = $trx.SelectSingleNode("//*[local-name()='Counters']")
if ([int]$counters.total -lt 30 -or $counters.total -ne $counters.passed -or [int]$counters.notExecuted -ne 0) { throw 'Tests missing, skipped or unsuccessful.' }
$modes = @('local','catalog','settings','production','history','diagnostics-offline','licenses','licenses-90','licenses-30','licenses-7','licenses-expired','licenses-renewed','historical','historical-export')
foreach ($mode in $modes) {
    $process = Start-Process (Join-Path $publish 'NAP.App.exe') -WorkingDirectory $fixtureTemp -ArgumentList @("--phase6-preview=$mode", "--phase6-output=$evidence", '--compact') -RedirectStandardOutput (Join-Path $evidence "$mode.stdout.txt") -RedirectStandardError (Join-Path $evidence "$mode.stderr.txt") -PassThru
    if (-not $process.WaitForExit(60000)) { $process.Kill(); throw "Synthetic preview timeout: $mode" }
    if ($process.ExitCode -ne 0) { throw "Synthetic preview failed: $mode" }
}
$sidecars = @(Get-ChildItem $evidence -Filter '*.png.json' | ForEach-Object { Get-Content $_.FullName -Raw | ConvertFrom-Json })
if ($sidecars.Count -lt $modes.Count -or @($sidecars | Where-Object bindingErrors -ne 0).Count) { throw 'Missing render or binding errors.' }
$after = Snapshot-NapLocations
if ($before -ne $after) { throw 'A named ordinary NAP location changed.' }
@{ unchangedNamedNapLocations=$true; scope='LocalAppData/NAP and ProgramData/NAP-coordination-v2-* only; not full process I/O tracing'; ordinaryStartupIsolated=$false; msiExecutionAuthorized=$false; modes=$modes; bindingErrors=0; captures=$sidecars.Count; focusedPassed=[int]$counters.passed; noHostStarted=$true; noZipIngestion=$true } | ConvertTo-Json -Depth 8 | Out-File (Join-Path $evidence 'verification.json')
$inventory = @($files | Sort-Object FullName | ForEach-Object { @{ path=[IO.Path]::GetRelativePath($publish,$_.FullName).Replace('\','/'); length=$_.Length; sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() } })
$manifest = @{ application='NAP 2.0'; testVersion="phase6-msi-retained-$env:GITHUB_RUN_ID"; architecture='win-x64'; sourceCommit=(git rev-parse HEAD); generatedUtc=[DateTime]::UtcNow.ToString('o'); githubActionsRun="https://github.com/robdor80/NAP/actions/runs/$env:GITHUB_RUN_ID"; sdk=(& dotnet --version); publication='net8.0-windows self-contained, untrimmed, multiple files'; runtime=$runtime.runtimeOptions.includedFrameworks; requiredFiles=$required; files=$inventory; status='retained verification candidate only; destroyed after run'; privateTransportValidated=$false; ordinaryStartupIsolated=$false; limitations=@('Do not execute on MSI.','No host/Git/installer/updater included.','Notice completeness pending legal review.','NU1902/NU1903 tracked in #49.','No binary package was transferred or approved.') }
$manifest | ConvertTo-Json -Depth 12 | Out-File (Join-Path $evidence 'retained-candidate-manifest.json') -Encoding utf8
$inventory | ForEach-Object { "$($_.sha256)  $($_.path)" } | Out-File (Join-Path $evidence 'retained-candidate.sha256') -Encoding utf8
$licenses | ConvertTo-Json -Depth 8 | Out-File (Join-Path $evidence 'third-party-packages.json')
@{ requiredDependenciesPresent=$true; apphostX64=$true; selfContained=$true; privateFileNameScanPassed=$true; boundedSecretPatternScanPassed=$true; noPdbOrObj=$true; noBinaryUpload=$true; noArchiveCreated=$true; noPostTransferBinaryVerification='not applicable; no transfer'; sourceCommit=(git rev-parse HEAD) } | ConvertTo-Json | Out-File (Join-Path $evidence 'dependency-security-check.json')
git diff --exit-code
if ($LASTEXITCODE -ne 0) { throw 'Source tree changed during verification.' }

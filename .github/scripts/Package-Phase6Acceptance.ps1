$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:GITHUB_REF -ne 'refs/heads/codex/nap-2-fase-6-ux') { throw 'Authorized branch runner required.' }
$publish = Join-Path $env:RUNNER_TEMP 'nap-acceptance-publish'
$evidence = Join-Path $PWD 'artifacts/phase6-evidence'
$delivery = Join-Path $PWD 'artifacts/phase6-delivery'
New-Item -ItemType Directory -Force $evidence,$delivery | Out-Null
foreach ($variable in @('SIXLABORS_LICENSE_B64','SixLaborsLicenseFile','SixLaborsLicenseKey','SIXLABORS_LICENSE_KEY')) {
  if ([Environment]::GetEnvironmentVariable($variable)) { throw 'Private license variable survived cleanup.' }
}
if (Test-Path (Join-Path $env:RUNNER_TEMP 'nap-sixlabors-license')) { throw 'Private license survived cleanup.' }
$required = @('NAP.App.exe','NAP.App.dll','NAP.Core.dll','NAP.Presentation.dll','NAP.AI.dll','NAP.App.deps.json','NAP.App.runtimeconfig.json','SixLabors.ImageSharp.dll','Microsoft.Data.Sqlite.dll','SQLitePCLRaw.core.dll','e_sqlite3.dll','coreclr.dll','hostfxr.dll','hostpolicy.dll','System.Private.CoreLib.dll','PresentationFramework.dll','PresentationCore.dll','wpfgfx_cor3.dll','D3DCompiler_47_cor3.dll','config/universes/nimroel/profile.json')
foreach ($file in $required) { if (-not (Test-Path (Join-Path $publish $file) -PathType Leaf)) { throw "Required dependency missing: $file" } }
$runtime = Get-Content (Join-Path $publish 'NAP.App.runtimeconfig.json') -Raw | ConvertFrom-Json
if (-not $runtime.runtimeOptions.includedFrameworks -or $runtime.runtimeOptions.framework -or $runtime.runtimeOptions.frameworks) { throw 'Runtime is not self-contained.' }
$exe = [IO.File]::ReadAllBytes((Join-Path $publish 'NAP.App.exe'))
if ([BitConverter]::ToUInt16($exe, [BitConverter]::ToInt32($exe,0x3c)+4) -ne 0x8664) { throw 'Apphost is not x64.' }
# The legal gate is closed over the exact runtime NuGet graph. New packages stop publication.
$assets = Get-Content src/NAP.App/obj/project.assets.json -Raw | ConvertFrom-Json -AsHashtable
$mapping = @{
 'Microsoft.Data.Sqlite.Core/8.0.31'='MIT'; 'Microsoft.Data.Sqlite/8.0.31'='MIT';
 'SixLabors.ImageSharp/3.2.0'='Apache-2.0 (existing Community grant; official build validation)';
 'SQLitePCLRaw.bundle_e_sqlite3/2.1.12'='Apache-2.0'; 'SQLitePCLRaw.core/2.1.12'='Apache-2.0';
 'SQLitePCLRaw.lib.e_sqlite3/2.1.12'='Apache-2.0; native SQLite public domain';
 'SQLitePCLRaw.provider.e_sqlite3/2.1.12'='Apache-2.0'; 'System.Memory/4.5.3'='MIT'
}
$inventory = @()
foreach ($name in @($assets.libraries.Keys | Sort-Object)) {
 $library = $assets.libraries[$name]; if ($library.type -ne 'package') { continue }
 if (-not $mapping.ContainsKey($name)) { throw "Unreviewed package: $name" }
 $root = Join-Path @($assets.packageFolders.Keys)[0] $library.path
 [xml]$spec = Get-Content @(Get-ChildItem $root -Filter '*.nuspec' -File)[0].FullName -Raw
 $copyright = $spec.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='copyright']")
 $inventory += @{ package=$name; license=$mapping[$name]; copyright=if($copyright){$copyright.InnerText}else{'See upstream notices'}; redistributed=$true }
}
Copy-Item docs/phase6-public-acceptance/package/* $publish
Copy-Item docs/phase6-public-acceptance/notices (Join-Path $publish 'third-party-notices') -Recurse
$sourceNotices = Get-Content docs/phase6-public-acceptance/notices/sources.json -Raw | ConvertFrom-Json
foreach ($notice in $sourceNotices) {
 if ((Get-FileHash (Join-Path $publish "third-party-notices/$($notice.file)") -Algorithm SHA256).Hash.ToLowerInvariant() -ne $notice.sha256) { throw 'Notice provenance mismatch.' }
}
$inventory | ConvertTo-Json -Depth 6 | Out-File (Join-Path $publish 'third-party-packages.json') -Encoding utf8
# Evidence from exact dependency bytes, with all private licensing material already removed.
& dotnet tools/NAP.PrivatePackageProbe/bin/Release/net8.0/NAP.PrivatePackageProbe.dll $publish | Out-File (Join-Path $evidence 'runtime-probe.json')
if ($LASTEXITCODE -ne 0) { throw 'Dependency runtime probe failed.' }
$filter = '(FullyQualifiedName~Phase6OverviewTests&FullyQualifiedName!~AuthenticatedOverview)|(FullyQualifiedName~Phase6WorkflowTests&FullyQualifiedName!~AuthenticatedPaging&FullyQualifiedName!~OneSyntheticAsset&FullyQualifiedName!~RevokedPolicyBlocks)|(FullyQualifiedName~Phase6PolishTests&FullyQualifiedName!~DurableStreams&FullyQualifiedName!~ExtendedReadContract)|FullyQualifiedName~Phase6HistoricalTests|FullyQualifiedName~ThresholdsCatchUpWithoutChangingTheMachineClock|FullyQualifiedName~OfflineReportUsesClosedVocabulary|FullyQualifiedName~HostReadFailuresAndForeignOrLateResponses'
& dotnet test tests/NAP.Tests/NAP.Tests.csproj -c Release --no-build --no-restore --filter $filter --results-directory $evidence --logger 'trx;LogFileName=focused.trx'
if ($LASTEXITCODE -ne 0) { throw 'Focused tests failed.' }
[xml]$trx = Get-Content (Join-Path $evidence 'focused.trx') -Raw
$counters = $trx.SelectSingleNode("//*[local-name()='Counters']")
if ([int]$counters.total -lt 49 -or $counters.total -ne $counters.passed -or [int]$counters.notExecuted -ne 0) { throw 'Missing/failed/skipped focused tests.' }
# Record the five existing warnings without suppression. No general regression or volume tests.
& dotnet list src/NAP.App/NAP.App.csproj package --include-transitive --vulnerable --format json --no-restore | Out-File (Join-Path $evidence 'nuget-vulnerabilities.json')
if ($LASTEXITCODE -ne 0) { throw 'Could not record vulnerability inventory.' }
$commit = git rev-parse HEAD
@{application='NAP 2.0 Fase 6'; sourceCommit=$commit; architecture='win-x64'; acceptanceOnly=$true;
 runtime=$runtime.runtimeOptions.includedFrameworks; run="https://github.com/robdor80/NAP/actions/runs/$env:GITHUB_RUN_ID";
 sdk=(& dotnet --version); privateLicenseIncluded=$false; sessionPersistence='none';
 pending=@('#49: five NU1902/NU1903 advisories; IPC timeout; StorageUnavailable','#47: CUTOVER blocked','Human MSI operational acceptance pending')} |
 ConvertTo-Json -Depth 8 | Out-File (Join-Path $publish 'PROCEDENCIA.json') -Encoding utf8
$files = @(Get-ChildItem $publish -File -Recurse)
foreach ($file in $files) {
 if ($file.Name -match '(?i)(\.lic$|\.pdb$|\.pfx$|\.pem$|\.key$|\.env$|settings\.json|license-reminders\.json)' -or $file.FullName -match '[\\/](obj|\.git)[\\/]') { throw 'Private/developer filename detected; contents withheld.' }
 $bytes = [IO.File]::ReadAllBytes($file.FullName)
 foreach ($encoding in @([Text.Encoding]::UTF8,[Text.Encoding]::Unicode)) {
  $text = $encoding.GetString($bytes)
  if ($text -match '(?i)Id=[^;\r\n]{1,160};Kind=(Community|Commercial);|gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,}|-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----') { throw 'Secret-like material detected; contents withheld.' }
 }
}
$manifest = @($files | Sort-Object FullName | ForEach-Object { '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(),[IO.Path]::GetRelativePath($publish,$_.FullName).Replace('\','/') })
$manifest | Out-File (Join-Path $publish 'SHA256SUMS.txt') -Encoding utf8
$zip = Join-Path $delivery 'NAP-2.0-Fase6-Aceptacion-Windows-x64.zip'
Compress-Archive -Path "$publish/*" -DestinationPath $zip -CompressionLevel Optimal
$extracted = Join-Path $env:RUNNER_TEMP 'nap-acceptance-extracted'
Expand-Archive $zip $extracted
function Verify-Manifest([string]$root) {
 $lines = @(Get-Content (Join-Path $root 'SHA256SUMS.txt'))
 foreach ($line in $lines) {
  $hash,$path = $line -split '  ',2
  if ((Get-FileHash (Join-Path $root $path) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash) { throw "Manifest mismatch: $path" }
 }
 if (@(Get-ChildItem $root -File -Recurse).Count -ne $lines.Count + 1) { throw 'Unexpected/missing package file.' }
}
Verify-Manifest $extracted
# Poison ordinary settings/profiles with invalid synthetic content. ETW must show no access.
$ordinary = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'NAP'
New-Item -ItemType Directory -Force "$ordinary/profiles","$env:PROGRAMDATA/NAP-coordination-v2-phase6-canary" | Out-Null
'INVALID SYNTHETIC SETTINGS - MUST NOT BE READ' | Out-File "$ordinary/settings.json"
'INVALID SYNTHETIC LICENSE REGISTER' | Out-File "$ordinary/license-reminders.json"
'INVALID SYNTHETIC PROFILE' | Out-File "$ordinary/profiles/profile.json"
function Snapshot-Canaries {
 @(Get-ChildItem $ordinary,"$env:PROGRAMDATA/NAP-coordination-v2-phase6-canary" -File -Recurse | Sort-Object FullName | ForEach-Object { $_.FullName + ':' + (Get-FileHash $_.FullName).Hash }) -join "`n"
}
$before = Snapshot-Canaries
& dotnet tools/NAP.AcceptanceIoProbe/bin/Release/net8.0/NAP.AcceptanceIoProbe.dll (Join-Path $extracted 'NAP.App.exe') (Join-Path $evidence 'file-io.json') --phase6-preview=acceptance --phase6-self-test --compact
if ($LASTEXITCODE -ne 0) { throw 'Acceptance process or ETW isolation gate failed.' }
if ((Snapshot-Canaries) -ne $before) { throw 'Ordinary NAP canary changed.' }
$acceptance = Get-Content (Join-Path $extracted 'laboratorio/aceptacion.json') -Raw | ConvertFrom-Json
if (-not $acceptance.acceptanceOnlyBuild -or $acceptance.bindingErrors -ne 0) { throw 'Not an isolated acceptance build.' }
Copy-Item "$extracted/laboratorio/*" $evidence
Remove-Item "$extracted/laboratorio" -Recurse -Force
Verify-Manifest $extracted
# Deliberate laboratory escape attempt: a junction must be rejected before any write.
$escape = Join-Path $env:RUNNER_TEMP 'nap-forbidden-lab-target'
New-Item -ItemType Directory $escape | Out-Null
New-Item -ItemType Junction -Path (Join-Path $extracted 'laboratorio') -Target $escape | Out-Null
$badLab = Start-Process (Join-Path $extracted 'NAP.App.exe') -ArgumentList '--phase6-self-test' -PassThru
if (-not $badLab.WaitForExit(10000)) { $badLab.Kill(); throw 'Unsafe lab did not stop.' }
if ($badLab.ExitCode -ne 1 -or @(Get-ChildItem $escape -Force).Count -ne 0) { throw 'Unsafe lab was used.' }
Remove-Item (Join-Path $extracted 'laboratorio') -Force
# Direct executable startup must use the same acceptance branch; no development tools in user flow.
$direct = Start-Process (Join-Path $extracted 'NAP.App.exe') -WorkingDirectory $env:RUNNER_TEMP -PassThru
Start-Sleep -Seconds 5
if ($direct.HasExited) { throw 'Direct acceptance startup failed.' }
$direct.CloseMainWindow() | Out-Null
if (-not $direct.WaitForExit(10000)) { $direct.Kill(); throw 'Normal window close failed.' }
if ($direct.ExitCode -ne 0) { throw 'Direct acceptance exited unsuccessfully.' }
# Unsafe ordinary arguments and arbitrary output locations must be rejected before creating a window.
foreach ($bad in @('--automation-config=C:\not-allowed.json','--settings=C:\not-allowed.json','--phase6-output=C:\not-allowed','--phase6-preview=unknown')) {
 $p = Start-Process (Join-Path $extracted 'NAP.App.exe') -ArgumentList $bad -PassThru
 if (-not $p.WaitForExit(10000)) { $p.Kill(); throw 'Unsafe argument did not fail closed.' }
 if ($p.ExitCode -ne 1) { throw 'Unsafe argument accepted.' }
}
# The actual double-click launcher is tested via cmd.exe, then its GUI is closed normally.
$launcher = Join-Path $extracted 'Iniciar-prueba-NAP.cmd'
$cmd = Start-Process cmd.exe -ArgumentList '/d','/c',('"' + $launcher + '"') -PassThru
Start-Sleep -Seconds 5
$launched = @(Get-Process NAP.App -ErrorAction SilentlyContinue)
if ($launched.Count -ne 1) { throw 'Launcher did not start exactly one acceptance process.' }
$launched[0].CloseMainWindow() | Out-Null
if (-not $cmd.WaitForExit(10000)) { $cmd.Kill(); throw 'Launcher did not exit.' }
if ($cmd.ExitCode -ne 0) { throw 'Launcher failed.' }
Verify-Manifest $extracted
Copy-Item (Join-Path $publish 'SHA256SUMS.txt') $delivery
Copy-Item (Join-Path $publish 'PROCEDENCIA.json') $delivery
$zipHash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$zipHash  $([IO.Path]::GetFileName($zip))" | Out-File (Join-Path $delivery 'ZIP-SHA256.txt') -Encoding utf8
@{ sourceCommit=$commit; focusedPassed=[int]$counters.passed; bindingErrors=0; files=$files.Count+1;
 zipSha256=$zipHash; extractedManifestVerified=$true; directStartup=$true; launcherVerified=$true;
 unsafeArgumentsRejected=$true; unsafeLaboratoryJunctionRejected=$true; unchangedOrdinaryCanaries=$true; etwFileIoGate=$true; retentionDaysRequested=90;
 noMsiAccess=$true; noHostStarted=$true; noGeneralRegression=$true; noAssetZipTests=$true;
 privateFileAndSecretPatternScanPassed=$true; noticesCompleteForReviewedGraph=$true } | ConvertTo-Json -Depth 6 | Out-File (Join-Path $evidence 'verification.json')
Copy-Item (Join-Path $evidence 'verification.json') $delivery
git diff --exit-code
if ($LASTEXITCODE -ne 0) { throw 'Source changed during verification.' }

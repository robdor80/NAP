[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string] $ExpectedCommit,
    [switch] $PrepareOnly
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
if (-not $IsWindows -or [Environment]::OSVersion.Version.Build -lt 22000 -or $PSVersionTable.PSVersion.Major -lt 7) {
    throw 'Requires Windows 11 and PowerShell 7. Do not run on production or in Cloud.'
}
if (-not [Environment]::Is64BitProcess) { throw 'Use x64 PowerShell.' }
$source = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd('\')
$temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
$sourceName = [IO.Path]::GetFileName($source)
if ([IO.Path]::GetDirectoryName($source) -ne $temp -or $sourceName -notmatch '^NAP-F81-Source-[0-9a-f]{32}$') {
    throw 'Requires a new independent TEMP/NAP-F81-Source-<GUID> checkout. Production checkout rejected.'
}
function Assert-OrdinaryAncestors([string] $Path) {
    for ($dir = [IO.DirectoryInfo]::new($Path); $null -ne $dir; $dir = $dir.Parent) {
        if (-not $dir.Exists -or ($dir.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Missing directory or reparse point. Stop.' }
    }
}
Assert-OrdinaryAncestors $source
$head = & git -C $source rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $head -ne $ExpectedCommit) { throw 'Checkout commit mismatch.' }
$dirty = & git -C $source status --porcelain
if ($LASTEXITCODE -ne 0 -or $dirty) { throw 'Checkout must be clean before preparing laboratory.' }
$sdks = & dotnet --list-sdks
if ($LASTEXITCODE -ne 0 -or -not ($sdks -match '^8\.0\.')) { throw '.NET SDK 8 is required.' }
$runtimes = & dotnet --list-runtimes
if ($LASTEXITCODE -ne 0 -or -not ($runtimes -match '^Microsoft.WindowsDesktop.App 8\.0\.')) { throw 'Windows Desktop Runtime 8 x64 required.' }
# Consume only the privately selected original file via the official property. Never read, copy or print its bytes.
if ([string]::IsNullOrWhiteSpace($env:SixLaborsLicenseFile) -or -not [IO.File]::Exists($env:SixLaborsLicenseFile)) {
    throw 'Original Six Labors license path must be selected privately in SixLaborsLicenseFile. No new license or secret transfer is requested.'
}
if (-not [string]::IsNullOrEmpty($env:SixLaborsLicenseKey)) { throw 'Use the original file selection, not a key override.' }
Assert-OrdinaryAncestors $temp
$lab = Join-Path $temp ('NAP-F81-Lab-' + [guid]::NewGuid().ToString('N'))
if (Test-Path -LiteralPath $lab) { throw 'Laboratory must be new.' }
[IO.Directory]::CreateDirectory($lab) | Out-Null
Assert-OrdinaryAncestors $lab
foreach ($name in @('app','build','evidence')) { [IO.Directory]::CreateDirectory((Join-Path $lab $name)) | Out-Null }
$consent = 'NAP-F81-SYNTHETIC-READONLY-v1'
[IO.File]::WriteAllText((Join-Path $lab 'f81-isolation.txt'), $consent)
$project = Join-Path $source 'src/NAP.App/NAP.App.csproj'
# Separate build artifacts prevent reusing acceptance-only or ordinary startup outputs.
# Official Release licensing remains enabled; no verbosity diagnostics/binlogs and no secret values in arguments.
& dotnet publish $project -c Release -r win-x64 --self-contained false -p:Phase81GitLabOnly=true -p:Phase6AcceptanceOnly=false --artifacts-path (Join-Path $lab 'build') -o (Join-Path $lab 'app') --verbosity minimal -nodeReuse:false
if ($LASTEXITCODE -ne 0) { throw 'Licensed F8.1 publish failed. Keep evidence; do not launch or switch mode.' }
if (Get-ChildItem (Join-Path $lab 'app') -Filter '*.lic' -File -Recurse) { throw 'Unexpected license in output. Stop; do not share outputs.' }
if (Test-Path (Join-Path $lab 'app/config')) { throw 'Unexpected installed profiles. Stop.' }
$exe = Join-Path $lab 'app/NAP.GitVisualLab.exe'
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Dedicated laboratory binary missing.' }
Get-ChildItem (Join-Path $lab 'app') -File -Recurse | Get-FileHash -Algorithm SHA256 |
    Select-Object @{Name='File';Expression={[IO.Path]::GetRelativePath($lab, $_.Path)}},Hash |
    ConvertTo-Json | Set-Content (Join-Path $lab 'evidence/binary-hashes.json')
[IO.File]::WriteAllText((Join-Path $lab 'evidence/source-commit.txt'), $head)
$activation = @("--f81-git-visual-lab=$consent", "--f81-root=`"$lab`"")
function Invoke-Preflight([string[]] $Options, [string] $Name, [int] $ExpectedExit) {
    $out = Join-Path $lab ('evidence/' + $Name + '.stdout.txt')
    $err = Join-Path $lab ('evidence/' + $Name + '.stderr.txt')
    $process = Start-Process -FilePath $exe -ArgumentList $Options -PassThru -RedirectStandardOutput $out -RedirectStandardError $err
    if (-not $process.WaitForExit(15000)) {
        # Stop only the exact newly spawned dedicated laboratory process. Never touch another NAP process.
        $process.Kill(); throw 'Preflight timeout. No interactive launch permitted.'
    }
    $process.Refresh()
    if ($process.ExitCode -ne $ExpectedExit) { throw 'Preflight exit mismatch. Stop.' }
}
Invoke-Preflight ($activation + '--f81-verify-isolation') 'contract' 0
if ((Get-Content (Join-Path $lab 'evidence/contract.stdout.txt') -Raw) -notmatch '^F81_ISOLATION_CONTRACT_OK;') { throw 'Isolation proof missing.' }
Invoke-Preflight @('--f81-git-visual-lab=wrong') 'rejected-activation' 1
Invoke-Preflight ($activation + @('--f81-verify-isolation','--settings=forbidden')) 'rejected-settings' 1
Write-Host "Isolated laboratory prepared: $lab"
Write-Host "Exact launch: & '$exe' '--f81-git-visual-lab=$consent' '--f81-root=$lab'"
if ($PrepareOnly) { return }
$process = Start-Process -FilePath $exe -ArgumentList $activation -PassThru -RedirectStandardOutput (Join-Path $lab 'evidence/session.stdout.txt') -RedirectStandardError (Join-Path $lab 'evidence/session.stderr.txt')
Write-Host "Dedicated laboratory PID: $($process.Id). Close its window to stop. Keep all evidence."
$process.WaitForExit()
$process.Refresh()
if ($process.ExitCode -ne 0) { throw 'Laboratory session failed. Preserve evidence; do not certify F8.1.' }
$bindingReport = Get-Content (Join-Path $lab 'evidence/session.stderr.txt') -Raw
if ($bindingReport -notmatch 'F81_WPF_BINDING_ERRORS=0' -or $bindingReport -match 'wpf_binding_error:') {
    throw 'WPF binding evidence missing or failed. No certification.'
}
Write-Host 'Visual session ended; physical acceptance still requires evidence review by ChatGPT.'

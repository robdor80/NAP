param([Parameter(Mandatory=$true)][string]$ProbePath)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows -or $env:GITHUB_ACTIONS -ne 'true') { throw 'Disposable Windows runner only.' }
$probe = [IO.Path]::GetFullPath($ProbePath)
$volume = [IO.Path]::GetPathRoot([IO.Path]::GetTempPath()).TrimEnd('\')
$runnerVolume = [IO.Path]::GetPathRoot($env:RUNNER_TEMP).TrimEnd('\')
if ($volume -ne $runnerVolume -or $volume -eq $env:SystemDrive -or [IO.DriveInfo]::new($volume).DriveFormat -ne 'NTFS') {
    throw 'Only the disposable non-system NTFS runner volume can be prepared.'
}
[IO.Directory]::CreateDirectory('artifacts/windows-regression') | Out-Null
$before = & dotnet $probe catalog-session observe
if ($LASTEXITCODE -ne 0) { throw 'Catalog fallback behavior failed before volume preparation.' }
$before | Set-Content artifacts/windows-regression/catalog-session-before.json
& fsutil usn queryjournal $volume
if ($LASTEXITCODE -ne 0) {
    & fsutil usn createjournal m=67108864 a=8388608 $volume
    if ($LASTEXITCODE -ne 0) { throw 'Cannot prepare the required per-file USN facility on the disposable volume.' }
}
$after = & dotnet $probe catalog-session reusable
if ($LASTEXITCODE -ne 0) { throw 'Catalog reusable session requirements failed after volume preparation.' }
$after | Set-Content artifacts/windows-regression/catalog-session-after.json
Write-Host "Synthetic catalog session behavior before: $before; after: $after"

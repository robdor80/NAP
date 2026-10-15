param([Parameter(Mandatory=$true)][string]$CorePath)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows -or $env:GITHUB_ACTIONS -ne 'true') { throw 'Disposable Windows runner only.' }
$assembly = [Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($CorePath))
$capture = $assembly.GetType('NAP.Core.CatalogFileStamp').GetMethod('Capture', [Reflection.BindingFlags]'Static,NonPublic')
$observations = @()
foreach ($parent in @($env:RUNNER_TEMP, (Join-Path $env:USERPROFILE 'AppData/Local/Temp')) | Select-Object -Unique) {
    $lab = Join-Path $parent ('nap-stamp-' + [guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($lab) | Out-Null
    $path = Join-Path $lab 'synthetic.db'
    $bytes = [byte[]]::new(8192)
    [IO.File]::WriteAllBytes($path, $bytes)
    $before = $capture.Invoke($null, [object[]]@([string]$path))
    $time = [IO.File]::GetLastWriteTimeUtc($path)
    [IO.File]::WriteAllBytes($path, $bytes)
    [IO.File]::SetLastWriteTimeUtc($path, $time)
    $same = $capture.Invoke($null, [object[]]@([string]$path))
    $bytes[4096] = 255
    [IO.File]::WriteAllBytes($path, $bytes)
    [IO.File]::SetLastWriteTimeUtc($path, $time)
    $changed = $capture.Invoke($null, [object[]]@([string]$path))
    $drive = [IO.DriveInfo]::new($path)
    $observations += [pscustomobject]@{ Path=$path; Format=$drive.DriveFormat; DriveType=$drive.DriveType.ToString(); Before=$before; SameBytesRewritten=$same; BodyChanged=$changed }
}
[IO.Directory]::CreateDirectory('artifacts/windows-regression') | Out-Null
$observations | ConvertTo-Json -Depth 8 | Set-Content artifacts/windows-regression/catalog-stamp.json
$observations | ConvertTo-Json -Depth 8 | Write-Host

# Synthetic single personal start on ephemeral Windows GitHub CI only.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$InstalledDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_OS -ne 'Windows') { throw 'Only CI.' }
$base = Join-Path $env:LOCALAPPDATA 'NAP'
$settings = Join-Path $base 'settings.json'
$config = Join-Path $base 'Host\launch.json'
$fixture = Join-Path $env:RUNNER_TEMP ('nap09-personal-' + [Guid]::NewGuid().ToString('N'))
$root = Join-Path $fixture 'workspace'
$prod = Join-Path $fixture 'production'
$archive = Join-Path $fixture 'archive'
$mixed = Join-Path $fixture 'mixed'
$log = Join-Path $fixture 'desktop-stdout.txt'
$err = Join-Path $fixture 'desktop-stderr.txt'
foreach ($directory in @($base, $root, $prod, $archive, $mixed)) { [void][IO.Directory]::CreateDirectory($directory) }
if (Test-Path -LiteralPath $settings -PathType Leaf) { throw 'CI settings already exist.' }
if (Test-Path -LiteralPath $config -PathType Leaf) { throw 'CI Host config already exists.' }
$settingsJson = [ordered]@{
  schema_version = 1
  universes = @([ordered]@{universe_id='nimroel'; workspace_root=$root; production_root=$prod; archive_root=$archive})
} | ConvertTo-Json -Depth 4
[IO.File]::WriteAllText($settings, $settingsJson, [Text.UTF8Encoding]::new($false))
try {
  $generator = Join-Path $InstalledDirectory 'updater\NAP09.ActivarHostPersonal.ps1'
  & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $generator -Aplicar -BandejaComun $mixed
  if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $config)) { throw 'Launch script failed.' }
  # Run the same WPF assembly through dotnet to capture Console.Error from
  # the WinExe startup; a native GUI launcher can discard standard streams.
  $app = Join-Path $InstalledDirectory 'NAP.App.dll'
  $arguments = @($app, '--smoke-automation', "--smoke-output=$fixture\smoke")
  # Start-Process -Wait waits for the entire process tree, but NAP's Host
  # intentionally survives WPF. Wait only for WPF itself.
  $process = Start-Process -FilePath 'dotnet' -ArgumentList $arguments -PassThru -RedirectStandardOutput $log -RedirectStandardError $err
  if (-not $process.WaitForExit(60000)) {
    $process.Kill()
    throw 'NAP WPF did not exit after the personal-startup smoke.'
  }
  $process.Refresh()
  $stdout = if (Test-Path -LiteralPath $log) { [string](Get-Content -LiteralPath $log -Raw) } else { '' }
  $stderr = if (Test-Path -LiteralPath $err) { [string](Get-Content -LiteralPath $err -Raw) } else { '' }
  if ($process.ExitCode -ne 0 -or $stdout -notmatch '"automationConnected":true') {
    throw ("NAP09 personal launch FAILED exit={0}; stderr={1}; stdout={2}" -f $process.ExitCode,
      ([string]$stderr), ([string]$stdout))
  }
  Write-Host 'NAP09 personal launch via real PowerShell config PASS.'
}
finally {
  Get-CimInstance Win32_Process -Filter "Name = 'NAP.AutomationHost.exe'" |
    Where-Object { $_.CommandLine -and $_.CommandLine.Contains($config) } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
}

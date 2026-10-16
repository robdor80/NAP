param([Parameter(Mandatory)][string]$LaunchConfiguration,
      [string]$CorePath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'src/NAP.Core/bin/Release/net8.0/NAP.Core.dll'))
$ErrorActionPreference = 'Stop'
$taskLaunchPath = [IO.Path]::GetFullPath($LaunchConfiguration)
$taskLabRoot = [IO.Path]::GetDirectoryName($taskLaunchPath)
$taskTempRoot = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath([IO.Path]::GetTempPath())) + [IO.Path]::DirectorySeparatorChar
if (-not $taskLabRoot.StartsWith($taskTempRoot, [StringComparison]::OrdinalIgnoreCase) -or
    -not [IO.Path]::GetFileName($taskLabRoot).StartsWith('nap-host-', [StringComparison]::Ordinal)) {
    throw 'Only a generated TEMP laboratory may be inspected.'
}
[void][Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($CorePath))
$taskConfig = [IO.File]::ReadAllText($taskLaunchPath) | ConvertFrom-Json
$taskConnection = [NAP.Core.AutomationConnection]::new($taskConfig.Connection.PipeName, $taskConfig.Connection.KeyBase64)
$taskClient = [NAP.Core.AutomationHostClient]::new($taskConnection)
$taskDeadline = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(60))
function Read-HostOperation($operation, [string]$universe, $payload) {
    $taskClient.CallAsync[object]($operation, $universe, $payload, $taskDeadline.Token,
        ('command_' + [guid]::NewGuid().ToString('N'))).GetAwaiter().GetResult().GetRawText() | ConvertFrom-Json
}
try {
    $snapshot = Read-HostOperation ([NAP.Core.AutomationOperation]::Status) '' @{}
    $universes = @(foreach ($definition in $taskConfig.Universes) {
        $id = $definition.Storage.UniverseId.Value
        $items = [Collections.Generic.List[object]]::new()
        for ($offset = 0; ; $offset += 100) {
            $page = @(Read-HostOperation ([NAP.Core.AutomationOperation]::QueuePage) $id @{Offset=$offset; Limit=100})
            $items.AddRange([object[]]$page)
            if ($page.Count -lt 100) { break }
        }
        $sample = $items | Where-Object State -eq 'Queued' | Select-Object -First 1
        $events = if ($null -ne $sample) {
            @(Read-HostOperation ([NAP.Core.AutomationOperation]::Events) $id @{ItemId=$sample.Id.Value}) | Select-Object -Last 5
        } else { @() }
        [ordered]@{ universe=$id; items=$items.Count; revisionSum=($items | Measure-Object Revision -Sum).Sum
            states=@($items | Group-Object State | Select-Object Name,Count); sampledItem=$sample.Id.Value; latestEvents=@($events) }
    })
    [ordered]@{ observedUtc=[DateTimeOffset]::UtcNow.ToString('O'); status=$snapshot; universes=$universes } | ConvertTo-Json -Depth 16
} finally { $taskDeadline.Dispose() }

param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath($RepositoryRoot)
$artifactRoot = Join-Path $taskRoot 'artifacts/phase5'
$entries = @(Get-ChildItem -LiteralPath $artifactRoot -Filter '*.trx' -Recurse | Sort-Object FullName | ForEach-Object {
    [xml]$report = [IO.File]::ReadAllText($_.FullName)
    $counters = $report.TestRun.ResultSummary.Counters
    [ordered]@{
        file = [IO.Path]::GetRelativePath($taskRoot, $_.FullName).Replace('\', '/')
        sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        start = [string]$report.TestRun.Times.start
        finish = [string]$report.TestRun.Times.finish
        total = [int]$counters.total
        passed = [int]$counters.passed
        failed = [int]$counters.failed
        notExecuted = [int]$counters.notExecuted
        failedTests = @($report.TestRun.Results.UnitTestResult | Where-Object outcome -eq 'Failed' | ForEach-Object { [string]$_.testName })
    }
})
if ($entries.Count -eq 0) { throw 'No retained Phase 5 TRX evidence was found.' }
$entries | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath (Join-Path $taskRoot 'docs/NAP_2_0_FASE_5_TEST_EVIDENCE.json') -Encoding utf8NoBOM
$metricRoot = Join-Path $artifactRoot 'p510/metrics'
if (Test-Path -LiteralPath $metricRoot) {
    $metrics = @(Get-ChildItem -LiteralPath $metricRoot -Filter '*.json' | Sort-Object FullName | ForEach-Object {
        [ordered]@{
            file = [IO.Path]::GetRelativePath($taskRoot, $_.FullName).Replace('\', '/')
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            measurement = ([IO.File]::ReadAllText($_.FullName) | ConvertFrom-Json)
        }
    })
    ConvertTo-Json -InputObject $metrics -Depth 25 | Set-Content -LiteralPath (Join-Path $taskRoot 'docs/NAP_2_0_FASE_5_RESOURCE_EVIDENCE.json') -Encoding utf8NoBOM
}
$changed = @(git -C $taskRoot diff --name-only cd7532f011eea8a72e89848234b259f1f5293e15)
if ($LASTEXITCODE -ne 0) { throw 'Cannot inventory changes against the Phase 4 baseline.' }
$untracked = @(git -C $taskRoot ls-files --others --exclude-standard)
@($changed + $untracked) | Sort-Object -Unique | Set-Content -LiteralPath (Join-Path $taskRoot 'docs/NAP_2_0_FASE_5_CHANGED_FILES.txt') -Encoding utf8NoBOM
Write-Output "Indexed $($entries.Count) retained TRX runs. Overlapping selections are not additive."

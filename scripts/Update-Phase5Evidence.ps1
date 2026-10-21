param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent),
      [string]$ComparisonTrx = 'artifacts/phase5/final/p510-final-complete-regression.trx')
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath($RepositoryRoot)
$artifactRoot = Join-Path $taskRoot 'artifacts/phase5'
$manifestPath = Join-Path $taskRoot 'docs/NAP_2_0_FASE_5_TEST_EVIDENCE.json'
$entries = @(Get-ChildItem -LiteralPath $artifactRoot -Filter '*.trx' -Recurse | Sort-Object FullName | ForEach-Object {
    [xml]$report = [IO.File]::ReadAllText($_.FullName)
    $counters = $report.TestRun.ResultSummary.Counters
    [ordered]@{
        file = [IO.Path]::GetRelativePath($taskRoot, $_.FullName).Replace('\', '/')
        sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        start = [string]$report.TestRun.Times.start
        finish = [string]$report.TestRun.Times.finish
        runOutcome = [string]$report.TestRun.ResultSummary.outcome
        runMessages = @($report.TestRun.ResultSummary.RunInfos.RunInfo | ForEach-Object { [string]$_.Text })
        total = [int]$counters.total
        executed = [int]$counters.executed
        passed = [int]$counters.passed
        failed = [int]$counters.failed
        notExecuted = [int]$counters.notExecuted
        # Some VSTest TRX counters say zero while individual Windows-only results are NotExecuted.
        # Preserve the raw counter above and record the actual omitted result count separately.
        notExecutedResults = @($report.TestRun.Results.UnitTestResult | Where-Object outcome -eq 'NotExecuted').Count
        failedTests = @($report.TestRun.Results.UnitTestResult | Where-Object outcome -eq 'Failed' | ForEach-Object { [string]$_.testName })
    }
})
if ($entries.Count -eq 0) { throw 'No retained Phase 5 TRX evidence was found.' }
# A recovered cloud checkout may retain older raw runs only in the committed evidence ZIP.
# Index new files without erasing those verified historical records or relabeling their outcomes.
$indexed = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
if (Test-Path -LiteralPath $manifestPath) {
    $storedManifest = [Text.Json.JsonDocument]::Parse([IO.File]::ReadAllText($manifestPath))
    try {
        foreach ($element in $storedManifest.RootElement.EnumerateArray()) {
            $entry = $element.GetRawText() | ConvertFrom-Json -AsHashtable
            # PowerShell 7.4 converts ISO strings to DateTime and changes their original offset.
            foreach ($name in @('start', 'finish')) { $entry[$name] = $element.GetProperty($name).GetString() }
            $indexed[$entry.file] = $entry
        }
    } finally { $storedManifest.Dispose() }
}
foreach ($entry in $entries) { $indexed[$entry.file] = $entry }
$indexed.Values | Sort-Object { $_.file } | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
$baseline = Join-Path $taskRoot 'artifacts/phase5/preacceptance/phase5-preacceptance-regression.trx'
$comparisonPath = Join-Path $taskRoot $ComparisonTrx
if (-not (Test-Path -LiteralPath $comparisonPath)) {
    $ComparisonTrx = 'artifacts/phase5/p510/p510-review-regression.trx'
    $comparisonPath = Join-Path $taskRoot $ComparisonTrx
}
if ((Test-Path -LiteralPath $baseline) -and (Test-Path -LiteralPath $comparisonPath)) {
    [xml]$previous = [IO.File]::ReadAllText($baseline)
    [xml]$current = [IO.File]::ReadAllText($comparisonPath)
    $compared = @($previous.TestRun.Results.UnitTestResult | Where-Object outcome -eq 'Failed' | ForEach-Object {
        $prior = $_
        $taskMatchedTests = @($current.TestRun.Results.UnitTestResult | Where-Object { $_.testName -ceq $prior.testName })
        [ordered]@{
            test = [string]$prior.testName
            previousClassification = if ([string]$prior.Output.ErrorInfo.Message -match 'privilegio requerido|required privilege') { 'EnvironmentSymlinkPrivilege' } else { 'Software' }
            currentOutcome = if ($taskMatchedTests.Count -eq 1) { [string]$taskMatchedTests[0].outcome } elseif ($taskMatchedTests.Count -eq 0) { 'Missing' } else { 'Ambiguous' }
            currentMessage = if ($taskMatchedTests.Count -eq 1 -and $taskMatchedTests[0].outcome -eq 'Failed') { [string]$taskMatchedTests[0].Output.ErrorInfo.Message } else { $null }
        }
    })
    [ordered]@{
        previousTrx = [IO.Path]::GetRelativePath($taskRoot, $baseline).Replace('\', '/')
        currentTrx = $ComparisonTrx.Replace('\', '/')
        previousSha256 = (Get-FileHash -LiteralPath $baseline -Algorithm SHA256).Hash.ToLowerInvariant()
        currentSha256 = (Get-FileHash -LiteralPath $comparisonPath -Algorithm SHA256).Hash.ToLowerInvariant()
        comparisons = $compared
    } | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $taskRoot 'docs/NAP_2_0_FASE_5_REGRESSION_COMPARISON.json') -Encoding utf8NoBOM
}
$metricRoot = Join-Path $artifactRoot 'p510/metrics'
if (Test-Path -LiteralPath $metricRoot) {
    $metrics = @(Get-ChildItem -LiteralPath $metricRoot -Filter '*.json' | Sort-Object FullName | ForEach-Object {
        [ordered]@{
            file = [IO.Path]::GetRelativePath($taskRoot, $_.FullName).Replace('\', '/')
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            measurement = ([IO.File]::ReadAllText($_.FullName) | ConvertFrom-Json)
        }
    })
    $metricPath = Join-Path $taskRoot 'docs/NAP_2_0_FASE_5_RESOURCE_EVIDENCE.json'
    $indexedMetrics = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    if (Test-Path -LiteralPath $metricPath) {
        foreach ($entry in @([IO.File]::ReadAllText($metricPath) | ConvertFrom-Json -AsHashtable)) { $indexedMetrics[$entry.file] = $entry }
    }
    foreach ($entry in $metrics) { $indexedMetrics[$entry.file] = $entry }
    ConvertTo-Json -InputObject @($indexedMetrics.Values | Sort-Object { $_.file }) -Depth 25 | Set-Content -LiteralPath $metricPath -Encoding utf8NoBOM
}
$changed = @(git -C $taskRoot diff --name-only cd7532f011eea8a72e89848234b259f1f5293e15)
if ($LASTEXITCODE -ne 0) { throw 'Cannot inventory changes against the Phase 4 baseline.' }
$untracked = @(git -C $taskRoot ls-files --others --exclude-standard)
@($changed + $untracked) | Sort-Object -Unique | Set-Content -LiteralPath (Join-Path $taskRoot 'docs/NAP_2_0_FASE_5_CHANGED_FILES.txt') -Encoding utf8NoBOM
Write-Output "Indexed $($entries.Count) current TRX runs; retained $($indexed.Count) total records. Overlapping selections are not additive."

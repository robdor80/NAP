param(
    [Parameter(Mandatory = $true)]
    [string] $ResultsPath
)

$ErrorActionPreference = 'Stop'
$expected = @(
    'SyntheticNormalizationComparisonRendersAtBothViewportsWithoutCreatingDefinitiveCandidate'
    'RealCatalogFiltersDetailStatisticsAndThirtyThousandRecycledItemsRender'
    'EmptyBootstrapAndVirtualizedPanelRenderWithoutCreatingSettings'
    'ImportAddsSecondUniverseAndSwitchesSelectorWithoutCreatingRootsOrSettings'
)
$summary = @('## Windows/WPF: required execution of four smoke tests', '')

try {
    if (-not (Test-Path -LiteralPath $ResultsPath -PathType Leaf)) {
        throw 'No WPF TRX was produced. Restore/build/test may have failed; Windows execution is not verified.'
    }
    [xml] $trx = Get-Content -LiteralPath $ResultsPath -Raw
    $ns = [System.Xml.XmlNamespaceManager]::new($trx.NameTable)
    $ns.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
    $results = @($trx.SelectNodes('/t:TestRun/t:Results/t:UnitTestResult', $ns))
    $problems = @()
    foreach ($name in $expected) {
        $fullName = "NAP.Tests.VisualExplorerWindowsSmokeTests.$name"
        $matches = @($results | Where-Object { $_.testName -eq $fullName })
        $outcome = if ($matches.Count -eq 1) { [string] $matches[0].outcome } else { "Missing or duplicated ($($matches.Count))" }
        $summary += "- ${name}: $outcome"
        if ($matches.Count -ne 1 -or $outcome -ne 'Passed') {
            $problems += "${name}: $outcome"
        }
    }
    if ($results.Count -ne $expected.Count) {
        $problems += "Expected exactly four results; found $($results.Count)."
    }
    if ($problems.Count -gt 0) {
        throw ($problems -join "`n")
    }
    Write-Host 'All four existing Windows/WPF tests executed and passed.'
}
catch {
    $summary += '', "Verification failed: $($_.Exception.Message)", '',
        'No Windows/WPF certification is claimed. Inspect the TRX and job logs for desktop, runtime, rendering, timeout or application failures. Tests are not silently skipped or replaced.'
    throw
}
finally {
    $summary | Write-Host
    if ($env:GITHUB_STEP_SUMMARY) {
        $summary | Out-File -LiteralPath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Append
    }
}

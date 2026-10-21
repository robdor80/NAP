param([Parameter(Mandatory = $true)][string] $ResultsPath)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $ResultsPath -PathType Leaf)) {
    throw 'No regression TRX; the five real symlink checks are not certified.'
}
[xml] $taskTrx = Get-Content -LiteralPath $ResultsPath -Raw
$taskNs = [System.Xml.XmlNamespaceManager]::new($taskTrx.NameTable)
$taskNs.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
$taskResults = @($taskTrx.SelectNodes('/t:TestRun/t:Results/t:UnitTestResult', $taskNs))
$taskExpected = @(
    'NAP.Tests.AutomationQueueTests.LinkedBoundaryArtifactsNeverModifyTargets(kind: "state")'
    'NAP.Tests.AutomationQueueTests.LinkedBoundaryArtifactsNeverModifyTargets(kind: "database")'
    'NAP.Tests.AutomationQueueTests.LinkedBoundaryArtifactsNeverModifyTargets(kind: "journal")'
    'NAP.Tests.AutomationQueueTests.LinkedQueueDirectoryAndWalArtifactsAreNeverFollowed'
    'NAP.Tests.InboxAdmissionTests.SymlinkZipAndSymlinkStagingFailClosed'
)
foreach ($taskName in $taskExpected) {
    $taskMatches = @($taskResults | Where-Object { $_.testName -eq $taskName })
    if ($taskMatches.Count -ne 1 -or $taskMatches[0].outcome -ne 'Passed') {
        throw "Real symlink assertion missing, skipped, duplicated or failed: $taskName"
    }
    Write-Host "Passed: $taskName"
}

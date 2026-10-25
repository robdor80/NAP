$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:GITHUB_REF -ne 'refs/heads/codex/nap-2-fase-6-ux') { throw 'Authorized branch runner required.' }
$archive = Join-Path $env:RUNNER_TEMP 'nap-downloaded-artifact.zip'
$extracted = Join-Path $env:RUNNER_TEMP 'nap-downloaded-artifact'
# PowerShell 7.4+ preserves native stdout bytes in file redirection. No token is printed.
& gh api "repos/robdor80/NAP/actions/artifacts/$env:NAP_ARTIFACT_ID/zip" > $archive
if ($LASTEXITCODE -ne 0) { throw 'Published artifact download failed.' }
$hash = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
if ($hash -ne $env:NAP_ARTIFACT_DIGEST) { throw 'Downloaded ZIP differs from GitHub artifact digest.' }
Expand-Archive $archive $extracted
$lines = @(Get-Content (Join-Path $extracted 'SHA256SUMS.txt'))
foreach ($line in $lines) {
  $expected,$path = $line -split '  ',2
  if ((Get-FileHash (Join-Path $extracted $path) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw "Downloaded file mismatch: $path" }
}
if (@(Get-ChildItem $extracted -File -Recurse).Count -ne $lines.Count + 1) { throw 'Downloaded artifact has unexpected files.' }
$provenance = Get-Content (Join-Path $extracted 'PROCEDENCIA.json') -Raw | ConvertFrom-Json
if ($provenance.sourceCommit -ne $env:GITHUB_SHA -or -not $provenance.acceptanceOnly) { throw 'Downloaded artifact has wrong source/mode.' }
$metadata = & gh api "repos/robdor80/NAP/actions/artifacts/$env:NAP_ARTIFACT_ID" | ConvertFrom-Json
if ($metadata.expired -or $metadata.workflow_run.head_sha -ne $env:GITHUB_SHA) { throw 'Artifact metadata does not match this build.' }
$evidence = Join-Path $PWD 'artifacts/phase6-evidence'
@{ artifactId=$env:NAP_ARTIFACT_ID; artifactName=$metadata.name; userDownloadUrl=$env:NAP_ARTIFACT_URL;
  archiveSha256=$hash; expiresAt=$metadata.expires_at; sourceCommit=$env:GITHUB_SHA;
  downloadedZipMatchesGitHubDigest=$true; allManifestFilesVerified=$true; files=$lines.Count+1;
  archiveDownloadEndpoint=$metadata.archive_download_url; authentication='Normal GitHub sign-in required for browser artifact download' } |
  ConvertTo-Json -Depth 6 | Out-File (Join-Path $evidence 'download-verification.json') -Encoding utf8
Copy-Item (Join-Path $extracted 'SHA256SUMS.txt') $evidence
Copy-Item (Join-Path $extracted 'PROCEDENCIA.json') $evidence
Copy-Item (Join-Path $extracted 'third-party-packages.json') $evidence

[CmdletBinding(DefaultParameterSetName = 'Run')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Run')]
    [ValidateNotNullOrEmpty()]
    [string[]] $DotnetArguments,

    [Parameter(Mandatory, ParameterSetName = 'Cleanup')]
    [switch] $Cleanup
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

# This helper is restricted to disposable GitHub-hosted jobs. Never put the
# decoded license, its path or the Base64 value in GITHUB_ENV/GITHUB_OUTPUT.
if ($env:GITHUB_ACTIONS -ne 'true' -or [string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) {
    throw 'License provisioning requires a GitHub Actions runner with RUNNER_TEMP.'
}
$licenseDirectory = Join-Path $env:RUNNER_TEMP 'nap-sixlabors-license'
$licensePath = Join-Path $licenseDirectory 'sixlabors.lic'

function Remove-TemporaryLicense {
    if (Test-Path -LiteralPath $licenseDirectory) {
        Remove-Item -LiteralPath $licenseDirectory -Recurse -Force
    }
}

if ($Cleanup) {
    Remove-TemporaryLicense
    exit 0
}

$licenseB64 = $env:SIXLABORS_LICENSE_B64
# MSBuild must inherit only the file path, never the secret value.
Remove-Item Env:SIXLABORS_LICENSE_B64 -ErrorAction SilentlyContinue
if ([string]::IsNullOrWhiteSpace($licenseB64)) {
    throw 'SIXLABORS_LICENSE_B64 is missing or unavailable. Configure the repository Actions secret from the original license on the MSI. Build stopped; official license validation is required.'
}
if ($env:RUNNER_DEBUG -eq '1') {
    throw 'Disable Actions debug logging before running a licensed build.'
}

$exitCode = 1
try {
    try {
        $licenseBytes = [Convert]::FromBase64String($licenseB64)
    }
    catch {
        # Do not print the decoder exception or the supplied value.
        throw 'SIXLABORS_LICENSE_B64 is not valid Base64.'
    }
    if ($licenseBytes.Length -eq 0) {
        throw 'SIXLABORS_LICENSE_B64 decoded to an empty file.'
    }

    # Defense in depth for ordinary compiler/validator output. Escape workflow
    # commands, including multiline licenses, before registering masks. Also
    # mask individual long tokens so identifiers/signatures cannot be logged.
    $licenseText = [Text.Encoding]::UTF8.GetString($licenseBytes)
    $masks = @($licenseB64, $licenseText) + @($licenseText -split '\r?\n') +
        @([regex]::Matches($licenseText, '[A-Za-z0-9+/=_-]{16,}') | ForEach-Object { $_.Value })
    foreach ($mask in $masks) {
        if (-not [string]::IsNullOrWhiteSpace($mask)) {
            $escaped = $mask.Replace('%', '%25').Replace("`r", '%0D').Replace("`n", '%0A')
            Write-Host "::add-mask::$escaped"
        }
    }
    $licenseB64 = $null
    $licenseText = $null
    $masks = $null

    Remove-TemporaryLicense
    if ($IsWindows) {
        [IO.Directory]::CreateDirectory($licenseDirectory) | Out-Null
    }
    else {
        # Restrict access before writing bytes; no chmod race on Linux.
        $privateMode = [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite -bor [IO.UnixFileMode]::UserExecute
        [IO.Directory]::CreateDirectory($licenseDirectory, $privateMode) | Out-Null
    }
    [IO.File]::WriteAllBytes($licensePath, $licenseBytes)
    [Array]::Clear($licenseBytes, 0, $licenseBytes.Length)
    $env:SixLaborsLicenseFile = $licensePath

    # Keep the official Release validation active. Call sites contain reviewed
    # arguments: no diagnostic verbosity, binary logs or license-key overrides.
    # Disable reusable MSBuild nodes so they cannot retain the temporary path.
    & dotnet @DotnetArguments --verbosity minimal -nodeReuse:false
    $exitCode = $LASTEXITCODE
}
catch {
    # All provisioning failures have a generic message: never dump exceptions,
    # local variables, file contents or environment variables to Actions logs.
    Write-Host '::error::Licensed dotnet command could not complete. Check the repository secret and runner setup without printing the license or enabling diagnostic logs.'
    $exitCode = 1
}
finally {
    Remove-Item Env:SixLaborsLicenseFile -ErrorAction SilentlyContinue
    Remove-TemporaryLicense
}

exit $exitCode

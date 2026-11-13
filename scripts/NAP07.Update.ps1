# NAP07 — actualizador EXPLICITO, sin privilegios, sin servicio y sin CUTOVER.
# Requiere un SHA-256 obtenido por un canal independiente de los metadatos de Releases.
[CmdletBinding()]
param(
    [ValidateSet('Check','Stage','Apply','Recover','Rollback')][string]$Action = 'Check',
    [ValidateSet('stable','preview')][string]$Channel = 'stable',
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [ValidatePattern('^[0-9a-fA-F]{64}$')][string]$TrustedSha256,
    [string]$PreviousSetup,
    [ValidatePattern('^[0-9a-fA-F]{64}$')][string]$PreviousSha256,
    [string]$Confirm,
    [switch]$AllowUnsigned,
    [switch]$AcknowledgeSchemaRisk,
    [string]$FixtureSetup,
    [ValidateSet('None','DiskFull','BeforeSetup','AfterSetup')][string]$Fault = 'None'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$repo = 'robdor80/NAP'
$api = "https://api.github.com/repos/$repo"
$base = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
$install = [IO.Path]::GetFullPath((Join-Path $base 'Programs/NAP'))
$updates = [IO.Path]::GetFullPath((Join-Path $base 'NAP/Updates'))
$staging = Join-Path $updates 'staging'
$rollback = Join-Path $updates 'rollback'
$journalFile = Join-Path $updates 'transaction.json'
$lockStream = $null
$ci = $env:GITHUB_ACTIONS -eq 'true'
if (-not [Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::Windows)) {
    throw 'NAP07 se ejecuta exclusivamente en Windows.'
}
if (($FixtureSetup -or $Fault -ne 'None') -and -not $ci) {
    throw 'Los fixtures e inyecciones de fallos solo estan disponibles en GitHub Actions.'
}
function Assert-Plain([string]$path) {
    $p = [IO.Path]::GetFullPath($path)
    while ($p) {
        if (Test-Path -LiteralPath $p) {
            $i = Get-Item -LiteralPath $p -Force
            if (($i.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Ruta con enlace/reparse rechazada: $p"
            }
        }
        $parent = [IO.Path]::GetDirectoryName($p)
        if (-not $parent -or $parent -eq $p) { break }
        $p = $parent
    }
}
function Assert-TrustedFile([string]$path,[string]$sha,[switch]$unsigned) {
    if (-not $sha -or $sha -notmatch '^[0-9a-fA-F]{64}$') { throw 'Es obligatorio proporcionar SHA-256 independiente.' }
    Assert-Plain $path
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Fichero no encontrado: $path" }
    $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $sha.ToLowerInvariant()) { throw "SHA-256 no coincide: $path" }
    $sig = Get-AuthenticodeSignature -LiteralPath $path
    if ($sig.Status -eq 'Valid') { return 'Authenticode valido + SHA-256 independiente' }
    if ($sig.Status -eq 'NotSigned' -and $unsigned) { return 'SIN FIRMA; SHA-256 independiente y permiso explicito' }
    throw "Firma Authenticode no aceptable ($($sig.Status)). Para instalador NAP sin firmar hace falta -AllowUnsigned."
}
function Save-Json([string]$path,$data) {
    Assert-Plain $path
    $tmp = "$path.tmp"
    if (Test-Path -LiteralPath $tmp) { throw "Escritura anterior pendiente, inspeccionar: $tmp" }
    $json = $data | ConvertTo-Json -Depth 12
    [IO.File]::WriteAllText($tmp, $json, [Text.UTF8Encoding]::new($false))
    if (Test-Path -LiteralPath $path) {
        [IO.File]::Replace($tmp, $path, $null)
    } else {
        [IO.File]::Move($tmp, $path)
    }
}
function Read-Json([string]$path) {
    Assert-Plain $path
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Recibo no encontrado: $path" }
    return (Get-Content -LiteralPath $path -Raw | ConvertFrom-Json)
}
function Lock-Updates {
    Assert-Plain $updates
    [IO.Directory]::CreateDirectory($updates) | Out-Null
    [IO.Directory]::CreateDirectory($staging) | Out-Null
    [IO.Directory]::CreateDirectory($rollback) | Out-Null
    $script:lockStream = [IO.File]::Open((Join-Path $updates 'nap07.lock'), [IO.FileMode]::OpenOrCreate,
        [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
}
function Require-Space([long]$size) {
    if ($Fault -eq 'DiskFull') { throw 'Simulacion de disco lleno ANTES de cualquier instalacion.' }
    $drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($updates))
    $required = [Math]::Max(512MB, ($size * 4))
    if ($drive.AvailableFreeSpace -lt $required) {
        throw "Espacio libre insuficiente para staging + instalacion + rollback ($required bytes necesarios)."
    }
}
function Read-InstalledVersion {
    $app = Join-Path $install 'NAP.App.exe'
    Assert-Plain $app
    if (-not (Test-Path -LiteralPath $app -PathType Leaf)) {
        throw 'NAP no esta instalado en la ruta fija de NAP06.'
    }
    $raw = [Diagnostics.FileVersionInfo]::GetVersionInfo($app).ProductVersion
    if ($raw -notmatch '^(\d+\.\d+\.\d+)') { throw "Version local no reconocible: $raw" }
    return $Matches[1]
}
function Verify-Installed([string]$expected) {
    $actual = Read-InstalledVersion
    if ($actual -ne $expected) { throw "Version instalada $actual, esperada $expected" }
    $hostExe = Join-Path $install 'host/NAP.AutomationHost.exe'
    if (-not (Test-Path -LiteralPath $hostExe -PathType Leaf)) { throw 'Falta AutomationHost instalado.' }
    $hostVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($hostExe).ProductVersion
    if ($hostVersion -notmatch "^$([regex]::Escape($expected))([.+]|$)") {
        throw "Version de Host no coincide: $hostVersion"
    }
}
function Assert-Closed {
    # NAP02 deja el Host vivo al cerrar WPF. La parada/drain debe solicitarse
    # desde Ajustes > Detener automatizacion; nunca matar el proceso ni tocar IPC ajeno.
    $running = @(Get-CimInstance Win32_Process -Filter "Name='NAP.App.exe' OR Name='NAP.AutomationHost.exe'")
    $prefix = $install.TrimEnd('\') + '\'
    foreach ($process in $running) {
        if (-not $process.ExecutablePath) { throw "Proceso NAP de identidad indeterminada (PID $($process.ProcessId)); actualizacion bloqueada." }
        $path = [IO.Path]::GetFullPath([string]$process.ExecutablePath)
        if ($path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "NAP esta ejecutandose (PID $($process.ProcessId)). Detener Host desde la interfaz, esperar drenaje y cerrar WPF antes de actualizar."
        }
    }
}
function Get-Release([string]$v,[string]$channel) {
    $headers = @{ 'User-Agent'='NAP07-explicit-update'; 'Accept'='application/vnd.github+json'; 'X-GitHub-Api-Version'='2022-11-28' }
    if ($v) {
        $release = Invoke-RestMethod -Uri "$api/releases/tags/v$v" -Headers $headers -TimeoutSec 30
        $releases = @($release)
    } else {
        $releases = @(Invoke-RestMethod -Uri "$api/releases?per_page=30" -Headers $headers -TimeoutSec 30)
    }
    foreach ($r in $releases) {
        if ($r.draft -or ($channel -eq 'stable' -and $r.prerelease) -or
            ($channel -eq 'preview' -and -not $r.prerelease)) { continue }
        $tag = [string]$r.tag_name
        if ($tag -notmatch '^v(\d+\.\d+\.\d+)$') { continue }
        $num = $Matches[1]
        if ($v -and $num -ne $v) { continue }
        $file = "NAP-Setup-$num.exe"
        $asset = @($r.assets | Where-Object { $_.name -ceq $file })
        if ($asset.Count -ne 1) { continue }
        $expected = "https://github.com/$repo/releases/download/v$num/$file"
        if ([string]$asset[0].browser_download_url -cne $expected) { throw 'URL de descarga no coincide con GitHub Releases del repositorio fijado.' }
        if ([long]$asset[0].size -lt 1024 -or [long]$asset[0].size -gt 1073741824) { throw 'Tamano de installer fuera de limites.' }
        return [pscustomobject]@{ Version=$num; ReleaseId=[string]$r.id; AssetId=[string]$asset[0].id;
            Size=[long]$asset[0].size; Url=$expected; Channel=$channel }
    }
    throw "No se encontro una Release valida en el canal $channel$(if($v){" para v$v"})."
}
function Run-Setup([string]$path) {
    Assert-Plain $path
    $p = Start-Process -FilePath $path -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES',
        '/NORESTART','/NOCLOSEAPPLICATIONS') -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "Inno Setup termino con codigo $($p.ExitCode)" }
}
function Restore-Previous($tx) {
    $old = Join-Path $rollback "NAP-Setup-$($tx.PreviousVersion).exe"
    [void](Assert-TrustedFile $old ([string]$tx.PreviousSha256) -unsigned:$AllowUnsigned)
    Assert-Closed
    $tx.State='restoring'; Save-Json $journalFile $tx
    Run-Setup $old
    Verify-Installed ([string]$tx.PreviousVersion)
    $tx.State='rolled_back'; Save-Json $journalFile $tx
}
try {
    if ($Action -eq 'Check') {
        $current = try { Read-InstalledVersion } catch { 'no detectada' }
        $r = Get-Release '' $Channel
        Write-Host "NAP instalado: $current; canal: $Channel; Release: v$($r.Version)"
        Write-Host "Fuente fija: https://github.com/$repo/releases"
        Write-Host 'NO SE HA DESCARGADO NI EJECUTADO NADA. Se requiere SHA-256 independiente para Stage y confirmacion expresa para Apply.'
        return
    }
    Lock-Updates
    if ($Action -eq 'Stage') {
        if (-not $Version -or -not $TrustedSha256) { throw 'Stage exige -Version y -TrustedSha256 independiente.' }
        if ([version]$Version -le [version](Read-InstalledVersion)) { throw 'Solo se admite version superior a la instalada.' }
        $target = Join-Path $staging "NAP-Setup-$Version.exe"
        $receiptPath = Join-Path $staging "NAP-Setup-$Version.receipt.json"
        if ((Test-Path -LiteralPath $journalFile) -and (Read-Json $journalFile).State -in @('prepared','applying','restoring','recovery_needed')) {
            throw 'Hay una transaccion pendiente de recuperacion. No se permite un nuevo staging.'
        }
        if ($FixtureSetup) {
            $release = [pscustomobject]@{ Version=$Version; ReleaseId='CI-FIXTURE'; AssetId='CI-FIXTURE'; Size=(Get-Item -LiteralPath $FixtureSetup).Length; Url='CI-LOCAL'; Channel=$Channel }
        } else { $release = Get-Release $Version $Channel }
        Require-Space $release.Size
        if (Test-Path -LiteralPath $target) {
            [void](Assert-TrustedFile $target $TrustedSha256 -unsigned:$AllowUnsigned)
        } else {
            $partial = "$target.partial"
            if (Test-Path -LiteralPath $partial) { throw "Descarga interrumpida; revisar y borrar manualmente: $partial" }
            if ($FixtureSetup) { Copy-Item -LiteralPath $FixtureSetup -Destination $partial }
            else { Invoke-WebRequest -Uri $release.Url -OutFile $partial -TimeoutSec 600 -UseBasicParsing }
            try {
                if ((Get-Item -LiteralPath $partial).Length -ne $release.Size) { throw 'Tamano descargado diferente al asset de GitHub.' }
                [void](Assert-TrustedFile $partial $TrustedSha256 -unsigned:$AllowUnsigned)
                [IO.File]::Move($partial,$target)
            } finally {
                if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial -Force }
            }
        }
        $receipt = [ordered]@{ Schema=1; Repository=$repo; Channel=$Channel; Version=$Version; TrustedSha256=$TrustedSha256.ToLowerInvariant();
            ReleaseId=$release.ReleaseId; AssetId=$release.AssetId; State='staged'; Utc=[DateTime]::UtcNow.ToString('o') }
        Save-Json $receiptPath $receipt
        $tool = Join-Path $updates 'tools'
        Assert-Plain $tool
        [IO.Directory]::CreateDirectory($tool) | Out-Null
        $this = [IO.Path]::GetFullPath($MyInvocation.MyCommand.Path)
        $toolCopy = Join-Path $tool 'NAP07.Update.ps1'
        if (-not $this.Equals($toolCopy,[StringComparison]::OrdinalIgnoreCase)) {
            Copy-Item -LiteralPath $this -Destination $toolCopy -Force
        }
        Write-Host "STAGED v$Version; SHA-256 comprobado; NO EJECUTADO."
        Write-Host "Para aplicar, usar copia externa al instalador: $toolCopy"
        return
    }
    $tx = $null
    if ($Action -eq 'Apply') {
        if (-not $Version -or -not $TrustedSha256 -or -not $PreviousSetup -or -not $PreviousSha256) {
            throw 'Apply requiere version, SHA nuevo independiente, Setup anterior y SHA anterior independiente.'
        }
        if ($Confirm -cne "APLICAR-$Version") { throw "Confirmacion necesaria: -Confirm APLICAR-$Version" }
        $this = [IO.Path]::GetFullPath($MyInvocation.MyCommand.Path)
        if ($this.StartsWith($install.TrimEnd('\') + '\',[StringComparison]::OrdinalIgnoreCase)) {
            throw "Ejecutar la copia en $updates\tools, no un script dentro de la instalacion sustituible."
        }
        $oldVersion = Read-InstalledVersion
        if ([version]$Version -le [version]$oldVersion) { throw 'No se permite actualizar a una version igual o inferior.' }
        $receipt = Read-Json (Join-Path $staging "NAP-Setup-$Version.receipt.json")
        if ($receipt.Schema -ne 1 -or $receipt.Repository -cne $repo -or $receipt.Version -cne $Version -or
            $receipt.TrustedSha256 -cne $TrustedSha256.ToLowerInvariant() -or $receipt.State -cne 'staged') {
            throw 'Recibo de staging no corresponde a los datos confirmados.'
        }
        $new = Join-Path $staging "NAP-Setup-$Version.exe"
        [void](Assert-TrustedFile $new $TrustedSha256 -unsigned:$AllowUnsigned)
        if ([IO.Path]::GetFileName($PreviousSetup) -cne "NAP-Setup-$oldVersion.exe") {
            throw 'El instalador de recuperacion no corresponde a la version actualmente instalada.'
        }
        [void](Assert-TrustedFile $PreviousSetup $PreviousSha256 -unsigned:$AllowUnsigned)
        Assert-Closed
        Require-Space ((Get-Item -LiteralPath $new).Length + (Get-Item -LiteralPath $PreviousSetup).Length)
        if (Test-Path -LiteralPath $journalFile) {
            $previousTx = Read-Json $journalFile
            if ($previousTx.State -notin @('committed','rolled_back')) { throw 'Transaccion anterior sin recuperar.' }
        }
        $old = Join-Path $rollback "NAP-Setup-$oldVersion.exe"
        if (Test-Path -LiteralPath $old) {
            [void](Assert-TrustedFile $old $PreviousSha256 -unsigned:$AllowUnsigned)
        } else { Copy-Item -LiteralPath $PreviousSetup -Destination $old }
        [void](Assert-TrustedFile $old $PreviousSha256 -unsigned:$AllowUnsigned)
        $tx = [ordered]@{ Schema=1; State='prepared'; PreviousVersion=$oldVersion; NewVersion=$Version;
            PreviousSha256=$PreviousSha256.ToLowerInvariant(); NewSha256=$TrustedSha256.ToLowerInvariant();
            Channel=$Channel; CreatedUtc=[DateTime]::UtcNow.ToString('o'); Source='explicit-human-confirmation' }
        Save-Json $journalFile $tx
        if ($Fault -eq 'BeforeSetup') { throw 'Fallo sintetico antes de ejecutar el instalador; Recover permite volver al estado verificado.' }
        $tx.State='applying'; Save-Json $journalFile $tx
        try {
            Run-Setup $new
            if ($Fault -eq 'AfterSetup') { throw 'Fallo sintetico tras instalacion nueva.' }
            Verify-Installed $Version
            $tx.State='committed'; Save-Json $journalFile $tx
            Write-Host "NAP07 PASS: actualizado a v$Version, binarios App+Host comprobados, datos no manipulados. Abrir WPF manualmente."
        } catch {
            $reason = $_.Exception.Message
            $tx.State='recovery_needed'; Save-Json $journalFile $tx
            try {
                Restore-Previous $tx
                throw "NAP07 ACTUALIZACION FALLIDA; rollback verificado a v$oldVersion. Causa: $reason"
            } catch {
                if ($tx.State -ne 'rolled_back') {
                    $tx.State='recovery_needed'
                    Save-Json $journalFile $tx
                    throw "NAP07 RECUPERACION PENDIENTE: $reason; fallo de recuperacion: $($_.Exception.Message). NO abrir NAP. Ejecutar -Action Recover."
                }
                throw
            }
        }
        return
    }
    $tx = Read-Json $journalFile
    if ($tx.Schema -ne 1 -or $tx.PreviousVersion -notmatch '^\d+\.\d+\.\d+$' -or
        $tx.NewVersion -notmatch '^\d+\.\d+\.\d+$' -or
        $tx.PreviousSha256 -notmatch '^[0-9a-f]{64}$' -or $tx.NewSha256 -notmatch '^[0-9a-f]{64}$') {
        throw 'Journal no conforme: no aplicar recuperacion.'
    }
    if ($Action -eq 'Recover') {
        if ($tx.State -notin @('prepared','applying','restoring','recovery_needed')) { throw 'No hay transaccion interrumpida pendiente.' }
        if ($Confirm -cne "RECUPERAR-$($tx.PreviousVersion)") { throw "Exige -Confirm RECUPERAR-$($tx.PreviousVersion)" }
        Restore-Previous $tx
        Write-Host "RECUPERACION COMPLETADA: v$($tx.PreviousVersion). Datos no modificados."
        return
    }
    if ($Action -eq 'Rollback') {
        if ($tx.State -cne 'committed') { throw 'Solo se revierte una actualizacion confirmada; Recover cubre fallos.' }
        if ($Confirm -cne "REVERTIR-$($tx.PreviousVersion)" -or -not $AcknowledgeSchemaRisk) {
            throw "Reversion manual requiere -Confirm REVERTIR-$($tx.PreviousVersion) y -AcknowledgeSchemaRisk (no degrada ningun esquema)."
        }
        # Tras abrir la nueva app podria haber migraciones incompatibles. La aceptacion
        # explicita no altera SQLite: exige respaldo y revision de compatibilidad externa.
        Restore-Previous $tx
        Write-Host "ROLLBACK CONTROLADO: v$($tx.PreviousVersion). Verificar compatibilidad de catálogos antes de reabrir."
    }
} finally {
    if ($lockStream) { $lockStream.Dispose() }
}

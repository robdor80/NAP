[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SourcePng,
    [Parameter(Mandatory)][string]$DestinationIco
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'La generacion del icono NAP06 solo funciona en Windows.' }
Add-Type -AssemblyName System.Drawing
$source = [System.Drawing.Image]::FromFile((Resolve-Path -LiteralPath $SourcePng).Path)
try {
    $bitmap = [System.Drawing.Bitmap]::new(256, 256, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $graphics.DrawImage($source, 0, 0, 256, 256)
        }
        finally { $graphics.Dispose() }
        $stream = [System.IO.MemoryStream]::new()
        try {
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            $png = $stream.ToArray()
            $parent = Split-Path -Parent $DestinationIco
            [System.IO.Directory]::CreateDirectory($parent) | Out-Null
            $file = [System.IO.File]::Create($DestinationIco)
            try {
                $writer = [System.IO.BinaryWriter]::new($file)
                try {
                    $writer.Write([uint16]0)
                    $writer.Write([uint16]1)
                    $writer.Write([uint16]1)
                    $writer.Write([byte]0)
                    $writer.Write([byte]0)
                    $writer.Write([byte]0)
                    $writer.Write([byte]0)
                    $writer.Write([uint16]1)
                    $writer.Write([uint16]32)
                    $writer.Write([uint32]$png.Length)
                    $writer.Write([uint32]22)
                    $writer.Write($png)
                    $writer.Flush()
                }
                finally { $writer.Dispose() }
            }
            finally { $file.Dispose() }
        }
        finally { $stream.Dispose() }
    }
    finally { $bitmap.Dispose() }
}
finally { $source.Dispose() }

#Requires -Version 5.1
<#
.SYNOPSIS
    Renders assets/icon.svg into src/MasterImage.App/appicon.ico.

.DESCRIPTION
    The geometry below is a transcription of assets/icon.svg, which is the human-readable master.
    Keep the two in step: there is no SVG rasteriser on a stock Windows box, and adding one as a
    build dependency to redraw two triangles isn't worth it.

    Each size is rasterised from the vector geometry at its own resolution rather than downscaled
    from a single large bitmap — that's what keeps the 16px version, the one that actually shows in
    the taskbar, legible instead of mushy.
#>
[CmdletBinding()]
param(
    [string] $OutputPath = (Join-Path $PSScriptRoot '..\src\MasterImage.App\appicon.ico')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase

# 256 is the design space; every other size is a clean scale of it.
$DesignSize = 256
$Sizes = @(16, 32, 48, 64, 128, 256)

$NearPeak = 'M98,44 L186,212 L16,212 Z'
$FarPeak = 'M178,104 L240,212 L114,212 Z'
$CornerRadius = 48
$SeamWidth = 12

function New-IconPng {
    param([int] $Size)

    $visual = New-Object System.Windows.Media.DrawingVisual
    $ctx = $visual.RenderOpen()

    $scale = $Size / $DesignSize
    $ctx.PushTransform((New-Object System.Windows.Media.ScaleTransform -ArgumentList $scale, $scale))

    $black = [System.Windows.Media.Brushes]::Black
    $white = [System.Windows.Media.Brushes]::White

    $bounds = New-Object System.Windows.Rect -ArgumentList 0, 0, $DesignSize, $DesignSize
    $ctx.DrawRoundedRectangle($black, $null, $bounds, $CornerRadius, $CornerRadius)

    $ctx.DrawGeometry($white, $null, [System.Windows.Media.Geometry]::Parse($NearPeak))

    # Drawn second, with a black stroke, so it cuts the seam over the near peak where they overlap.
    $seam = New-Object System.Windows.Media.Pen -ArgumentList $black, $SeamWidth
    $seam.LineJoin = [System.Windows.Media.PenLineJoin]::Round
    $ctx.DrawGeometry($white, $seam, [System.Windows.Media.Geometry]::Parse($FarPeak))

    $ctx.Pop()
    $ctx.Close()

    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap -ArgumentList `
        $Size, $Size, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)

    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder

    # [void] matters: Frames.Add returns the insertion index, and an uncaptured value would join
    # this function's output — the caller would get @(0, byte[]) instead of the PNG.
    [void] $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))

    $stream = New-Object System.IO.MemoryStream
    $encoder.Save($stream)
    return , $stream.ToArray()
}

$frames = @()
foreach ($size in $Sizes) {
    $png = New-IconPng -Size $size

    # A malformed .ico fails silently — Windows just shows a default icon — so catch it here,
    # where the cause is still obvious, rather than wondering why the taskbar looks wrong.
    if ($png -isnot [byte[]] -or $png.Length -lt 100) {
        throw "Rendering the ${size}px frame produced $($png.GetType().Name), $($png.Length) bytes; expected a byte[] of at least 100."
    }

    $frames += , $png
}

# ICONDIR, then one 16-byte ICONDIRENTRY per size, then the PNG payloads back to back.
$ico = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter -ArgumentList $ico

$writer.Write([uint16] 0)             # reserved
$writer.Write([uint16] 1)             # type: 1 = icon
$writer.Write([uint16] $Sizes.Count)

$offset = 6 + (16 * $Sizes.Count)
for ($i = 0; $i -lt $Sizes.Count; $i++) {
    # The dimension fields are a single byte each, so 256 doesn't fit — 0 is the agreed escape
    # hatch for it. Writing 256 here truncates to 0 anyway, but silently and by accident.
    $dimension = if ($Sizes[$i] -ge 256) { 0 } else { $Sizes[$i] }

    $writer.Write([byte] $dimension)   # width
    $writer.Write([byte] $dimension)   # height
    $writer.Write([byte] 0)            # palette size; 0 = truecolour
    $writer.Write([byte] 0)            # reserved
    $writer.Write([uint16] 1)          # colour planes
    $writer.Write([uint16] 32)         # bits per pixel
    $writer.Write([uint32] $frames[$i].Length)
    $writer.Write([uint32] $offset)

    $offset += $frames[$i].Length
}

foreach ($frame in $frames) {
    $writer.Write($frame)
}
$writer.Flush()

$resolved = [System.IO.Path]::GetFullPath($OutputPath)
[System.IO.File]::WriteAllBytes($resolved, $ico.ToArray())

Write-Host "Wrote $resolved ($($ico.Length) bytes, $($Sizes.Count) sizes: $($Sizes -join ', '))"

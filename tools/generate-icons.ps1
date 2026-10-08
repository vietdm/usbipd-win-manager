<#
.SYNOPSIS
    Renders the USBIPD Manager icons as multi-size .ico files (16, 20, 24, 32, 48, 64, 256).

.DESCRIPTION
    Windows PowerShell 5.1 + System.Drawing. Every size is drawn natively (not downscaled) so small sizes stay crisp.
    Output: src/UsbipdManager/Assets/{app, tray-windows, tray-wsl, tray-warning}.ico
    Frames below 256 px are stored as 32-bit DIBs, the 256 px frame as PNG (standard Vista+ layout).

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\generate-icons.ps1
#>
[CmdletBinding()]
param(
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
if (-not $OutputDirectory) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $OutputDirectory = Join-Path $scriptDir '..\src\UsbipdManager\Assets'
}
Add-Type -AssemblyName System.Drawing

$Sizes = @(16, 20, 24, 32, 48, 64, 256)

function New-Color([string]$hex, [int]$alpha = 255) {
    $hex = $hex.TrimStart('#')
    return [System.Drawing.Color]::FromArgb($alpha,
        [Convert]::ToInt32($hex.Substring(0, 2), 16),
        [Convert]::ToInt32($hex.Substring(2, 2), 16),
        [Convert]::ToInt32($hex.Substring(4, 2), 16))
}

function New-RoundedRectPath([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [single]($r * 2)
    if ($d -le 0) {
        $path.AddRectangle((New-Object System.Drawing.RectangleF($x, $y, $w, $h)))
        return $path
    }
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-Pen([System.Drawing.Color]$color, [single]$width) {
    $pen = New-Object System.Drawing.Pen($color, $width)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    return $pen
}

# Stroke width in pixels: whole pixels at small sizes keep lines sharp.
function Get-Stroke([int]$size, [single]$units) {
    $px = $units * $size / 32.0
    if ($size -le 32) { return [single][Math]::Max(1, [Math]::Round($px)) }
    return [single]$px
}

function Draw-Background($g, [int]$size, [string]$hex) {
    $inset = if ($size -le 20) { 0 } else { [single]($size / 32.0) }
    $radius = [single]([Math]::Max(2, $size * 0.2))
    $path = New-RoundedRectPath $inset $inset ($size - 2 * $inset) ($size - 2 * $inset) $radius
    $brush = New-Object System.Drawing.SolidBrush (New-Color $hex)
    $g.FillPath($brush, $path)
    $brush.Dispose(); $path.Dispose()
}

# USB trident on a slate tile (application icon).
function Draw-App($g, [int]$size) {
    Draw-Background $g $size '0F172A'
    $u = $size / 32.0
    $green = New-Color '22C55E'
    $pen = New-Pen $green (Get-Stroke $size 2.4)
    $brush = New-Object System.Drawing.SolidBrush $green

    $cx = [single](16 * $u)
    $g.DrawLine($pen, $cx, [single](9 * $u), $cx, [single](23 * $u))
    # Arrow head
    $head = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF($cx, [single](4.5 * $u))),
        (New-Object System.Drawing.PointF([single](12.5 * $u), [single](9.5 * $u))),
        (New-Object System.Drawing.PointF([single](19.5 * $u), [single](9.5 * $u))))
    $g.FillPolygon($brush, $head)
    # Base plug
    $r = [single](3.2 * $u)
    $g.FillEllipse($brush, [single]($cx - $r), [single](25 * $u - $r), [single](2 * $r), [single](2 * $r))
    # Left branch ending in a square
    $g.DrawLines($pen, [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF($cx, [single](19.5 * $u))),
        (New-Object System.Drawing.PointF([single](10 * $u), [single](15.5 * $u))),
        (New-Object System.Drawing.PointF([single](10 * $u), [single](12.5 * $u)))))
    $sq = [single](4.4 * $u)
    $g.FillRectangle($brush, [single](10 * $u - $sq / 2), [single](9 * $u), $sq, $sq)
    # Right branch ending in a dot
    $g.DrawLines($pen, [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF($cx, [single](17 * $u))),
        (New-Object System.Drawing.PointF([single](22 * $u), [single](13 * $u))),
        (New-Object System.Drawing.PointF([single](22 * $u), [single](11.5 * $u)))))
    $dr = [single](2.4 * $u)
    $g.FillEllipse($brush, [single](22 * $u - $dr), [single](10 * $u - $dr), [single](2 * $dr), [single](2 * $dr))

    $pen.Dispose(); $brush.Dispose()
}

# Four window panes on a blue tile (Windows mode). Integer rectangles keep it crisp at 16 px.
function Draw-TrayWindows($g, [int]$size) {
    Draw-Background $g $size '1D4ED8'
    $margin = [int][Math]::Round($size * 0.22)
    $gap = [int][Math]::Max(1, [Math]::Round($size * 0.07))
    $pane = [int][Math]::Floor(($size - 2 * $margin - $gap) / 2)
    $start = [int][Math]::Floor(($size - (2 * $pane + $gap)) / 2)
    $oldMode = $g.SmoothingMode
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None
    $brush = New-Object System.Drawing.SolidBrush (New-Color 'FFFFFF')
    foreach ($ix in 0, 1) {
        foreach ($iy in 0, 1) {
            $g.FillRectangle($brush, $start + $ix * ($pane + $gap), $start + $iy * ($pane + $gap), $pane, $pane)
        }
    }
    $g.SmoothingMode = $oldMode
    $brush.Dispose()
}

# Terminal prompt ">_" on a green tile (WSL2 mode).
function Draw-TrayWsl($g, [int]$size) {
    Draw-Background $g $size '15803D'
    $u = $size / 32.0
    $pen = New-Pen (New-Color 'FFFFFF') (Get-Stroke $size 3.2)
    $g.DrawLines($pen, [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF([single](8 * $u), [single](9.5 * $u))),
        (New-Object System.Drawing.PointF([single](15 * $u), [single](16 * $u))),
        (New-Object System.Drawing.PointF([single](8 * $u), [single](22.5 * $u)))))
    $g.DrawLine($pen, [single](17.5 * $u), [single](22.5 * $u), [single](24.5 * $u), [single](22.5 * $u))
    $pen.Dispose()
}

# Amber triangle with "!" (unsupported machine / usbipd not ready / error).
function Draw-TrayWarning($g, [int]$size) {
    $u = $size / 32.0
    $tri = New-Object System.Drawing.Drawing2D.GraphicsPath
    $tri.AddPolygon([System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF([single](16 * $u), [single](1.5 * $u))),
        (New-Object System.Drawing.PointF([single](31 * $u), [single](29.5 * $u))),
        (New-Object System.Drawing.PointF([single](1 * $u), [single](29.5 * $u)))))
    $fill = New-Object System.Drawing.SolidBrush (New-Color 'F59E0B')
    $g.FillPath($fill, $tri)
    $outline = New-Pen (New-Color '92400E') ([single][Math]::Max(1, $size / 32.0))
    $g.DrawPath($outline, $tri)
    $dark = New-Color '1C1917'
    $pen = New-Pen $dark (Get-Stroke $size 3.4)
    $g.DrawLine($pen, [single](16 * $u), [single](11 * $u), [single](16 * $u), [single](19.5 * $u))
    $brush = New-Object System.Drawing.SolidBrush $dark
    $dot = [single]([Math]::Max(1.5, 3.6 * $u))
    $g.FillEllipse($brush, [single](16 * $u - $dot / 2), [single](24.2 * $u - $dot / 2), $dot, $dot)
    $tri.Dispose(); $fill.Dispose(); $outline.Dispose(); $pen.Dispose(); $brush.Dispose()
}

function Render-Frame([scriptblock]$draw, [int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    & $draw $g $size
    $g.Dispose()
    return $bmp
}

function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
    $size = $bmp.Width
    $rect = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = $data.Stride
    $pixels = New-Object byte[] ($stride * $size)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
    $bmp.UnlockBits($data)

    $maskStride = [int]([Math]::Ceiling($size / 32.0) * 4)
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter($ms)
    $w.Write([int]40); $w.Write([int]$size); $w.Write([int]($size * 2))
    $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]0)
    $w.Write([int]($size * $size * 4 + $maskStride * $size))
    $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0)
    # DIB rows are stored bottom-up.
    for ($y = $size - 1; $y -ge 0; $y--) { $w.Write($pixels, $y * $stride, $size * 4) }
    $w.Write((New-Object byte[] ($maskStride * $size)))
    $w.Flush()
    return $ms.ToArray()
}

function Get-PngBytes([System.Drawing.Bitmap]$bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return $ms.ToArray()
}

function Write-Icon([string]$path, [scriptblock]$draw) {
    $frames = @()
    foreach ($s in $Sizes) {
        $bmp = Render-Frame $draw $s
        $bytes = if ($s -ge 256) { Get-PngBytes $bmp } else { Get-DibBytes $bmp }
        $bmp.Dispose()
        $frames += , @{ Size = $s; Bytes = $bytes }
    }

    $fs = [System.IO.File]::Create($path)
    $w = New-Object System.IO.BinaryWriter($fs)
    $w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($f in $frames) {
        $dim = if ($f.Size -ge 256) { 0 } else { $f.Size }
        $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([int16]1); $w.Write([int16]32)
        $w.Write([int]$f.Bytes.Length); $w.Write([int]$offset)
        $offset += $f.Bytes.Length
    }
    foreach ($f in $frames) { $w.Write([byte[]]$f.Bytes) }
    $w.Flush(); $w.Dispose(); $fs.Dispose()
    Write-Host "Wrote $path"
}

if (-not (Test-Path $OutputDirectory)) { New-Item -ItemType Directory -Path $OutputDirectory | Out-Null }
$OutputDirectory = (Resolve-Path $OutputDirectory).Path

Write-Icon (Join-Path $OutputDirectory 'app.ico') ${function:Draw-App}
Write-Icon (Join-Path $OutputDirectory 'tray-windows.ico') ${function:Draw-TrayWindows}
Write-Icon (Join-Path $OutputDirectory 'tray-wsl.ico') ${function:Draw-TrayWsl}
Write-Icon (Join-Path $OutputDirectory 'tray-warning.ico') ${function:Draw-TrayWarning}

<#
  Generates the PWA / home-screen icons for the phone page from the pigeon silhouette.
  The geometry matches the tray icon (src/Hatoclip/assets/hatoclip.ico) and the inline SVG
  mark in src/Hatoclip/Web/index.html (all on a 48x48 grid). Edit the shapes below and rerun
  to rebrand:

    powershell -ExecutionPolicy Bypass -File tools\make-web-icons.ps1

  Output (embedded by Hatoclip.csproj via Web\**):
    src/Hatoclip/Web/icon.png             512x512  rounded square (manifest, purpose "any")
    src/Hatoclip/Web/icon-192.png         192x192  rounded square (manifest, purpose "any")
    src/Hatoclip/Web/apple-touch-icon.png 180x180  full-bleed opaque square (iOS rounds it itself)
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$outDir = Join-Path $PSScriptRoot '..\src\Hatoclip\Web'

$bgColor    = [System.Drawing.ColorTranslator]::FromHtml('#dfe7f1')
$bodyColor  = [System.Drawing.ColorTranslator]::FromHtml('#4a628c')
$wingColor  = [System.Drawing.ColorTranslator]::FromHtml('#96accd')
$beakColor  = [System.Drawing.ColorTranslator]::FromHtml('#f0a032')
$eyeColor   = [System.Drawing.Color]::White
$pupilColor = [System.Drawing.ColorTranslator]::FromHtml('#1d2433')

function New-Pts([double[]]$xy) {
    $pts = @()
    for ($i = 0; $i -lt $xy.Length; $i += 2) { $pts += New-Object System.Drawing.PointF ([single]$xy[$i]), ([single]$xy[$i + 1]) }
    return ,([System.Drawing.PointF[]]$pts)
}

# Neck: SVG path "M30,10 Q23,17 14,23.5 L30,34 L38.5,27 Q41.5,23 41,19 Z" (quadratic curves as cubic Beziers).
function Add-Quad($path, [single]$x0, [single]$y0, [single]$qx, [single]$qy, [single]$x1, [single]$y1) {
    $path.AddBezier($x0, $y0, ($x0 + 2 / 3 * ($qx - $x0)), ($y0 + 2 / 3 * ($qy - $y0)), ($x1 + 2 / 3 * ($qx - $x1)), ($y1 + 2 / 3 * ($qy - $y1)), $x1, $y1)
}

function New-NeckPath {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    Add-Quad $p 30 10 23 17 14 23.5
    $p.AddLine(14, 23.5, 30, 34)
    $p.AddLine(30, 34, 38.5, 27)
    Add-Quad $p 38.5 27 41.5 23 41 19
    $p.CloseFigure()
    return $p
}

function Draw-Pigeon([System.Drawing.Graphics]$g) {
    # 48x48 grid, same shapes as the inline SVG in index.html.
    $body = New-Object System.Drawing.SolidBrush $bodyColor
    $g.FillPolygon($body, (New-Pts @(13,30, 2,35.5, 4,37, 10,37.5, 18,37)))       # tail
    $g.FillEllipse($body, 9, 19.5, 30, 20)                                          # body  (cx 24, cy 29.5, rx 15, ry 10)
    $g.FillPath($body, (New-NeckPath))                                            # neck
    $g.FillEllipse($body, 27.5, 8, 15, 15)                                          # head  (cx 35, cy 15.5, r 7.5)
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $wingColor), 14.2, 23, 19.6, 10)  # wing (cx 24, cy 28)
    $g.FillPolygon((New-Object System.Drawing.SolidBrush $beakColor), (New-Pts @(41.5,14.5, 47,16.5, 41.5,18.5)))  # beak
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $eyeColor), 34.1, 12.1, 4.8, 4.8)    # eye   (cx 36.5, cy 14.5, r 2.4)
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $pupilColor), 36, 13.5, 2, 2)        # pupil (cx 37, cy 14.5, r 1)
}

function New-RoundedRect([single]$size, [single]$radius) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $p.AddArc(0, 0, $d, $d, 180, 90)
    $p.AddArc($size - $d, 0, $d, $d, 270, 90)
    $p.AddArc($size - $d, $size - $d, $d, $d, 0, 90)
    $p.AddArc(0, $size - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Save-Icon([int]$size, [string]$name, [bool]$rounded) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    $bg = New-Object System.Drawing.SolidBrush $bgColor
    if ($rounded) { $g.FillPath($bg, (New-RoundedRect $size ($size * 0.22))) }
    else { $g.FillRectangle($bg, 0, 0, $size, $size) }

    # Pigeon spans roughly x 2..47, y 8..40 of the grid (center ~24.5, 24). Scale it to ~76 % of the icon.
    $scale = $size * 0.76 / 45.0
    $g.TranslateTransform($size / 2.0, $size / 2.0)
    $g.ScaleTransform($scale, $scale)
    $g.TranslateTransform(-24.5, -24)
    Draw-Pigeon $g

    $g.Dispose()
    $path = Join-Path $outDir $name
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "Wrote $path ($size x $size)"
}

Save-Icon 512 'icon.png' $true
Save-Icon 192 'icon-192.png' $true
Save-Icon 180 'apple-touch-icon.png' $false

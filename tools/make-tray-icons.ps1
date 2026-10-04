<#
  Generates the three tray icons (16/24/32/48 px, 32-bit BMP entries with an AND mask) from the
  pigeon silhouette on a 32x32 grid:

    powershell -ExecutionPolicy Bypass -File tools\make-tray-icons.ps1

  Output (embedded by Hatoclip.csproj via assets\*.ico):
    src/Hatoclip/assets/hatoclip.ico           idle (also the exe / window / favicon icon)
    src/Hatoclip/assets/hatoclip-received.ico  shown for a few seconds after text arrives from the phone
                                               (orange envelope badge, top left)
    src/Hatoclip/assets/hatoclip-sent.ico      shown for a few seconds after the phone fetched the clipboard
                                               (teal outgoing-arrow badge, bottom right)

  The drawing code is deterministic: rerunning it reproduces the committed .ico files byte for byte.
  Edit the shapes below and rerun to rebrand.
#>
param(
    [string] $OutDir = (Join-Path $PSScriptRoot '..\src\Hatoclip\assets')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-PointF([single]$x, [single]$y) { New-Object System.Drawing.PointF($x, $y) }

# $Badge: 'none' | 'received' | 'sent'. The drawing order matters for byte-identical output.
function Draw-Pigeon([int]$size, [string]$Badge) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 32.0
    $g.ScaleTransform($s, $s)
    $body = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 74, 98, 140))
    $wing = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 150, 172, 205))
    $beak = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 240, 160, 50))
    # tail
    $g.FillPolygon($body, [System.Drawing.PointF[]]@((New-PointF 9 17), (New-PointF 1 23), (New-PointF 4 25), (New-PointF 12 21)))
    # body
    $g.FillEllipse($body, 6, 12, 20, 14)
    # head
    $g.FillEllipse($body, 17, 5, 11, 11)
    # beak
    $g.FillPolygon($beak, [System.Drawing.PointF[]]@((New-PointF 27 9), (New-PointF 31.5 11), (New-PointF 27 12.5)))
    # wing
    $g.FillEllipse($wing, 9, 15, 13, 7)

    if ($Badge -eq 'received') {
        # Letter (envelope) on an orange disc at the top left.
        $env = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 235, 90, 40))
        $g.FillEllipse($env, 0, 0, 15, 15)
        $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, 1.6)
        $g.DrawRectangle($pen, 3.5, 4.5, 8, 6)
        $g.DrawLines($pen, [System.Drawing.PointF[]]@((New-PointF 3.5 4.5), (New-PointF 7.5 8), (New-PointF 11.5 4.5)))
        $pen.Dispose()
    }

    # eye
    if ($size -ge 24) { $g.FillEllipse([System.Drawing.Brushes]::White, 22.5, 8, 3, 3) }

    if ($Badge -eq 'sent') {
        # Outgoing arrow (up-right) on a teal disc at the bottom right; the head and beak stay visible.
        $disc = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 0, 150, 160))
        $g.FillEllipse($disc, 17, 17, 15, 15)
        $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, 2.2)
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $g.DrawLine($pen, 20.5, 28.5, 28, 21)
        $g.DrawLines($pen, [System.Drawing.PointF[]]@((New-PointF 22.5 21), (New-PointF 28 21), (New-PointF 28 26.5)))
        $pen.Dispose()
    }

    $g.Dispose()
    return $bmp
}

function Get-IcoEntry([System.Drawing.Bitmap]$bmp) {
    $n = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter($ms)
    # BITMAPINFOHEADER (height doubled for the XOR + AND masks)
    $w.Write([int]40); $w.Write([int]$n); $w.Write([int]($n * 2)); $w.Write([int16]1); $w.Write([int16]32)
    $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0)
    for ($y = $n - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $n; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A)
        }
    }
    $rowBytes = [int]([math]::Ceiling($n / 32.0) * 4)
    for ($y = $n - 1; $y -ge 0; $y--) {
        $row = New-Object byte[] $rowBytes
        for ($x = 0; $x -lt $n; $x++) {
            if ($bmp.GetPixel($x, $y).A -eq 0) {
                $row[[int][math]::Floor($x / 8)] = $row[[int][math]::Floor($x / 8)] -bor (0x80 -shr ($x % 8))
            }
        }
        $w.Write($row)
    }
    $w.Flush()
    return , $ms.ToArray()
}

function Save-Ico([string]$path, [string]$Badge) {
    $sizes = @(16, 24, 32, 48)
    $entries = @()
    foreach ($sz in $sizes) {
        $bmp = Draw-Pigeon $sz $Badge
        $entries += , (Get-IcoEntry $bmp)
        $bmp.Dispose()
    }

    $fs = [System.IO.File]::Create($path)
    $w = New-Object System.IO.BinaryWriter($fs)
    $w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $sz = $sizes[$i]; $data = $entries[$i]
        $w.Write([byte]$sz); $w.Write([byte]$sz); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]$data.Length); $w.Write([int]$offset)
        $offset += $data.Length
    }
    foreach ($d in $entries) { $w.Write($d) }
    $w.Close()
    Write-Host "Wrote $path"
}

$OutDir = [System.IO.Path]::GetFullPath($OutDir)
Save-Ico (Join-Path $OutDir 'hatoclip.ico') 'none'
Save-Ico (Join-Path $OutDir 'hatoclip-received.ico') 'received'
Save-Ico (Join-Path $OutDir 'hatoclip-sent.ico') 'sent'

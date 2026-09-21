param([string]$OutPath = "D:\Work\Code\CCS_VP\WinOne\src\WinOne\Assets\app.ico")
Add-Type -AssemblyName System.Drawing

$size = 256
$bmp = New-Object System.Drawing.Bitmap($size, $size)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$g.TextRenderingHint = 'AntiAlias'

$g.Clear([System.Drawing.Color]::Transparent)

# rounded-rect gradient background
$path = New-Object System.Drawing.Drawing2D.GraphicsPath
$r = 56
$path.AddArc(0, 0, $r, $r, 180, 90)
$path.AddArc($size - $r, 0, $r, $r, 270, 90)
$path.AddArc($size - $r, $size - $r, $r, $r, 0, 90)
$path.AddArc(0, $size - $r, $r, $r, 90, 90)
$path.CloseFigure()

$brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    (New-Object System.Drawing.Point(0, 0)),
    (New-Object System.Drawing.Point($size, $size)),
    [System.Drawing.Color]::FromArgb(255, 0, 120, 212),
    [System.Drawing.Color]::FromArgb(255, 0, 90, 158))
$g.FillPath($brush, $path)

# white "W" glyph
$font = New-Object System.Drawing.Font('Segoe UI', 140, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$fmt = New-Object System.Drawing.StringFormat
$fmt.Alignment = 'Center'
$fmt.LineAlignment = 'Center'
$g.DrawString('W', $font, [System.Drawing.Brushes]::White,
    (New-Object System.Drawing.RectangleF(0, 4, $size, $size)), $fmt)

$g.Dispose()

# save PNG
New-Item -ItemType Directory -Force -Path (Split-Path $OutPath) | Out-Null
$pngStream = New-Object System.IO.MemoryStream
$bmp.Save($pngStream, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Save(($OutPath -replace '\.ico$', '.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

# wrap PNG into a valid .ico container (PNG entry, 256px)
$png = $pngStream.ToArray()
$pngStream.Dispose()
$ico = New-Object System.IO.MemoryStream
$w = [System.IO.BinaryWriter]::new($ico)
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]1)          # ICONDIR
$w.Write([byte]0); $w.Write([byte]0)                                    # 256px
$w.Write([byte]0); $w.Write([byte]0)                                    # colors, reserved
$w.Write([uint16]1); $w.Write([uint16]32)                               # planes, bpp
$w.Write([uint32]$png.Length)                                           # data size
$w.Write([uint32]22)                                                    # offset
$w.Write($png)
[System.IO.File]::WriteAllBytes($OutPath, $ico.ToArray())
$w.Dispose()
Write-Output "OK: $OutPath ($((Get-Item $OutPath).Length) bytes)"

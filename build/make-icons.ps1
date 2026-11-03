# 生成「凌雾跨设备传输」图标：Windows .ico + Android mipmap + 预览图
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root  = Split-Path -Parent $PSScriptRoot
$pcOut = Join-Path $root 'build\icon'
$andRes = Join-Path $root 'src\android\res'
New-Item -ItemType Directory -Path $pcOut -Force | Out-Null
New-Item -ItemType Directory -Path $andRes -Force | Out-Null

function New-LogoBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)

    $inset  = [double]$size * 0.03
    $radius = [double]$size * 0.235
    $rect   = New-Object System.Drawing.RectangleF($inset, $inset, ($size - 2*$inset), ($size - 2*$inset))
    $d      = [double]$radius * 2
    $path   = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc(($rect.Right - $d), $rect.Y, $d, $d, 270, 90)
    $path.AddArc(($rect.Right - $d), ($rect.Bottom - $d), $d, $d, 0, 90)
    $path.AddArc($rect.X, ($rect.Bottom - $d), $d, $d, 90, 90)
    $path.CloseFigure()

    # 底色渐变
    $c1 = [System.Drawing.Color]::FromArgb(255, 24, 33, 47)
    $c2 = [System.Drawing.Color]::FromArgb(255, 9, 14, 21)
    $br = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            (New-Object System.Drawing.PointF(0, 0)),
            (New-Object System.Drawing.PointF($size, $size)), $c1, $c2)
    $g.FillPath($br, $path)
    $br.Dispose()

    # 底部薄荷雾光
    $glowPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $glowPath.AddEllipse([double]($size * -0.15), [double]($size * 0.45), [double]($size * 1.30), [double]($size * 0.95))
    $pgb = New-Object System.Drawing.Drawing2D.PathGradientBrush($glowPath)
    $pgb.CenterColor  = [System.Drawing.Color]::FromArgb(150, 62, 224, 162)
    $pgb.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 62, 224, 162))
    $old = $g.Clip
    $g.SetClip($path, [System.Drawing.Drawing2D.CombineMode]::Intersect)
    $g.FillPath($pgb, $glowPath)
    $pgb.Dispose()
    $g.Clip = $old

    # 主体：凌（白）
    $fs = [float]($size * 0.475)
    $font = New-Object System.Drawing.Font('Microsoft YaHei', $fs, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = [System.Drawing.StringAlignment]::Center
    $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
    $shadowBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(110, 0, 0, 0))
    $textBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 246, 250, 252))
    $tRect = New-Object System.Drawing.RectangleF(0, [float]($size * -0.02), $size, $size)
    $g.DrawString('凌', $font, $shadowBrush, $tRect, $sf)
    $tRect.Offset([float]($size * -0.008), [float]($size * -0.018))
    $g.DrawString('凌', $font, $textBrush, $tRect, $sf)

    # 底部薄荷小横条
    $barW = [double]$size * 0.185
    $barH = [math]::Max(2.0, [double]$size * 0.038)
    $barX = ($size - $barW) / 2
    $barY = [double]$size * 0.775
    $barBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 62, 224, 162))
    $bar = New-Object System.Drawing.RectangleF($barX, $barY, $barW, $barH)
    $bp = New-Object System.Drawing.Drawing2D.GraphicsPath
    $bd = $barH
    $bp.AddArc($bar.X, $bar.Y, $bd, $bd, 180, 90)
    $bp.AddArc(($bar.Right - $bd), $bar.Y, $bd, $bd, 270, 90)
    $bp.AddArc(($bar.Right - $bd), ($bar.Bottom - $bd), $bd, $bd, 0, 90)
    $bp.AddArc($bar.X, ($bar.Bottom - $bd), $bd, $bd, 90, 90)
    $bp.CloseFigure()
    $g.FillPath($barBrush, $bp)
    $barBrush.Dispose(); $bp.Dispose()

    $font.Dispose(); $sf.Dispose(); $textBrush.Dispose(); $shadowBrush.Dispose(); $path.Dispose(); $glowPath.Dispose()
    $g.Dispose()
    return $bmp
}

# ---- Android mipmap ----
$densities = @{ 'mdpi' = 48; 'hdpi' = 72; 'xhdpi' = 96; 'xxhdpi' = 144; 'xxxhdpi' = 192 }
foreach ($k in $densities.Keys) {
    $dir = Join-Path $andRes "mipmap-$k"
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    $bmp = New-LogoBitmap $densities[$k]
    $bmp.Save((Join-Path $dir 'ic_launcher.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}
"Android mipmap 完成: " + ($densities.Keys -join ', ')

# ---- 预览图 512 ----
$big = New-LogoBitmap 512
$big.Save((Join-Path $root 'build\icon\preview-512.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$big.Save((Join-Path $pcOut 'app-512.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$big.Dispose()
"预览图: build\icon\preview-512.png"

# ---- Windows .ico（多尺寸，BMP 条目，兼容性最好）----
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$blobs = @()
foreach ($s in $sizes) {
    $bmp = New-LogoBitmap $s
    $w = $bmp.Width; $h = $bmp.Height
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    # BITMAPINFOHEADER
    $bw.Write([int]40); $bw.Write([int]$w); $bw.Write([int]($h * 2))
    $bw.Write([int16]1); $bw.Write([int16]32); $bw.Write([int]0)
    $bw.Write([int]($w * $h * 4)); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)
    # XOR 位图（自下而上，BGRA）
    for ($y = $h - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $w; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $bw.Write([byte]$c.B); $bw.Write([byte]$c.G); $bw.Write([byte]$c.R); $bw.Write([byte]$c.A)
        }
    }
    # AND 掩码（全 0，每行 4 字节对齐）
    $rowBytes = [math]::Ceiling($w / 8.0)
    $pad = (4 - ($rowBytes % 4)) % 4
    for ($y = 0; $y -lt $h; $y++) {
        for ($i = 0; $i -lt $rowBytes; $i++) { $bw.Write([byte]0) }
        for ($i = 0; $i -lt $pad; $i++) { $bw.Write([byte]0) }
    }
    $bw.Flush()
    $blobs += , @{ size = $s; data = $ms.ToArray() }
    $bw.Dispose(); $ms.Dispose(); $bmp.Dispose()
}

$icoPath = Join-Path $pcOut 'app.ico'
$fsOut = [System.IO.File]::Create($icoPath)
$bwOut = New-Object System.IO.BinaryWriter($fsOut)
$bwOut.Write([int16]0); $bwOut.Write([int16]1); $bwOut.Write([int16]$blobs.Count)
$offset = 6 + 16 * $blobs.Count
foreach ($b in $blobs) {
    $dim = if ($b.size -ge 256) { 0 } else { $b.size }
    $bwOut.Write([byte]$dim); $bwOut.Write([byte]$dim)
    $bwOut.Write([byte]0); $bwOut.Write([byte]0)
    $bwOut.Write([int16]1); $bwOut.Write([int16]32)
    $bwOut.Write([int]$b.data.Length); $bwOut.Write([int]$offset)
    $offset += $b.data.Length
}
foreach ($b in $blobs) { $bwOut.Write($b.data) }
$bwOut.Flush(); $bwOut.Dispose(); $fsOut.Dispose()
"ICO: $icoPath  ($([math]::Round((Get-Item $icoPath).Length/1KB,1)) KB, $($blobs.Count) 个尺寸)"

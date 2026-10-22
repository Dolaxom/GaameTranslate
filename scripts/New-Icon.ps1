$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetDirectory = Join-Path $PSScriptRoot '..\assets'
New-Item -ItemType Directory -Path $assetDirectory -Force | Out-Null
$bitmap = [System.Drawing.Bitmap]::new(64, 64)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::FromArgb(20, 27, 33))
$pen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(153, 227, 198), 4)
$graphics.DrawLines($pen, [System.Drawing.Point[]]@([System.Drawing.Point]::new(12,24),[System.Drawing.Point]::new(12,12),[System.Drawing.Point]::new(24,12)))
$graphics.DrawLines($pen, [System.Drawing.Point[]]@([System.Drawing.Point]::new(40,12),[System.Drawing.Point]::new(52,12),[System.Drawing.Point]::new(52,24)))
$graphics.DrawLines($pen, [System.Drawing.Point[]]@([System.Drawing.Point]::new(12,40),[System.Drawing.Point]::new(12,52),[System.Drawing.Point]::new(24,52)))
$graphics.DrawLines($pen, [System.Drawing.Point[]]@([System.Drawing.Point]::new(40,52),[System.Drawing.Point]::new(52,52),[System.Drawing.Point]::new(52,40)))
$font = [System.Drawing.Font]::new('Segoe UI', 22, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$brush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
$graphics.DrawString('G', $font, $brush, 21, 17)
$stream = [System.IO.MemoryStream]::new()
$bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
$png = $stream.ToArray()
$writer = [System.IO.BinaryWriter]::new([System.IO.File]::Create((Join-Path $assetDirectory 'GaameTranslate.ico')))
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]1)
    $writer.Write([byte]64); $writer.Write([byte]64); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$png.Length); $writer.Write([uint32]22); $writer.Write($png)
} finally { $writer.Dispose(); $stream.Dispose(); $brush.Dispose(); $font.Dispose(); $pen.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }

# Generates src/DotStream.App/Resources/dotstream.ico and dotstream-1024.png
#
# The PNG is for everywhere that will not take an .ico: the Discord developer portal
# wants 1024x1024, and package listings generally want a PNG too.
#
# The mark is the name: a dot. Dark body circle, accent dot centred at 37.5% of the
# canvas - the same geometry the tray icon used before it became an asset.
#
# Regenerate after changing the accent colour. Windows Vista+ reads PNG-compressed
# icon entries at every size, so each entry is just a PNG.

param(
    [string] $OutputPath = (Join-Path $PSScriptRoot '..\src\DotStream.App\Resources\dotstream.ico')
)

Add-Type -AssemblyName System.Drawing

$Body   = [System.Drawing.Color]::FromArgb(255, 0x1E, 0x1E, 0x22)
$Accent = [System.Drawing.Color]::FromArgb(255, 0x4D, 0xD9, 0xE8)
$Sizes  = @(16, 20, 24, 32, 48, 64, 128, 256)

function New-Png([int] $size) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $bodyBrush = New-Object System.Drawing.SolidBrush($Body)
    $graphics.FillEllipse($bodyBrush, 0.0, 0.0, [double]($size - 1), [double]($size - 1))

    $diameter = [double]$size * 0.375
    $offset = ([double]$size - $diameter) / 2.0
    $accentBrush = New-Object System.Drawing.SolidBrush($Accent)
    $graphics.FillEllipse($accentBrush, $offset, $offset, $diameter, $diameter)

    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)

    $bytes = $stream.ToArray()
    $stream.Dispose(); $accentBrush.Dispose(); $bodyBrush.Dispose()
    $graphics.Dispose(); $bitmap.Dispose()
    return , $bytes
}

$images = @{}
foreach ($size in $Sizes) { $images[$size] = New-Png $size }

$directory = Split-Path $OutputPath -Parent
if (-not (Test-Path $directory)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }

$file = [System.IO.File]::Create($OutputPath)
$writer = New-Object System.IO.BinaryWriter($file)

# ICONDIR
$writer.Write([uint16]0)               # reserved
$writer.Write([uint16]1)               # type: icon
$writer.Write([uint16]$Sizes.Count)

# ICONDIRENTRY per image; 256 is encoded as 0 in the width/height bytes
$offset = 6 + (16 * $Sizes.Count)
foreach ($size in $Sizes) {
    $bytes = $images[$size]
    $dimension = if ($size -ge 256) { 0 } else { $size }

    $writer.Write([byte]$dimension)
    $writer.Write([byte]$dimension)
    $writer.Write([byte]0)             # palette entries
    $writer.Write([byte]0)             # reserved
    $writer.Write([uint16]1)           # colour planes
    $writer.Write([uint16]32)          # bits per pixel
    $writer.Write([uint32]$bytes.Length)
    $writer.Write([uint32]$offset)

    $offset += $bytes.Length
}

foreach ($size in $Sizes) { $writer.Write($images[$size]) }

$writer.Flush(); $writer.Dispose(); $file.Dispose()

$resolved = (Resolve-Path $OutputPath).Path
"wrote $resolved ($((Get-Item $resolved).Length) bytes, $($Sizes.Count) sizes)"

# A single large PNG, same mark, for places that cannot read an .ico.
$pngPath = Join-Path (Split-Path $OutputPath -Parent) 'dotstream-1024.png'
$large = New-Png 1024
$large.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
$large.Dispose()

"wrote $pngPath"

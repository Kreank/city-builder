param([Parameter(Mandatory=$true)][string[]]$Names, [string]$Out = "contact.png", [int]$Cols = 4, [int]$Tile = 300)
# Legt Renders (blender\renders\<Name>.png) als Raster mit Beschriftung zusammen.
Add-Type -AssemblyName System.Drawing
$dir = Join-Path $PSScriptRoot "renders"
$rows = [math]::Ceiling($Names.Count / $Cols)
$bmp = New-Object System.Drawing.Bitmap ($Cols * $Tile), ($rows * $Tile)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::White)
$font = New-Object System.Drawing.Font "Segoe UI", 11, ([System.Drawing.FontStyle]::Bold)
for ($i = 0; $i -lt $Names.Count; $i++) {
    $p = Join-Path $dir ($Names[$i] + ".png")
    if (-not (Test-Path $p)) { continue }
    $img = [System.Drawing.Image]::FromFile($p)
    $x = ($i % $Cols) * $Tile; $y = [math]::Floor($i / $Cols) * $Tile
    $g.DrawImage($img, $x, $y, $Tile, $Tile)
    $g.DrawString($Names[$i], $font, [System.Drawing.Brushes]::Black, $x + 6, $y + 6)
    $img.Dispose()
}
$path = Join-Path $dir $Out
$bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Output $path

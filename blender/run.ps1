param([Parameter(Mandatory=$true)][string]$Name, [string[]]$Only = @())
# -Only: bei Familien-Skripten (props, buildings_*) nur diese Varianten bauen
# Baut ein Asset: blender\scripts\obj_<Name>.py  ->  Assets\_Project\Models\...\<Name>.fbx + blender\renders\<Name>*.png
$blender = "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"
$script = Join-Path $PSScriptRoot "scripts\obj_$Name.py"
$extra = @(); if ($Only.Count -gt 0) { $extra = @("--") + $Only }
$out = & $blender --background --factory-startup --python-exit-code 1 --python $script @extra 2>&1 | Out-String
$pattern = 'CB_|Error|Traceback|File "|line \d+|Exception'
$out -split "`n" | Where-Object { $_ -match $pattern } | ForEach-Object { $_.TrimEnd() }
Write-Output ("exit=" + $LASTEXITCODE)
# Beschreibungsdateien <Name>.json neben den FBX aktualisieren (Rolle, Zone, Stufe ...; s. scripts\write_sidecars.py)
$side = & $blender --background --factory-startup --python-exit-code 1 --python (Join-Path $PSScriptRoot "scripts\write_sidecars.py") 2>&1 | Out-String
$side -split "`n" | Where-Object { $_ -match 'CB_SIDECARS|Error|Traceback' } | ForEach-Object { $_.TrimEnd() }

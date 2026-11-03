# Baut alle Assets (obj_*.py) neu.
Get-ChildItem (Join-Path $PSScriptRoot "scripts\obj_*.py") | ForEach-Object {
    $n = $_.BaseName.Substring(4)
    Write-Output "== $n"
    & (Join-Path $PSScriptRoot "run.ps1") $n
}

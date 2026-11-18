# Rebuilds the Battle Tank's 3D model with Blender, then the item mods and the plugin.
#   .\blender\build.ps1                      uses the newest Blender in Program Files
#   .\blender\build.ps1 -Blender "D:\Blender\blender.exe"
param([string]$Blender = "", [string]$GameDir = "D:\SteamLibrary\steamapps\common\ZED ZONE")
$ErrorActionPreference = "Stop"
$Repo = Split-Path $PSScriptRoot -Parent
Set-Location $Repo
if (-not $Blender) {
    $found = Get-ChildItem "C:\Program Files\Blender Foundation" -Recurse -Filter blender.exe -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $found) { throw "Blender not found. Install it from blender.org or pass -Blender <path to blender.exe>" }
    $Blender = $found.FullName
}
Write-Host "==> Building the 3D model with $Blender" -ForegroundColor Cyan
& $Blender -b -P blender\build_tank.py
if ($LASTEXITCODE -ne 0) { throw "Blender failed" }
Write-Host "==> Item mods (needs Python with Pillow: pip install pillow)" -ForegroundColor Cyan
python art-src\build_items.py
Write-Host "==> Plugin" -ForegroundColor Cyan
dotnet build src\Tank.csproj -c Release "-p:GameDir=$GameDir"
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
Write-Host "Done. Renders are in blender\out, the model is in src\model3d." -ForegroundColor Green

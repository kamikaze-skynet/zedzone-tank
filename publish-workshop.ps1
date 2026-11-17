# Builds Tank and uploads it to the Steam Workshop with SteamCMD.
#
#   .\publish-workshop.ps1 -SteamUser <login>                     first upload: creates a Private item
#   .\publish-workshop.ps1 -SteamUser <login> -Visibility Public  make it public
#   .\publish-workshop.ps1 -SteamUser <login> -ChangeNote "Fixed X"
#
# SteamCMD asks for your password / Steam Guard code itself. This script never sees or saves them.
# The first upload writes the new item id to workshop\publishedfileid.txt; commit that file so
# later runs update the same item instead of creating a new one.
param(
    [Parameter(Mandatory = $true)][string]$SteamUser,
    [string]$GameDir = "D:\SteamLibrary\steamapps\common\ZED ZONE",
    [ValidateSet("Public", "FriendsOnly", "Private", "Unlisted")][string]$Visibility = "Private",
    [string]$ChangeNote = "",
    [switch]$UpdateDescription,
    [switch]$SkipBuild
)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$AppId   = "1211600"
$Title   = "Tank - drive a main battle tank in ZED ZONE"
$Source  = Join-Path $PSScriptRoot "workshop\content\Tank"
# Staged outside git (dist\ is ignored) so publishing never leaves tracked files
# modified, which used to make the next `git pull` refuse to update.
$Content = Join-Path $PSScriptRoot "dist\content"
$ModDir  = Join-Path $Content "Tank"
$Preview = Join-Path $PSScriptRoot "workshop\preview.png"
$IdFile  = Join-Path $PSScriptRoot "workshop\publishedfileid.txt"
$Dll     = Join-Path $PSScriptRoot "src\bin\Release\net6.0\Tank.dll"

# ---- 1. build and stage the item folder
if (-not $SkipBuild) {
    dotnet build src\Tank.csproj -c Release "-p:GameDir=$GameDir"
    if ($LASTEXITCODE -ne 0) { throw "Build failed" }
}
if (-not (Test-Path $Dll)) { throw "Missing $Dll - build first" }
$Version = [System.Reflection.AssemblyName]::GetAssemblyName($Dll).Version.ToString(3)
if (Test-Path $Content) { Remove-Item $Content -Recurse -Force }
New-Item -ItemType Directory -Force $ModDir | Out-Null
Copy-Item (Join-Path $Source "mod.json") (Join-Path $ModDir "mod.json") -Force
Copy-Item $Dll (Join-Path $ModDir "Tank.dll") -Force
Copy-Item (Join-Path $Source "items") (Join-Path $ModDir "items") -Recurse -Force
Copy-Item (Join-Path $PSScriptRoot "README.md") (Join-Path $ModDir "README.md") -Force
$modJson = Join-Path $ModDir "mod.json"
$json = Get-Content $modJson -Raw
$json = $json -replace '"version"\s*:\s*"[^"]*"', ('"version": "' + $Version + '"')
[System.IO.File]::WriteAllText($modJson, $json, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "==> Staged Tank $Version in dist\content\Tank" -ForegroundColor Cyan

# ---- 2. SteamCMD (downloaded once into tools\steamcmd)
$Tools = Join-Path $PSScriptRoot "tools\steamcmd"
$SteamCmd = Join-Path $Tools "steamcmd.exe"
if (-not (Test-Path $SteamCmd)) {
    Write-Host "==> Downloading SteamCMD" -ForegroundColor Cyan
    New-Item -ItemType Directory -Force $Tools | Out-Null
    $zip = Join-Path $Tools "steamcmd.zip"
    Invoke-WebRequest "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip" -OutFile $zip -UseBasicParsing
    Expand-Archive -LiteralPath $zip -DestinationPath $Tools -Force
    Remove-Item $zip
}

# ---- 3. item description (VDF)
$Id = "0"
if (Test-Path $IdFile) { $Id = (Get-Content $IdFile -Raw).Trim() }
$isNew = ($Id -eq "0" -or $Id -eq "")
if (-not $ChangeNote) { $ChangeNote = "Tank $Version" }

function Clean([string]$s) { return ($s -replace '"', "'" -replace '\\', '/') }
function P([string]$p) { return ($p -replace '\\', '/') }

$lines = @(
    '"workshopitem"', '{',
    ('  "appid" "' + $AppId + '"'),
    ('  "publishedfileid" "' + $Id + '"'),
    ('  "contentfolder" "' + (P $Content) + '"'),
    ('  "previewfile" "' + (P $Preview) + '"'),
    ('  "changenote" "' + (Clean $ChangeNote) + '"')
)
if ($isNew -or $PSBoundParameters.ContainsKey("Visibility")) {
    $vis = @{ Public = 0; FriendsOnly = 1; Private = 2; Unlisted = 3 }[$Visibility]
    $lines += ('  "visibility" "' + $vis + '"')
}
if ($isNew -or $UpdateDescription) {
    $desc = Get-Content (Join-Path $PSScriptRoot "workshop\description.txt") -Raw
    $lines += ('  "title" "' + (Clean $Title) + '"')
    $lines += ('  "description" "' + (Clean $desc) + '"')
}
$lines += '}'

$Dist = Join-Path $PSScriptRoot "dist"
New-Item -ItemType Directory -Force $Dist | Out-Null
$Vdf = Join-Path $Dist "workshop_item.vdf"
[System.IO.File]::WriteAllText($Vdf, ($lines -join "`r`n"), (New-Object System.Text.UTF8Encoding($false)))

# ---- 4. upload
if ($isNew) { Write-Host "==> Creating a new Workshop item ($Visibility)" -ForegroundColor Cyan }
else        { Write-Host "==> Updating Workshop item $Id" -ForegroundColor Cyan }
function Get-ItemUpdated([string]$itemId) {
    # Public Steam Web API, no key needed. Returns the item's last-update time, or 0.
    try {
        $r = Invoke-RestMethod -Method Post -Uri "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/" `
            -Body @{ itemcount = 1; "publishedfileids[0]" = $itemId } -TimeoutSec 20
        return [long]$r.response.publishedfiledetails[0].time_updated
    } catch { return 0 }
}
$before = if ($isNew) { 0 } else { Get-ItemUpdated $Id }

& $SteamCmd +login $SteamUser +workshop_build_item $Vdf +quit
$steamExit = $LASTEXITCODE

# SteamCMD writes the item id back into the VDF after a successful upload.
$after = Get-Content $Vdf -Raw
$failed = $false
if ($after -match '"publishedfileid"\s+"(\d+)"' -and $Matches[1] -ne "0" -and -not $isNew) {
    # SteamCMD prints "ERROR (Access Denied)" and still exits normally, so check
    # with Steam that the item really changed.
    Start-Sleep -Seconds 3
    $now = Get-ItemUpdated $Id
    if ($before -gt 0 -and $now -gt 0 -and $now -le $before) { $failed = $true }
}
if ($steamExit -ne 0) { $failed = $true }
if (-not $failed -and $after -match '"publishedfileid"\s+"(\d+)"' -and $Matches[1] -ne "0") {
    $newId = $Matches[1]
    if ($isNew) {
        Set-Content -Path $IdFile -Value $newId -Encoding ASCII
        Write-Host "    saved item id to workshop\publishedfileid.txt - commit it" -ForegroundColor Yellow
    }
    Write-Host ""
    Write-Host " Done: Tank $Version uploaded - https://steamcommunity.com/sharedfiles/filedetails/?id=$newId" -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host " Upload FAILED - nothing changed on the Workshop. Read the SteamCMD output above." -ForegroundColor Red
    Write-Host " If it says 'Access Denied' or a login error, log in again (password + Steam Guard), then rerun this script:" -ForegroundColor Red
    Write-Host "   & `"$SteamCmd`" +login $SteamUser +quit" -ForegroundColor Yellow
    Write-Host " (First time: accept the Workshop agreement on the Steam website if it asks.)" -ForegroundColor Red
    exit 1
}

# Builds the Windows setup wizard Builds/Installer/OpenAT[-Faithful]-Setup-<version>.exe.
#   1. builds the release player with tools/build/build-player.sh (works while the editor is open), unless
#      -PlayerDir points at a player that is already built
#   2. checks that no game file ended up in the player folder
#   3. compiles tools/installer/OpenAT.iss with Inno Setup 6 (winget install JRSoftware.InnoSetup)
#
#   ./tools/installer/build-installer.ps1 [-Version 0.1.0] [-Flavor enhanced|faithful] [-PlayerDir <dir>] [-Iscc <ISCC.exe>]
# The flavour names the setup (OpenAT or OpenAT Faithful); build it from the matching branch.
param(
    [string]$Version = "",
    [ValidateSet("enhanced", "faithful")][string]$Flavor = "enhanced",
    [string]$PlayerDir = "",
    [string]$Iscc = ""
)
$ErrorActionPreference = "Stop"
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if (-not $Version) {
    $Version = ((Get-Content "$root/ProjectSettings/ProjectSettings.asset" | Select-String "^\s*bundleVersion:") -split ":\s*")[1].Trim()
}

if (-not $PlayerDir) {
    $PlayerDir = "$root/Builds/Release-$Flavor"
    Write-Host "== building the release player into $PlayerDir"
    & bash "$root/tools/build/build-player.sh" ($PlayerDir -replace '\\', '/')
    if ($LASTEXITCODE -ne 0) { throw "player build failed" }
}
if (-not (Test-Path "$PlayerDir/OpenAT.exe")) { throw "no player in $PlayerDir" }
Get-ChildItem $PlayerDir -Directory -Filter "*_BurstDebugInformation_DoNotShip" | Remove-Item -Recurse -Force

Write-Host "== checking the player folder for game content"
if (Test-Path "$PlayerDir/Game") { throw "the player folder contains a Game folder (game content); build into an empty folder" }
$gameTypes = "*.gli", "*.glj", "*.smk", "*.mcf", "*.res", "*.raw", "*.pol", "*.pln", "*.lbm", "*.mid", "*.csv"
$leaks = Get-ChildItem $PlayerDir -Recurse -File -Include $gameTypes
if ($leaks) { throw "the player contains game files: $($leaks.FullName -join ', ')" }

if (-not $Iscc) {
    $Iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
        Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $Iscc) { throw "Inno Setup 6 not found (winget install JRSoftware.InnoSetup) or pass -Iscc <ISCC.exe>" }
}
$out = "$root/Builds/Installer"
Write-Host "== compiling the $Flavor setup $Version"
& $Iscc "/DAppVersion=$Version" "/DFlavor=$Flavor" "/DPlayerDir=$((Resolve-Path $PlayerDir).Path)" "/DOutputDir=$out" "$PSScriptRoot/OpenAT.iss"
if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }
Get-ChildItem "$out/OpenAT*-Setup-$Version.exe" | ForEach-Object { Write-Host "setup: $($_.FullName)" }

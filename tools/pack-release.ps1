# IELTop release build with Velopack.
#
# One command publishes the app, packs a Velopack release, and writes the
# installer plus update files under ./releases. The app then updates itself
# from wherever you upload that folder, or from a GitHub Releases page.
#
# Usage:
#   pwsh tools/pack-release.ps1 -Version 1.0.1
#   pwsh tools/pack-release.ps1 -Version 1.0.1 -Runtime win-x64 -Channel win
#
# Notes:
# - Run from the repository root.
# - vpk is a .NET global tool: dotnet tool install -g vpk
# - Use the same vpk version as the Velopack package in IELTop.csproj.

param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$Runtime = "win-x64",
    [string]$Channel = "win",
    [string]$PackId = "IELTop",
    [string]$OutputDir = "releases",
    [switch]$SkipIcon
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not $SkipIcon) {
    Write-Host "Generating the app icon." -ForegroundColor Cyan
    node tools/make-icon.mjs
}

$publishDir = Join-Path $root "publish"
Write-Host "Publishing IELTop $Version ($Runtime)." -ForegroundColor Cyan
Remove-Item $publishDir -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish "IELTop/IELTop.csproj" `
    -c Release `
    -r $Runtime `
    --self-contained `
    -p:Version=$Version `
    -o $publishDir

if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    Write-Host "vpk is missing. Install it with: dotnet tool install -g vpk" -ForegroundColor Yellow
    exit 1
}

Write-Host "Packing the Velopack release." -ForegroundColor Cyan
$releases = Join-Path $root $OutputDir
vpk pack `
    --packId $PackId `
    --packVersion $Version `
    --packDir $publishDir `
    --mainExe IELTop.exe `
    --packTitle "IELTop" `
    --packAuthors "IELTop" `
    --runtime $Runtime `
    --channel $Channel `
    --outputDir $releases

Write-Host ""
Write-Host "Done. Upload everything in $releases to your update host," -ForegroundColor Green
Write-Host "then set that URL as the update feed in IELTop Settings." -ForegroundColor Green

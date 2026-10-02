# IELTop desktop release build.
#
# Publishes the cross platform client and zips it for a release. The app checks
# GitHub Releases for a newer version and opens the release page to install;
# that works the same on Windows, macOS and Linux, so no per OS installer is
# built here.
#
# Usage:
#   pwsh tools/pack-release.ps1 -Version 1.0.1
#   pwsh tools/pack-release.ps1 -Version 1.0.1 -Runtime linux-x64
#
# Notes:
# - Run from the repository root.
# - The web UI is built by the project itself, so npm must be on PATH.

param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$Runtime = "win-x64",
    [string]$OutputDir = "releases",
    [switch]$SkipIcon
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not $SkipIcon) {
    Write-Host "Generating the app icon." -ForegroundColor Cyan
    conda run -n ieltop-icon python tools/make-icon/make_icon.py
}

$publishDir = Join-Path $root "publish"
Write-Host "Publishing IELTop $Version ($Runtime)." -ForegroundColor Cyan
Remove-Item $publishDir -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish "IELTop.Desktop/IELTop.Desktop.csproj" `
    -c Release `
    -r $Runtime `
    --self-contained false `
    -p:Version=$Version `
    -o $publishDir

$releases = Join-Path $root $OutputDir
New-Item -ItemType Directory -Force -Path $releases | Out-Null
$zip = Join-Path $releases "IELTop-$Version-$Runtime.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Write-Host "Zipping the release." -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zip

Write-Host ""
Write-Host "Done. Attach $zip to a GitHub Release tagged v$Version." -ForegroundColor Green
Write-Host "The app offers the update on its next check." -ForegroundColor Green

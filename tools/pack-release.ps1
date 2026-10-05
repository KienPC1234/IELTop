# IELTop desktop release build with Velopack support.
#
# Usage:
#   pwsh tools/pack-release.ps1 -Version 1.0.0
#   pwsh tools/pack-release.ps1 -Version 1.0.0 -Runtime win-x64

param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$Runtime = "win-x64",
    [string]$Framework = "net10.0-windows10.0.19041.0",
    [string]$PackId = "IELTop",
    [string]$OutputDir = "releases",
    [switch]$SkipIcon,
    [switch]$SkipVelo,

    # Off by default, so the package stays small. Turn it on when the people
    # installing the app have no .NET runtime installed.
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not $SkipIcon -and (Test-Path "tools/make-icon/make_icon.py")) {
    Write-Host "Generating the app icon." -ForegroundColor Cyan
    try {
        conda run -n ieltop-icon python tools/make-icon/make_icon.py
    } catch {
        Write-Host "Icon generation skipped." -ForegroundColor Yellow
    }
}

$publishDir = Join-Path $root "publish"
Write-Host "Publishing IELTop $Version ($Runtime)." -ForegroundColor Cyan
Remove-Item $publishDir -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish "IELTop.Desktop/IELTop.Desktop.csproj" `
    -c Release `
    -f $Framework `
    -r $Runtime `
    -p:SelfContained=$($SelfContained.IsPresent.ToString().ToLowerInvariant()) `
    -p:Version=$Version `
    -o $publishDir

$releases = Join-Path $root $OutputDir
New-Item -ItemType Directory -Force -Path $releases | Out-Null
$zip = Join-Path $releases "IELTop-$Version-$Runtime.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Write-Host "Zipping the release." -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zip

if (-not $SkipVelo -and (Get-Command vpk -ErrorAction SilentlyContinue)) {
    Write-Host "Packing Velopack release with vpk." -ForegroundColor Cyan
    vpk pack `
        --packId $PackId `
        --packVersion $Version `
        --packDir $publishDir `
        --mainExe IELTop.Desktop.exe `
        --packTitle "IELTop" `
        --packAuthors "IELTop" `
        --runtime $Runtime `
        --outputDir $releases `
        --skipVeloAppCheck
}

Write-Host ""
Write-Host "Done. Output generated in $releases" -ForegroundColor Green

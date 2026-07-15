# scripts/release.ps1 - build a distributable Setup.exe.
#
# Publishes self-contained (bundles the .NET runtime) so the download runs on a clean Windows with
# nothing pre-installed. That costs ~100MB of download, which is the price of not making every user
# go and install a runtime first before the app will even start.
#
# Usage:  .\scripts\release.ps1 -Version 1.0.1
param(
    [Parameter(Mandatory = $true)][string]$Version
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root

try {
    # A running copy locks its own exe, which would fail the publish partway through.
    Get-Process MasterImage.App -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500

    $publish = Join-Path $root 'publish'
    if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

    Write-Host "Publishing $Version (self-contained)..." -ForegroundColor Cyan
    dotnet publish src/MasterImage.App/MasterImage.App.csproj `
        -c Release -r win-x64 --self-contained true `
        -p:Version=$Version -o $publish
    if ($LASTEXITCODE -ne 0) { throw 'publish failed' }

    Write-Host "Packaging $Version..." -ForegroundColor Cyan
    vpk pack `
        --packId MasterImage `
        --packVersion $Version `
        --packDir $publish `
        --mainExe MasterImage.App.exe `
        --packTitle 'Master Image' `
        --packAuthors 'KAIHARI'
    if ($LASTEXITCODE -ne 0) { throw 'vpk pack failed' }

    Write-Host ''
    Write-Host 'Done. Artifacts in .\Releases:' -ForegroundColor Green
    Get-ChildItem (Join-Path $root 'Releases') |
        ForEach-Object { '  {0}  ({1:N1} MB)' -f $_.Name, ($_.Length / 1MB) }

    Write-Host ''
    Write-Host 'Publish to GitHub with:' -ForegroundColor Yellow
    Write-Host "  gh release create v$Version .\Releases\* --title `"v$Version`" --notes `"...`""
}
finally {
    Pop-Location
}

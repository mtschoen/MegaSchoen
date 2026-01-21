#!/usr/bin/env pwsh
# Script to automatically update UI screenshots for documentation

$ErrorActionPreference = "Stop"

Write-Host "MegaSchoen MAUI Screenshot Updater" -ForegroundColor Cyan
Write-Host "===================================" -ForegroundColor Cyan
Write-Host ""

try {
    # Build the MAUI project (must use MSBuild for native C++ dependency)
    Write-Host "Building MAUI project..." -ForegroundColor Yellow
    MSBuild.exe MegaSchoen/MegaSchoen.csproj -p:Configuration=Debug -p:Platform=x64 -p:TargetFramework=net10.0-windows10.0.26100.0 -v:minimal

    if ($LASTEXITCODE -ne 0) {
        throw "Build failed"
    }
    Write-Host "Build successful!" -ForegroundColor Green

    # Run screenshot capture test
    Write-Host ""
    Write-Host "Capturing screenshots..." -ForegroundColor Yellow
    dotnet test "MegaSchoen.UITests/MegaSchoen.UITests.csproj" `
        --filter "FullyQualifiedName~CaptureMainWindowScreenshot" `
        --logger "console;verbosity=normal"

    if ($LASTEXITCODE -ne 0) {
        throw "Screenshot capture failed"
    }

    # List generated screenshots
    Write-Host ""
    Write-Host "Screenshots updated:" -ForegroundColor Green
    Get-ChildItem -Path "Screenshots" -Filter "*.png" | ForEach-Object {
        $size = [math]::Round($_.Length / 1KB, 2)
        Write-Host "  - $($_.Name) (${size} KB)" -ForegroundColor Cyan
    }
}
catch {
    Write-Host ""
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Done! Screenshots are ready for documentation." -ForegroundColor Green

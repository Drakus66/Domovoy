#!/usr/bin/env pwsh

# Pre-startup script for Domovoy
# Attaches USB device to WSL before starting Docker Compose

Write-Host "=== Domovoy Pre-startup Script ===" -ForegroundColor Green
Write-Host "Checking USB device attachment..." -ForegroundColor Yellow

# Check if usbipd is available
try {
    $usbipdVersion = & usbipd --version 2>$null
    Write-Host "usbipd found: $usbipdVersion" -ForegroundColor Green
}
catch {
    Write-Warning "usbipd not found. Please install usbipd-win from: https://github.com/dorssel/usbipd-win/releases"
    Write-Warning "USB device will not be attached to WSL."
    exit 0
}

# Attach USB device to WSL
try {
    Write-Host "Attaching USB device 2-3 to WSL..." -ForegroundColor Yellow
    & usbipd attach --wsl --busid 2-2
    Write-Host "USB device attached successfully!" -ForegroundColor Green
}
catch {
    Write-Warning "Failed to attach USB device 2-3 to WSL"
    Write-Warning "You may need to:"
    Write-Warning "1. Check if the USB device is connected (run 'usbipd list')"
    Write-Warning "2. Ensure WSL is running"
    Write-Warning "3. Check if the device bus ID is correct"
}

Write-Host "=== Pre-startup completed ===" -ForegroundColor Green

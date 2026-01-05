# Script for updating, building and running the project in Release configuration

$ProcessName = "AssetProcessor"
$ExePath = "bin\Release\net9.0-windows10.0.26100.0\win-x64\AssetProcessor.exe"

# Step 1: Git fetch + reset (avoids merge conflicts with force-pushed branches)
Write-Host "Updating from repository..." -ForegroundColor Cyan
git fetch origin
$currentBranch = git rev-parse --abbrev-ref HEAD
git reset --hard "origin/$currentBranch"
if ($LASTEXITCODE -ne 0) {
    Write-Host "Warning: git update completed with error" -ForegroundColor Yellow
}

# Step 2: Close the program if it's running
Write-Host "`nChecking running processes..." -ForegroundColor Cyan
$processes = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue
if ($processes) {
    Write-Host "Closing running instance of $ProcessName..." -ForegroundColor Yellow
    $processes | Stop-Process -Force
    Start-Sleep -Seconds 1
    Write-Host "Process closed." -ForegroundColor Green
} else {
    Write-Host "Application is not running." -ForegroundColor Gray
}

# Step 3: Build project
Write-Host "`nBuilding project..." -ForegroundColor Cyan
dotnet build TexTool.sln --configuration Release

if ($LASTEXITCODE -eq 0) {
    # Step 4: Run compiled exe
    Write-Host "`nStarting application..." -ForegroundColor Green
    if (Test-Path $ExePath) {
        Start-Process -FilePath $ExePath
        Write-Host "Application started: $ExePath" -ForegroundColor Green
    } else {
        Write-Host "Error: File not found: $ExePath" -ForegroundColor Red
        exit 1
    }
} else {
    Write-Host "`nBuild error! Application not started." -ForegroundColor Red
    exit 1
}

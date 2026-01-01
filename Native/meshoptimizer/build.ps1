# build.ps1 - Build meshopt_wrapper.dll for Windows x64
# Requires: CMake, Visual Studio Build Tools

param(
    [string]$Configuration = "Release",
    [string]$OutputDir = "..\..\bin\Release\net9.0-windows10.0.26100.0\win-x64"
)

$ErrorActionPreference = "Stop"

Write-Host "=== Building meshopt_wrapper.dll ===" -ForegroundColor Cyan

# Check for CMake
if (!(Get-Command cmake -ErrorAction SilentlyContinue)) {
    Write-Host "ERROR: CMake not found. Install from https://cmake.org/" -ForegroundColor Red
    exit 1
}

# Create build directory
$BuildDir = "build"
if (Test-Path $BuildDir) {
    Remove-Item -Recurse -Force $BuildDir
}
New-Item -ItemType Directory -Path $BuildDir | Out-Null

Push-Location $BuildDir

try {
    # Configure with CMake
    Write-Host "Configuring..." -ForegroundColor Yellow
    cmake .. -G "Visual Studio 17 2022" -A x64 -DCMAKE_BUILD_TYPE=$Configuration

    if ($LASTEXITCODE -ne 0) {
        throw "CMake configure failed"
    }

    # Build
    Write-Host "Building..." -ForegroundColor Yellow
    cmake --build . --config $Configuration --parallel

    if ($LASTEXITCODE -ne 0) {
        throw "CMake build failed"
    }

    # Copy DLL to output directory
    $DllPath = "$Configuration\meshopt_wrapper.dll"
    if (Test-Path $DllPath) {
        $FullOutputDir = Resolve-Path $OutputDir -ErrorAction SilentlyContinue
        if (!$FullOutputDir) {
            New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
            $FullOutputDir = Resolve-Path $OutputDir
        }

        Copy-Item $DllPath -Destination $FullOutputDir -Force
        Write-Host "SUCCESS: Copied to $FullOutputDir\meshopt_wrapper.dll" -ForegroundColor Green
    } else {
        Write-Host "WARNING: DLL not found at $DllPath" -ForegroundColor Yellow
    }

} finally {
    Pop-Location
}

Write-Host "=== Build complete ===" -ForegroundColor Cyan

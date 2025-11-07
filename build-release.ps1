# NFC Actions Release Build Script
# This script builds the release version and optionally creates an installer

param(
    [switch]$SkipBuild = $false,
    [switch]$BuildInstaller = $true
)

$ErrorActionPreference = "Stop"
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $scriptPath "NfcActions\NfcActions.csproj"
$publishPath = Join-Path $scriptPath "NfcActions\bin\Release\net7.0-windows\win-x64\publish"
$installerPath = Join-Path $scriptPath "Installer"

Write-Host "=== NFC Actions Release Build ===" -ForegroundColor Cyan
Write-Host ""

# Step 1: Build and publish the application
if (-not $SkipBuild) {
    Write-Host "[1/4] Cleaning previous builds..." -ForegroundColor Yellow
    dotnet clean $projectPath -c Release -v quiet

    Write-Host "[2/4] Building release (self-contained, single-file)..." -ForegroundColor Yellow
    dotnet publish $projectPath -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:PublishReadyToRun=true

    if ($LASTEXITCODE -ne 0) {
        Write-Host "Build failed!" -ForegroundColor Red
        exit 1
    }

    Write-Host "Build completed successfully!" -ForegroundColor Green
    Write-Host "Output: $publishPath" -ForegroundColor Gray
    Write-Host ""
}

# Step 2: Check if WiX is available
if ($BuildInstaller) {
    Write-Host "[3/4] Checking for WiX Toolset..." -ForegroundColor Yellow

    $wixInstalled = $false
    $candle = Get-Command candle.exe -ErrorAction SilentlyContinue
    $light = Get-Command light.exe -ErrorAction SilentlyContinue

    if ($candle -and $light) {
        $wixInstalled = $true
        Write-Host "WiX Toolset found!" -ForegroundColor Green
    } else {
        Write-Host "WiX Toolset not found in PATH" -ForegroundColor Yellow

        # Check common installation paths
        $wixPaths = @(
            "C:\Program Files (x86)\WiX Toolset v3.11\bin",
            "C:\Program Files (x86)\WiX Toolset v3.14\bin",
            "C:\Program Files\WiX Toolset v3.11\bin",
            "C:\Program Files\WiX Toolset v3.14\bin"
        )

        foreach ($path in $wixPaths) {
            if (Test-Path (Join-Path $path "candle.exe")) {
                $env:Path += ";$path"
                $wixInstalled = $true
                Write-Host "Found WiX at: $path" -ForegroundColor Green
                break
            }
        }
    }

    if ($wixInstalled) {
        Write-Host "[4/4] Building MSI installer..." -ForegroundColor Yellow

        Push-Location $installerPath

        # Create obj and bin directories if they don't exist
        New-Item -ItemType Directory -Force -Path obj | Out-Null
        New-Item -ItemType Directory -Force -Path bin | Out-Null

        # Run heat to harvest all files from publish folder
        Write-Host "  Harvesting files from publish folder..." -ForegroundColor Gray
        & heat.exe dir $publishPath -cg HarvestedFiles -gg -sfrag -srd -dr INSTALLFOLDER -var var.PublishDir -out obj\HarvestedFiles.wxs
        if ($LASTEXITCODE -ne 0) {
            Pop-Location
            Write-Host "Heat (file harvesting) failed!" -ForegroundColor Red
            exit 1
        }

        # Run candle (compile) on both wxs files
        Write-Host "  Compiling installer..." -ForegroundColor Gray
        & candle.exe Product.wxs obj\HarvestedFiles.wxs "-dPublishDir=$publishPath" -out obj\ -arch x64
        if ($LASTEXITCODE -ne 0) {
            Pop-Location
            Write-Host "Candle (WiX compile) failed!" -ForegroundColor Red
            exit 1
        }

        # Run light (link)
        Write-Host "  Linking MSI package..." -ForegroundColor Gray
        & light.exe obj\Product.wixobj obj\HarvestedFiles.wixobj -out bin\NfcActions-Setup.msi -ext WixUIExtension -sval
        if ($LASTEXITCODE -ne 0) {
            Pop-Location
            Write-Host "Light (WiX link) failed!" -ForegroundColor Red
            exit 1
        }

        Pop-Location

        $msiPath = Join-Path $installerPath "bin\NfcActions-Setup.msi"
        Write-Host ""
        Write-Host "=== Build Complete ===" -ForegroundColor Green
        Write-Host "MSI Installer: $msiPath" -ForegroundColor Cyan

    } else {
        Write-Host ""
        Write-Host "WiX Toolset not found. Skipping MSI creation." -ForegroundColor Yellow
        Write-Host "To build the MSI installer, install WiX Toolset from:" -ForegroundColor Yellow
        Write-Host "  https://github.com/wixtoolset/wix3/releases" -ForegroundColor Gray
        Write-Host ""
        Write-Host "=== Build Complete ===" -ForegroundColor Green
        Write-Host "Executable: $publishPath\NfcActions.exe" -ForegroundColor Cyan
    }
} else {
    Write-Host "[3/4] Skipping installer build" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "=== Build Complete ===" -ForegroundColor Green
    Write-Host "Executable: $publishPath\NfcActions.exe" -ForegroundColor Cyan
}

Write-Host ""

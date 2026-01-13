<# 
.SYNOPSIS
    BatchForge Release Build Script
.DESCRIPTION
    Builds self-contained single-file executables for all platforms,
    creates distribution packages, and generates SHA256 checksums.
.NOTES
    Run from repository root: .\scripts\release.ps1
    Requires: .NET 8 SDK
#>

param(
    [string]$Version = "1.0.0",
    [switch]$SkipMac = $false
)

$ErrorActionPreference = "Stop"

# Configuration
$ProjectPath = "src\BatchForge.Cli\BatchForge.Cli.csproj"
$DistDir = "dist"
$PublishDir = "src\BatchForge.Cli\bin\Release\net8.0"

# Platforms to build
$Platforms = @(
    @{ RID = "win-x64";     Output = "batchforge.exe";  Archive = "batchforge-win-x64.zip";       Type = "zip" },
    @{ RID = "linux-x64";   Output = "batchforge";      Archive = "batchforge-linux-x64.tar.gz";  Type = "tar" }
)

if (-not $SkipMac) {
    $Platforms += @{ RID = "osx-arm64";   Output = "batchforge";      Archive = "batchforge-osx-arm64.tar.gz";  Type = "tar" }
    $Platforms += @{ RID = "osx-x64";     Output = "batchforge";      Archive = "batchforge-osx-x64.tar.gz";    Type = "tar" }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  BatchForge Release Build v$Version" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Clean dist directory
if (Test-Path $DistDir) {
    Write-Host "Cleaning $DistDir..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force $DistDir
}
New-Item -ItemType Directory -Path $DistDir | Out-Null

# Build each platform
foreach ($platform in $Platforms) {
    $rid = $platform.RID
    $output = $platform.Output
    $archive = $platform.Archive
    $type = $platform.Type
    
    Write-Host ""
    Write-Host "Building for $rid..." -ForegroundColor Green
    
    # Publish
    dotnet publish $ProjectPath `
        -c Release `
        -r $rid `
        --self-contained true `
        /p:PublishSingleFile=true `
        /p:IncludeNativeLibrariesForSelfExtract=true `
        /p:Version=$Version `
        /p:AssemblyVersion=$Version `
        -o "$DistDir\$rid"
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Build failed for $rid" -ForegroundColor Red
        exit 1
    }
    
    # Get the executable path
    $exePath = "$DistDir\$rid\$output"
    
    if (-not (Test-Path $exePath)) {
        # Try with .exe extension for Windows builds on Linux
        $exePath = "$DistDir\$rid\BatchForge.Cli"
        if (Test-Path "$exePath.exe") {
            Rename-Item "$exePath.exe" "$DistDir\$rid\$output"
            $exePath = "$DistDir\$rid\$output"
        }
    }
    
    # Create archive
    Write-Host "Creating $archive..." -ForegroundColor Yellow
    
    $archivePath = "$DistDir\$archive"
    
    if ($type -eq "zip") {
        Compress-Archive -Path $exePath -DestinationPath $archivePath -Force
    }
    else {
        # For tar.gz, we need to use tar command
        # First, copy to a temp location with correct name
        $tempDir = "$DistDir\temp-$rid"
        New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
        Copy-Item $exePath "$tempDir\$output"
        
        # Create tar.gz
        Push-Location $tempDir
        tar -czf "..\$archive" $output
        Pop-Location
        
        Remove-Item -Recurse -Force $tempDir
    }
    
    # Clean up publish directory (keep only archive)
    Remove-Item -Recurse -Force "$DistDir\$rid"
    
    Write-Host "  Created: $archive" -ForegroundColor Green
}

# Generate SHA256 checksums
Write-Host ""
Write-Host "Generating SHA256 checksums..." -ForegroundColor Yellow

$checksumFile = "$DistDir\SHA256SUMS.txt"
$checksums = @()

Get-ChildItem "$DistDir\*" -Include "*.zip", "*.tar.gz" | ForEach-Object {
    $hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower()
    $name = $_.Name
    $checksums += "$hash  $name"
    Write-Host "  $name" -ForegroundColor Gray
    Write-Host "  $hash" -ForegroundColor DarkGray
}

$checksums | Out-File -FilePath $checksumFile -Encoding ASCII

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Build Complete!" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Distribution files in: $DistDir\" -ForegroundColor Green
Write-Host ""

Get-ChildItem $DistDir | ForEach-Object {
    $size = ""
    if ($_.Length -gt 1MB) {
        $size = "{0:N1} MB" -f ($_.Length / 1MB)
    } else {
        $size = "{0:N0} KB" -f ($_.Length / 1KB)
    }
    Write-Host ("  {0,-40} {1,10}" -f $_.Name, $size) -ForegroundColor White
}

Write-Host ""
Write-Host "Upload these files to GitHub Releases:" -ForegroundColor Yellow
Get-ChildItem "$DistDir\*" -Include "*.zip", "*.tar.gz", "*.txt" | ForEach-Object {
    Write-Host "  - $($_.Name)" -ForegroundColor White
}
Write-Host ""

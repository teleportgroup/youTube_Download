# Build Release Script
# Usage: .\build-release.ps1 [-Version "1.0.0"]

param(
    [string]$Version
)

$ErrorActionPreference = "Stop"

# Get version from csproj if not provided
if (-not $Version) {
    $csproj = Get-Content "YouTubeDownloader/YouTubeDownloader.csproj" -Raw
    if ($csproj -match '<VersionPrefix>([^<]+)</VersionPrefix>') {
        $Version = $matches[1]
    } else {
        $Version = "1.0.0"
    }
}

Write-Host "Building version $Version" -ForegroundColor Cyan

# Clean previous builds
Write-Host "Cleaning previous builds..." -ForegroundColor Yellow
if (Test-Path "publish") { Remove-Item "publish" -Recurse -Force }
if (Test-Path "installer_output") { Remove-Item "installer_output" -Recurse -Force }

# Publish application
Write-Host "Publishing application..." -ForegroundColor Yellow
dotnet publish YouTubeDownloader/YouTubeDownloader.csproj -c Release -r win-x64 --self-contained true -o publish

# Download yt-dlp if not present
if (-not (Test-Path "publish/yt-dlp.exe")) {
    Write-Host "Downloading yt-dlp..." -ForegroundColor Yellow
    Invoke-WebRequest -Uri "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe" -OutFile "publish/yt-dlp.exe"
}

# Download ffmpeg if not present
if (-not (Test-Path "publish/ffmpeg.exe")) {
    Write-Host "Downloading ffmpeg..." -ForegroundColor Yellow
    Invoke-WebRequest -Uri "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip" -OutFile "ffmpeg.zip"
    Expand-Archive -Path "ffmpeg.zip" -DestinationPath "ffmpeg_temp" -Force
    Copy-Item "ffmpeg_temp/*/bin/ffmpeg.exe" "publish/"
    Copy-Item "ffmpeg_temp/*/bin/ffprobe.exe" "publish/"
    Remove-Item "ffmpeg.zip" -Force
    Remove-Item "ffmpeg_temp" -Recurse -Force
}

# Build installer
Write-Host "Building installer..." -ForegroundColor Yellow
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer.iss

Write-Host ""
Write-Host "Build complete!" -ForegroundColor Green
Write-Host "Installer: installer_output/YouTubeAudioDownloader_Setup_$Version.exe" -ForegroundColor Cyan

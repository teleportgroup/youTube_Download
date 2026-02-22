# Release Script
# Usage: .\release.ps1 -Major    # 1.2.3 -> 2.0.0
#        .\release.ps1 -Minor    # 1.2.3 -> 1.3.0
#        .\release.ps1 -Patch    # 1.2.3 -> 1.2.4 (default)

param(
    [switch]$Major,
    [switch]$Minor,
    [switch]$Patch,
    [string]$Message = ""
)

$ErrorActionPreference = "Stop"

$csprojPath = "YouTubeDownloader/YouTubeDownloader.csproj"

# Check for uncommitted changes
$status = git status --porcelain
if ($status) {
    Write-Host "Uncommitted changes detected:" -ForegroundColor Yellow
    git status --short
    Write-Host ""

    $commitChanges = Read-Host "Commit all changes before release? (y/n)"
    if ($commitChanges -eq 'y') {
        $commitMsg = Read-Host "Enter commit message"
        if (-not $commitMsg) {
            $commitMsg = "Pre-release changes"
        }
        git add -A
        git commit -m $commitMsg
        Write-Host "Changes committed." -ForegroundColor Green
        Write-Host ""
    } else {
        Write-Host "Please commit or stash changes before releasing." -ForegroundColor Red
        exit 1
    }
}

# Get current version
$content = Get-Content $csprojPath -Raw
if ($content -match '<VersionPrefix>([^<]+)</VersionPrefix>') {
    $currentVersion = $matches[1]
} else {
    Write-Host "Error: Could not find VersionPrefix in csproj" -ForegroundColor Red
    exit 1
}

# Parse version
$parts = $currentVersion.Split('.')
$majorNum = [int]$parts[0]
$minorNum = [int]$parts[1]
$patchNum = [int]$parts[2]

# Determine increment type (default to Patch if none specified)
if ($Major) {
    $majorNum++
    $minorNum = 0
    $patchNum = 0
    $incrementType = "Major"
} elseif ($Minor) {
    $minorNum++
    $patchNum = 0
    $incrementType = "Minor"
} else {
    $patchNum++
    $incrementType = "Patch"
}

$newVersion = "$majorNum.$minorNum.$patchNum"

Write-Host ""
Write-Host "Increment type:  $incrementType" -ForegroundColor Cyan
Write-Host "Current version: $currentVersion" -ForegroundColor Yellow
Write-Host "New version:     $newVersion" -ForegroundColor Green
Write-Host ""

# Confirm
$confirm = Read-Host "Proceed with release? (y/n)"
if ($confirm -ne 'y') {
    Write-Host "Cancelled." -ForegroundColor Yellow
    exit 0
}

# Update version in csproj
Write-Host "Updating version in csproj..." -ForegroundColor Cyan
$content = $content -replace '<VersionPrefix>[^<]+</VersionPrefix>', "<VersionPrefix>$newVersion</VersionPrefix>"
Set-Content $csprojPath $content -NoNewline

# Commit version bump
Write-Host "Committing version bump..." -ForegroundColor Cyan
git add $csprojPath
git commit -m "Bump version to $newVersion"

# Create tag
Write-Host "Creating tag v$newVersion..." -ForegroundColor Cyan
if ($Message) {
    git tag -a "v$newVersion" -m $Message
} else {
    git tag -a "v$newVersion" -m "Release v$newVersion"
}

# Push everything
Write-Host "Pushing commits and tag to origin..." -ForegroundColor Cyan
git push origin main
git push origin "v$newVersion"

Write-Host ""
Write-Host "Release v$newVersion initiated!" -ForegroundColor Green
Write-Host "GitHub Actions will now build and publish the installer." -ForegroundColor Cyan
Write-Host "Check progress at: https://github.com/teleportgroup/youTube_Download/actions" -ForegroundColor Cyan

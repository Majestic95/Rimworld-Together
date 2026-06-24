# Build-Release.ps1
#
# Builds the RimWorld Together client, assembles the canonical mod-folder
# snapshot, generates a SHA-256 manifest, and zips everything into a release
# artifact ready for `gh release create`.
#
# Output:
#   dist\snapshot\           - staged mod-folder snapshot (what friends get)
#   dist\manifest.json       - { tag, built_at, built_from_commit, files{} }
#   dist\rt-mp-<tag>.zip     - the snapshot, zipped
#
# Usage:
#   .\Build-Release.ps1                        # auto-tag mp-YYYY-MM-DD-N
#   .\Build-Release.ps1 -Tag mp-2026-06-23-1   # explicit tag
#   .\Build-Release.ps1 -SkipBuild             # use existing 1.6/Assemblies/GameClient.dll
#   .\Build-Release.ps1 -Force                 # overwrite existing artifacts for the tag

[CmdletBinding()]
param(
    [string]$Tag,
    [switch]$SkipBuild,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$DistDir  = Join-Path $RepoRoot 'dist'
$Staging  = Join-Path $DistDir 'snapshot'

# Top-level entries that ship in the mod folder. Anything not in this list is
# excluded. Add new entries intentionally - do not switch to a denylist.
$ShipEntries = @(
    'About',
    '1.5',
    '1.6',
    'LoadFolders.xml',
    'LICENSE'
)

function Write-Step { param([string]$Msg) Write-Host "==> $Msg" -ForegroundColor Cyan }
function Write-Ok   { param([string]$Msg) Write-Host "    $Msg" -ForegroundColor Green }
function Write-Warn { param([string]$Msg) Write-Host "    $Msg" -ForegroundColor Yellow }

function New-AutoTag {
    $datePart = Get-Date -Format 'yyyy-MM-dd'
    $n = 1
    while ($true) {
        $candidate = "mp-$datePart-$n"
        $existing = & gh release view $candidate --repo Majestic95/Rimworld-Together 2>$null
        if ($LASTEXITCODE -ne 0) { return $candidate }
        $n++
        if ($n -gt 99) { throw "Could not auto-generate a unique tag for $datePart" }
    }
}

function Get-GitCommit {
    try {
        $sha = & git -C $RepoRoot rev-parse --short HEAD 2>$null
        if ($LASTEXITCODE -eq 0) { return $sha.Trim() }
    } catch { }
    return 'unknown'
}

function Assert-CleanTree {
    $status = & git -C $RepoRoot status --porcelain 2>$null
    if ($LASTEXITCODE -ne 0) { Write-Warn 'git status failed - proceeding anyway.'; return }
    if ($status) {
        Write-Warn 'Working tree has uncommitted changes:'
        $status -split "`n" | ForEach-Object { Write-Host "      $_" -ForegroundColor DarkYellow }
        Write-Warn 'The release will be built from the working tree, including these changes.'
        Write-Warn 'If this is intentional (e.g. shipping a WIP build), continue. Otherwise commit or stash first and re-run.'
        $confirm = Read-Host '    Continue? (y/N)'
        if ($confirm -notmatch '^[Yy]') { throw 'Aborted by user.' }
    } else {
        Write-Ok 'Working tree clean.'
    }
}

function Invoke-Build {
    Write-Step 'Building client (Release)'
    Push-Location (Join-Path $RepoRoot 'Source')
    try {
        & dotnet build (Join-Path $RepoRoot 'Source\Client\GameClient.csproj') -c Release --nologo -v minimal
        if ($LASTEXITCODE -ne 0) { throw "dotnet build failed (exit $LASTEXITCODE)" }
    } finally {
        Pop-Location
    }
    $built = Join-Path $RepoRoot '1.6\Assemblies\GameClient.dll'
    if (-not (Test-Path $built)) { throw "Expected build output not found: $built" }
    Write-Ok "Built $built"
}

function New-Staging {
    if (Test-Path $Staging) { Remove-Item -Path $Staging -Recurse -Force }
    New-Item -ItemType Directory -Path $Staging -Force | Out-Null
    Write-Step 'Staging mod folder snapshot'
    foreach ($entry in $ShipEntries) {
        $src = Join-Path $RepoRoot $entry
        if (-not (Test-Path $src)) {
            Write-Warn "Skipping missing ship entry: $entry"
            continue
        }
        $dest = Join-Path $Staging $entry
        if ((Get-Item $src).PSIsContainer) {
            Copy-Item -Path $src -Destination $dest -Recurse -Force
        } else {
            Copy-Item -Path $src -Destination $dest -Force
        }
        Write-Ok "Staged $entry"
    }

    # Rename GameClient.dll -> RTClient.dll in 1.6/Assemblies
    $gameClient = Join-Path $Staging '1.6\Assemblies\GameClient.dll'
    $rtClient   = Join-Path $Staging '1.6\Assemblies\RTClient.dll'
    if (-not (Test-Path $gameClient)) {
        throw "1.6/Assemblies/GameClient.dll not found in staging - build may have failed or output to wrong path."
    }
    if (Test-Path $rtClient) { Remove-Item -Path $rtClient -Force }
    Move-Item -Path $gameClient -Destination $rtClient -Force
    Write-Ok 'Renamed GameClient.dll -> RTClient.dll'

    # Strip development cruft that the build might drop into Assemblies
    $cruftPatterns = @('*.pdb', '*.xml', '*.deps.json', '*.runtimeconfig.json')
    foreach ($pattern in $cruftPatterns) {
        Get-ChildItem -Path (Join-Path $Staging '1.6\Assemblies') -Filter $pattern -File -ErrorAction SilentlyContinue |
            ForEach-Object {
                Remove-Item -Path $_.FullName -Force
                Write-Ok "Stripped $($_.Name)"
            }
    }
}

function New-Manifest {
    param([string]$ReleaseTag, [string]$Commit)
    Write-Step 'Generating manifest.json'
    $files = @{}
    $stagingFull = (Resolve-Path $Staging).Path
    Get-ChildItem -Path $Staging -Recurse -File | ForEach-Object {
        $rel = $_.FullName.Substring($stagingFull.Length).TrimStart('\','/').Replace('\','/')
        $hash = (Get-FileHash -Path $_.FullName -Algorithm SHA256).Hash.ToUpperInvariant()
        $files[$rel] = $hash
    }
    $manifest = [ordered]@{
        tag               = $ReleaseTag
        built_at          = (Get-Date).ToUniversalTime().ToString('o')
        built_from_commit = $Commit
        files             = $files
    }
    $manifestPath = Join-Path $DistDir 'manifest.json'
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -Path $manifestPath -Encoding utf8
    Write-Ok "manifest.json - $($files.Count) files"
    return $manifestPath
}

function New-Zip {
    param([string]$ReleaseTag)
    $tagSuffix = $ReleaseTag -replace '^mp-', ''
    $zipPath = Join-Path $DistDir "rt-mp-$tagSuffix.zip"
    if (Test-Path $zipPath) {
        if (-not $Force.IsPresent) { throw "$zipPath already exists. Re-run with -Force to overwrite." }
        Remove-Item -Path $zipPath -Force
    }
    Write-Step "Zipping snapshot -> $(Split-Path $zipPath -Leaf)"
    # Zip the *contents* of staging, not the staging folder itself, so the
    # archive root maps directly onto the mod folder root.
    Push-Location $Staging
    try {
        $entries = Get-ChildItem -Path . | ForEach-Object { $_.FullName }
        Compress-Archive -Path $entries -DestinationPath $zipPath -Force
    } finally {
        Pop-Location
    }
    $sizeKb = [math]::Round((Get-Item $zipPath).Length / 1KB)
    Write-Ok "$zipPath ($sizeKb KB)"
    return $zipPath
}

# ---------- main ----------
try {
    Write-Host ''
    Write-Host '=== RimWorld Together MP - Release Builder ===' -ForegroundColor Cyan
    Write-Host ''

    if (-not $Tag) { $Tag = New-AutoTag }
    if ($Tag -notmatch '^mp-') {
        throw "Tag must start with 'mp-' to keep personal builds separate from upstream-sync tags. Got: $Tag"
    }
    Write-Step "Release tag: $Tag"

    Assert-CleanTree
    if (-not $SkipBuild) { Invoke-Build } else { Write-Warn 'Skipping build per -SkipBuild.' }

    if (-not (Test-Path $DistDir)) { New-Item -ItemType Directory -Path $DistDir | Out-Null }

    New-Staging
    $manifestPath = New-Manifest -ReleaseTag $Tag -Commit (Get-GitCommit)
    $zipPath      = New-Zip -ReleaseTag $Tag

    Write-Host ''
    Write-Host '=== Done. Next step: publish the release. ===' -ForegroundColor Cyan
    Write-Host ''
    Write-Host 'Run:' -ForegroundColor White
    Write-Host "    gh release create $Tag ``" -ForegroundColor Gray
    Write-Host "        --repo Majestic95/Rimworld-Together ``" -ForegroundColor Gray
    Write-Host "        --title `"MP Build $Tag`" ``" -ForegroundColor Gray
    Write-Host "        --notes `"<release notes here>`" ``" -ForegroundColor Gray
    Write-Host "        `"$zipPath`" ``" -ForegroundColor Gray
    Write-Host "        `"$manifestPath`"" -ForegroundColor Gray
    Write-Host ''
    Write-Host 'After publishing, friends will pick this up on their next UpdateRTMP.bat run.' -ForegroundColor White
    Write-Host ''
    exit 0
} catch {
    Write-Host ''
    Write-Host "Build failed: $_" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor DarkRed
    exit 1
}

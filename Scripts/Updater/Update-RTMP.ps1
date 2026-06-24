# Update-RTMP.ps1
#
# Enforces an exact-match copy of the Majestic95/Rimworld-Together fork's
# latest "mp-*" release over the local RimWorld Together Steam Workshop install.
# Designed to be run before every play session so that Steam Workshop syncs
# cannot leave a player out of sync with the rest of the playtest group.
#
# Targets Windows PowerShell 5.1 (default on Windows 10/11) so friends do not
# need to install pwsh 7.
#
# Usage:
#   .\Update-RTMP.ps1                 # check + apply latest mp-* release
#   .\Update-RTMP.ps1 -Launch         # also launch RimWorld via Steam afterward
#   .\Update-RTMP.ps1 -Force          # re-download even if local tag matches
#   .\Update-RTMP.ps1 -ConfigPath x   # use an explicit config.json
#
# Config (optional, JSON file next to this script):
#   {
#     "ModFolderOverride": "C:\\path\\to\\steamapps\\workshop\\content\\294100\\3005289691",
#     "Repo": "Majestic95/Rimworld-Together",
#     "TagPrefix": "mp-",
#     "AutoLaunch": false
#   }

[CmdletBinding()]
param(
    [switch]$Launch,
    [switch]$Force,
    [string]$ConfigPath
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = `
    [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

# ---------- constants ----------
$WorkshopAppId   = '294100'
$WorkshopModId   = '3005289691'
$RimWorldAppId   = '294100'
$DefaultRepo     = 'Majestic95/Rimworld-Together'
$DefaultPrefix   = 'mp-'
$CacheRoot       = Join-Path $env:LOCALAPPDATA 'RTMPUpdater'
$CacheDir        = Join-Path $CacheRoot 'cache'
$LogPath         = Join-Path $CacheRoot 'Update-RTMP.log'
$MaxBackups      = 2

# ---------- io helpers ----------
function Write-Log {
    param([string]$Message, [string]$Level = 'INFO')
    $line = "[{0}] [{1}] {2}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Level, $Message
    if (-not (Test-Path $CacheRoot)) { New-Item -ItemType Directory -Path $CacheRoot -Force | Out-Null }
    Add-Content -Path $LogPath -Value $line -Encoding utf8
    $color = switch ($Level) { 'ERROR' {'Red'}; 'WARN' {'Yellow'}; 'OK' {'Green'}; default {'Gray'} }
    Write-Host $line -ForegroundColor $color
}

function Fail {
    param([string]$Message)
    Write-Log $Message 'ERROR'
    exit 1
}

function Read-Config {
    $candidates = @()
    if ($ConfigPath) { $candidates += $ConfigPath }
    $candidates += (Join-Path $PSScriptRoot 'config.json')
    foreach ($p in $candidates) {
        if ($p -and (Test-Path $p)) {
            try { return Get-Content $p -Raw | ConvertFrom-Json }
            catch { Write-Log "Failed to parse config at ${p}: $_" 'WARN' }
        }
    }
    return $null
}

# ---------- steam detection ----------
function Get-SteamInstallPath {
    $regCandidates = @(
        'HKCU:\SOFTWARE\Valve\Steam',
        'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam',
        'HKLM:\SOFTWARE\Valve\Steam'
    )
    foreach ($key in $regCandidates) {
        if (Test-Path $key) {
            $val = (Get-ItemProperty -Path $key -ErrorAction SilentlyContinue)
            $path = $val.SteamPath
            if (-not $path) { $path = $val.InstallPath }
            if ($path -and (Test-Path $path)) { return $path }
        }
    }
    return $null
}

function Get-SteamLibraryFolders {
    param([string]$SteamInstall)
    $libraries = @()
    if (-not $SteamInstall) { return $libraries }
    $libraries += $SteamInstall
    $vdf = Join-Path $SteamInstall 'steamapps\libraryfolders.vdf'
    if (-not (Test-Path $vdf)) { return $libraries }
    $content = Get-Content $vdf -Raw
    # Match lines like:    "path"      "D:\\SteamLibrary"
    $regex = [regex]'"path"\s+"([^"]+)"'
    foreach ($m in $regex.Matches($content)) {
        $p = $m.Groups[1].Value -replace '\\\\', '\'
        if ((Test-Path $p) -and ($libraries -notcontains $p)) { $libraries += $p }
    }
    return $libraries
}

function Resolve-ModFolder {
    param([string]$Override)
    if ($Override) {
        if (-not (Test-Path $Override)) {
            Fail "ModFolderOverride does not exist: $Override"
        }
        return $Override
    }
    $steam = Get-SteamInstallPath
    if (-not $steam) {
        Fail "Could not locate Steam install via registry. Create config.json next to this script with ModFolderOverride pointing at <library>\steamapps\workshop\content\$WorkshopAppId\$WorkshopModId"
    }
    Write-Log "Steam install: $steam"
    $libs = Get-SteamLibraryFolders -SteamInstall $steam
    foreach ($lib in $libs) {
        $candidate = Join-Path $lib "steamapps\workshop\content\$WorkshopAppId\$WorkshopModId"
        if (Test-Path $candidate) {
            Write-Log "Mod folder: $candidate"
            return $candidate
        }
    }
    # Mod folder doesn't exist yet - pick the first library and we'll create it.
    if ($libs.Count -gt 0) {
        $candidate = Join-Path $libs[0] "steamapps\workshop\content\$WorkshopAppId\$WorkshopModId"
        Write-Log "Mod folder does not exist yet, will create at: $candidate" 'WARN'
        return $candidate
    }
    Fail "No Steam libraries found. Subscribe to RimWorld Together on Steam Workshop at least once, then re-run this updater."
}

# ---------- rimworld running check ----------
function Assert-RimWorldClosed {
    $procs = Get-Process -Name 'RimWorldWin64','RimWorld','RimWorldLinux','RimWorldMac' -ErrorAction SilentlyContinue
    if ($procs) {
        Fail "RimWorld is currently running (PID(s): $(($procs | ForEach-Object Id) -join ', ')). Close it fully, then re-run this updater."
    }
}

# ---------- github release ----------
function Get-LatestRelease {
    param([string]$Repo, [string]$TagPrefix)
    $url = "https://api.github.com/repos/$Repo/releases"
    Write-Log "Querying $url"
    try {
        $releases = Invoke-RestMethod -Uri $url -Headers @{ 'User-Agent' = 'RTMPUpdater' } -ErrorAction Stop
    } catch {
        Fail "GitHub API request failed: $_"
    }
    $matching = $releases | Where-Object { $_.tag_name -like "$TagPrefix*" -and -not $_.draft }
    if (-not $matching) {
        Fail "No releases matching '$TagPrefix*' found at $Repo. Wait for the maintainer to publish one."
    }
    return $matching | Select-Object -First 1
}

# ---------- hash + manifest ----------
function Get-Sha256 {
    param([string]$Path)
    return (Get-FileHash -Path $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Test-FolderMatchesManifest {
    param([string]$Root, [hashtable]$Manifest)
    $rootFull = (Resolve-Path $Root).Path.TrimEnd('\','/')
    # Build a set of expected absolute paths, normalized to OS-native separators.
    $expected = New-Object 'System.Collections.Generic.HashSet[string]'
    foreach ($rel in $Manifest.Keys) {
        $native = $rel -replace '/', '\'
        $abs    = [System.IO.Path]::GetFullPath((Join-Path $rootFull $native))
        [void]$expected.Add($abs.ToLowerInvariant())
        if (-not (Test-Path $abs -PathType Leaf)) { return $false }
        if ((Get-Sha256 -Path $abs) -ne $Manifest[$rel].ToUpperInvariant()) { return $false }
    }
    # Reject any extra files not in the manifest (strict equality).
    foreach ($f in Get-ChildItem -Path $Root -Recurse -File) {
        if (-not $expected.Contains($f.FullName.ToLowerInvariant())) { return $false }
    }
    return $true
}

function ConvertTo-ManifestHashtable {
    param($ManifestObject)
    $h = @{}
    foreach ($prop in $ManifestObject.files.PSObject.Properties) {
        $h[$prop.Name] = $prop.Value
    }
    return $h
}

# ---------- download + extract ----------
function Get-ReleaseAssets {
    param($Release, [string]$DestDir)
    if (-not (Test-Path $DestDir)) { New-Item -ItemType Directory -Path $DestDir -Force | Out-Null }
    $zipAsset = $Release.assets | Where-Object { $_.name -like 'rt-mp-*.zip' } | Select-Object -First 1
    $manifestAsset = $Release.assets | Where-Object { $_.name -eq 'manifest.json' } | Select-Object -First 1
    if (-not $zipAsset) { Fail "Release $($Release.tag_name) has no rt-mp-*.zip asset." }
    if (-not $manifestAsset) { Fail "Release $($Release.tag_name) has no manifest.json asset." }

    $zipPath = Join-Path $DestDir $zipAsset.name
    $manifestPath = Join-Path $DestDir $manifestAsset.name

    if (-not (Test-Path $zipPath)) {
        Write-Log "Downloading $($zipAsset.name) ($([math]::Round($zipAsset.size / 1KB)) KB)"
        Invoke-WebRequest -Uri $zipAsset.browser_download_url `
            -OutFile $zipPath -Headers @{ 'User-Agent' = 'RTMPUpdater' } -UseBasicParsing
    } else {
        Write-Log "Using cached $($zipAsset.name)"
    }

    Write-Log "Downloading manifest.json"
    Invoke-WebRequest -Uri $manifestAsset.browser_download_url `
        -OutFile $manifestPath -Headers @{ 'User-Agent' = 'RTMPUpdater' } -UseBasicParsing

    return @{ Zip = $zipPath; Manifest = $manifestPath }
}

function Expand-ToStaging {
    param([string]$ZipPath, [string]$StagingDir)
    if (Test-Path $StagingDir) { Remove-Item -Path $StagingDir -Recurse -Force }
    New-Item -ItemType Directory -Path $StagingDir -Force | Out-Null
    Expand-Archive -Path $ZipPath -DestinationPath $StagingDir -Force
}

# ---------- atomic swap ----------
function Invoke-AtomicSwap {
    param([string]$LiveDir, [string]$StagingDir)
    $parent = Split-Path $LiveDir -Parent
    $tempName = (Split-Path $LiveDir -Leaf) + '-Temp'
    $tempPath = Join-Path $parent $tempName

    # Move staging into temp slot beside live, then swap.
    if (Test-Path $tempPath) { Remove-Item -Path $tempPath -Recurse -Force }
    Move-Item -Path $StagingDir -Destination $tempPath -Force

    if (Test-Path $LiveDir) {
        $backupName = (Split-Path $LiveDir -Leaf) + '.bak-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
        $backupPath = Join-Path $parent $backupName
        Move-Item -Path $LiveDir -Destination $backupPath -Force
        Write-Log "Previous install backed up to $backupName"
    }

    Move-Item -Path $tempPath -Destination $LiveDir -Force
}

function Remove-OldBackups {
    param([string]$LiveDir, [int]$Keep = $MaxBackups)
    $parent = Split-Path $LiveDir -Parent
    $leaf   = Split-Path $LiveDir -Leaf
    $backups = Get-ChildItem -Path $parent -Directory -Filter "$leaf.bak-*" `
        | Sort-Object Name -Descending
    if ($backups.Count -le $Keep) { return }
    $toDelete = $backups | Select-Object -Skip $Keep
    foreach ($b in $toDelete) {
        try {
            Remove-Item -Path $b.FullName -Recurse -Force
            Write-Log "Pruned old backup $($b.Name)"
        } catch {
            Write-Log "Failed to prune $($b.Name): $_" 'WARN'
        }
    }
}

# ---------- main ----------
try {
    Write-Host ''
    Write-Host '=== RimWorld Together MP Updater ===' -ForegroundColor Cyan
    Write-Host ''

    $config = Read-Config
    $repo       = if ($config -and $config.Repo)              { $config.Repo }              else { $DefaultRepo }
    $tagPrefix  = if ($config -and $config.TagPrefix)         { $config.TagPrefix }         else { $DefaultPrefix }
    $override   = if ($config -and $config.ModFolderOverride) { $config.ModFolderOverride } else { $null }
    $autoLaunch = $Launch.IsPresent -or ($config -and $config.AutoLaunch -eq $true)

    Assert-RimWorldClosed

    $modFolder = Resolve-ModFolder -Override $override
    $tagFile   = Join-Path $modFolder '.installed-tag'

    $release = Get-LatestRelease -Repo $repo -TagPrefix $tagPrefix
    $remoteTag = $release.tag_name
    Write-Log "Latest release: $remoteTag (published $($release.published_at))"

    $localTag = if (Test-Path $tagFile) { (Get-Content $tagFile -Raw).Trim() } else { '<none>' }
    Write-Log "Installed tag : $localTag"

    # Always pull the manifest so we can hash-verify even when tags match.
    $assets = Get-ReleaseAssets -Release $release -DestDir (Join-Path $CacheDir $remoteTag)
    $manifestObj = Get-Content $assets.Manifest -Raw | ConvertFrom-Json
    $manifest = ConvertTo-ManifestHashtable -ManifestObject $manifestObj

    $tagsMatch  = ($localTag -eq $remoteTag) -and -not $Force.IsPresent
    $hashesOk   = $false
    if ($tagsMatch -and (Test-Path $modFolder)) {
        Write-Log 'Verifying installed files match manifest...'
        $hashesOk = Test-FolderMatchesManifest -Root $modFolder -Manifest $manifest
    }

    if ($tagsMatch -and $hashesOk) {
        Write-Log "Up to date. Nothing to do." 'OK'
    } else {
        if ($tagsMatch -and -not $hashesOk) {
            Write-Log 'Files drifted from manifest (Steam may have re-synced). Re-applying our build.' 'WARN'
        } else {
            Write-Log "Applying new release $remoteTag."
        }

        # Extract directly beside the live install (same volume) so the final
        # rename swap is a true intra-volume rename, not a copy+delete. This
        # avoids a torn-state window if the user's Steam library is on a
        # different drive than %LOCALAPPDATA%.
        $modParent  = Split-Path $modFolder -Parent
        $stagingDir = Join-Path $modParent ((Split-Path $modFolder -Leaf) + '-staging')
        Expand-ToStaging -ZipPath $assets.Zip -StagingDir $stagingDir

        Write-Log 'Verifying extracted files against manifest...'
        if (-not (Test-FolderMatchesManifest -Root $stagingDir -Manifest $manifest)) {
            Fail 'Extracted snapshot did not match manifest. Aborting before touching the live install.'
        }

        Invoke-AtomicSwap -LiveDir $modFolder -StagingDir $stagingDir
        # NOTE: .installed-tag is shipped INSIDE the snapshot by Build-Release.ps1
        # (so it's covered by the manifest hash check). No post-swap write needed.
        Write-Log "Installed $remoteTag." 'OK'
        Remove-OldBackups -LiveDir $modFolder
    }

    if ($autoLaunch) {
        Write-Log 'Launching RimWorld via Steam...'
        Start-Process "steam://rungameid/$RimWorldAppId"
    } else {
        Write-Host ''
        Write-Host 'Launch RimWorld when ready. Closing this window is safe.' -ForegroundColor Cyan
    }

    exit 0
} catch {
    Write-Log "Unhandled error: $_" 'ERROR'
    Write-Log $_.ScriptStackTrace 'ERROR'
    exit 1
}

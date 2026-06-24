# Update-Server.ps1
#
# Build the RT server from this repo's Source/Server/ and deploy it locally,
# replacing the host's running GameServer.exe. Intended for the host's
# machine only - friends do not run a server.
#
# Targets Windows PowerShell 5.1 (default on Windows 10/11).
#
# Default install path (override with -InstallDir or config.json):
#   $env:USERPROFILE\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\RimWorld Together\Local Server\GameServer.exe
#
# Usage:
#   .\Update-Server.ps1                # build + swap + (optional) launch
#   .\Update-Server.ps1 -NoBuild       # skip dotnet publish (use existing publish output)
#   .\Update-Server.ps1 -Launch        # launch server console after swap
#   .\Update-Server.ps1 -Force         # do not prompt when server is running (will refuse, never kill)
#   .\Update-Server.ps1 -InstallDir X  # override install location
#
# Config (optional, JSON file next to this script):
#   { "InstallDir": "X:\\path\\to\\Local Server", "AutoLaunch": false }

[CmdletBinding()]
param(
    [switch]$NoBuild,
    [switch]$Launch,
    [switch]$Force,
    [string]$InstallDir,
    [string]$ConfigPath
)

$ErrorActionPreference = 'Stop'

# ---------- constants ----------
$RepoRoot          = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$ServerProject     = Join-Path $RepoRoot 'Source\Server\GameServer.csproj'
$PublishDir        = Join-Path $RepoRoot 'Source\Server\bin\Release\net8.0\win-x64\publish'
$PublishExe        = Join-Path $PublishDir 'GameServer.exe'
$DefaultInstallDir = Join-Path $env:USERPROFILE 'AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\RimWorld Together\Local Server'
$BackupPattern     = 'GameServer.exe.bak-*'
$MaxBackups        = 3

# ---------- io helpers ----------
function Write-Step { param([string]$Msg) Write-Host "==> $Msg" -ForegroundColor Cyan }
function Write-Ok   { param([string]$Msg) Write-Host "    $Msg" -ForegroundColor Green }
function Write-Warn { param([string]$Msg) Write-Host "    $Msg" -ForegroundColor Yellow }
function Fail       { param([string]$Msg) Write-Host "Server update failed: $Msg" -ForegroundColor Red; exit 1 }

function Read-Config {
    $candidates = @()
    if ($ConfigPath) { $candidates += $ConfigPath }
    $candidates += (Join-Path $PSScriptRoot 'config.json')
    foreach ($p in $candidates) {
        if ($p -and (Test-Path $p)) {
            try { return Get-Content $p -Raw | ConvertFrom-Json }
            catch { Write-Warn "Failed to parse config at ${p}: $_" }
        }
    }
    return $null
}

# ---------- install-path detection ----------
function Get-InstallDir {
    param([string]$Override, $Config)
    if ($Override) { return $Override }
    if ($Config -and $Config.InstallDir) { return $Config.InstallDir }
    return $DefaultInstallDir
}

# ---------- safety: is server running ----------
function Assert-ServerStopped {
    param([string]$InstallExe)
    # Match any GameServer.exe process whose path is our install location.
    # If the user is running a server elsewhere we ignore it.
    $procs = @()
    try {
        $procs = Get-Process -Name 'GameServer' -ErrorAction SilentlyContinue | Where-Object {
            $_.Path -and ($_.Path -ieq $InstallExe)
        }
    } catch { }
    if ($procs.Count -gt 0) {
        $pidsList = ($procs | ForEach-Object Id) -join ', '
        Write-Warn "Server is running at $InstallExe (PID(s): $pidsList)."
        Write-Warn "Stop it cleanly first: switch to the server console and type 'quit'."
        Write-Warn "(We never kill it for you - kill -9 risks corrupting in-flight saves.)"
        Fail "Server running; aborting before swap."
    }
}

# ---------- build ----------
function Invoke-Publish {
    Write-Step 'Publishing server (Release, win-x64, self-contained, single-file)'
    $argsList = @(
        'publish', $ServerProject,
        '-c', 'Release',
        '-r', 'win-x64',
        '--self-contained', 'true',
        '-p:PublishSingleFile=true',
        '--nologo',
        '-v', 'minimal'
    )
    & dotnet @argsList
    if ($LASTEXITCODE -ne 0) { Fail "dotnet publish failed (exit $LASTEXITCODE)" }
    if (-not (Test-Path $PublishExe)) { Fail "Publish succeeded but expected output missing: $PublishExe" }
    $sizeMb = [math]::Round((Get-Item $PublishExe).Length / 1MB, 1)
    Write-Ok "Published $PublishExe ($sizeMb MB)"
}

# ---------- swap ----------
function Invoke-Swap {
    param([string]$Source, [string]$Target)
    $targetDir = Split-Path $Target -Parent
    if (-not (Test-Path $targetDir)) {
        Write-Step "Install directory does not exist; creating $targetDir"
        New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    }
    if (Test-Path $Target) {
        $backupName = (Split-Path $Target -Leaf) + '.bak-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
        $backupPath = Join-Path $targetDir $backupName
        Copy-Item -Path $Target -Destination $backupPath -Force
        Write-Ok "Current server backed up to $backupName"
    } else {
        Write-Warn 'No existing server at install location; first-time install.'
    }
    Copy-Item -Path $Source -Destination $Target -Force
    $sizeMb = [math]::Round((Get-Item $Target).Length / 1MB, 1)
    Write-Ok "Installed $Target ($sizeMb MB)"
}

function Remove-OldBackups {
    param([string]$Dir, [int]$Keep = $MaxBackups)
    $backups = Get-ChildItem -Path $Dir -File -Filter $BackupPattern -ErrorAction SilentlyContinue `
        | Sort-Object Name -Descending
    if ($backups.Count -le $Keep) { return }
    $toDelete = $backups | Select-Object -Skip $Keep
    foreach ($b in $toDelete) {
        try {
            Remove-Item -Path $b.FullName -Force
            Write-Ok "Pruned old backup $($b.Name)"
        } catch {
            Write-Warn "Failed to prune $($b.Name): $_"
        }
    }
}

# ---------- launch ----------
function Invoke-Launch {
    param([string]$ExePath)
    Write-Step "Launching server: $ExePath"
    $workingDir = Split-Path $ExePath -Parent
    Start-Process -FilePath $ExePath -WorkingDirectory $workingDir
    Write-Ok 'Server process started.'
}

# ---------- main ----------
try {
    Write-Host ''
    Write-Host '=== RimWorld Together - Local Server Updater ===' -ForegroundColor Cyan
    Write-Host ''

    $config     = Read-Config
    $installDir = Get-InstallDir -Override $InstallDir -Config $config
    $installExe = Join-Path $installDir 'GameServer.exe'
    $autoLaunch = $Launch.IsPresent -or ($config -and $config.AutoLaunch -eq $true)

    Write-Step "Install location: $installExe"
    Assert-ServerStopped -InstallExe $installExe

    if (-not $NoBuild) {
        Invoke-Publish
    } else {
        Write-Warn 'Skipping build per -NoBuild.'
        if (-not (Test-Path $PublishExe)) { Fail "No existing publish output at $PublishExe; cannot -NoBuild." }
    }

    Invoke-Swap -Source $PublishExe -Target $installExe
    Remove-OldBackups -Dir $installDir

    if ($autoLaunch) {
        Invoke-Launch -ExePath $installExe
    } else {
        Write-Host ''
        Write-Host 'Server binary updated. Launch when ready:' -ForegroundColor Cyan
        Write-Host "    `"$installExe`"" -ForegroundColor White
        Write-Host ''
    }

    exit 0
} catch {
    Fail "$_"
}

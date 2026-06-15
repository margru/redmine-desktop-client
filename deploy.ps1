<#
.SYNOPSIS
    Deploys the built client (dist\RedmineClient) to a run location, safely.

.DESCRIPTION
    Copying a new build over a running RedmineClient.exe silently fails - Windows
    locks a running executable, so Explorer (and copy commands) skip the .exe while
    happily overwriting the unlocked files next to it. The result is a "deployed"
    folder whose .exe is still the old build. This script avoids that trap:

      1. Finds any RedmineClient.exe running from the target folder and closes it
         (gracefully first, so it saves its settings/timer state, then force-kills
         only if it refuses to exit).
      2. Copies dist\RedmineClient\* over the target.
      3. Verifies the target .exe now byte-for-byte matches the source (MD5), so a
         half-finished copy can't pass silently.

    Run build.ps1 first (or pass -Build) so dist\RedmineClient is current.

.PARAMETER Target
    Folder to deploy into. Default: C:\Programs\RedmineClient.

.PARAMETER Build
    Run build.ps1 before deploying.

.PARAMETER Force
    Skip the confirmation prompt before closing a running app.

.EXAMPLE
    .\deploy.ps1
.EXAMPLE
    .\deploy.ps1 -Build -Force
#>
[CmdletBinding()]
param(
    [string]$Target = "C:\Programs\RedmineClient",
    [switch]$Build,
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$source = Join-Path $root "dist\RedmineClient"

function Write-Step($msg) { Write-Host "`n==> $msg" -ForegroundColor Cyan }

# --- 1. Optionally build -------------------------------------------------------
if ($Build) {
    Write-Step "Building (build.ps1)"
    & (Join-Path $root "build.ps1")
    if ($LASTEXITCODE -ne 0) { throw "build.ps1 failed." }
}

$sourceExe = Join-Path $source "RedmineClient.exe"
if (-not (Test-Path $sourceExe)) {
    throw "Build output not found at '$sourceExe'. Run build.ps1 first (or pass -Build)."
}

# --- 2. Close any app running from the target ----------------------------------
Write-Step "Checking for a running app in '$Target'"
$targetExe = Join-Path $Target "RedmineClient.exe"
$running = @(Get-Process RedmineClient -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and ((Resolve-Path $_.Path).Path -ieq (Resolve-Path $targetExe -ErrorAction SilentlyContinue).Path) })

if ($running.Count -gt 0) {
    if (-not $Force) {
        $answer = Read-Host "RedmineClient is running from the target (PID $($running.Id -join ', ')). Close it and deploy? [y/N]"
        if ($answer -notmatch '^(y|yes)$') { throw "Aborted - app left running, nothing copied." }
    }
    foreach ($p in $running) {
        Write-Host "Closing PID $($p.Id) ..."
        $null = $p.CloseMainWindow()         # graceful: lets it save settings/timer state
    }
    # Give it a few seconds to exit on its own before forcing.
    for ($i = 0; $i -lt 10 -and -not $running[0].HasExited; $i++) {
        Start-Sleep -Milliseconds 500
        $running = @(Get-Process -Id ($running.Id) -ErrorAction SilentlyContinue)
        if ($running.Count -eq 0) { break }
    }
    $stubborn = @(Get-Process RedmineClient -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and ((Resolve-Path $_.Path).Path -ieq (Resolve-Path $targetExe -ErrorAction SilentlyContinue).Path) })
    if ($stubborn.Count -gt 0) {
        Write-Host "Still running; force-stopping..." -ForegroundColor Yellow
        $stubborn | Stop-Process -Force
        Start-Sleep -Milliseconds 500
    }
} else {
    Write-Host "Not running."
}

# --- 3. Copy ------------------------------------------------------------------
Write-Step "Copying '$source' -> '$Target'"
New-Item -ItemType Directory -Force -Path $Target | Out-Null
Copy-Item (Join-Path $source "*") $Target -Recurse -Force

# --- 4. Verify ----------------------------------------------------------------
Write-Step "Verifying"
$srcHash = (Get-FileHash $sourceExe -Algorithm MD5).Hash
$dstHash = (Get-FileHash $targetExe -Algorithm MD5).Hash
Write-Host "source: $srcHash"
Write-Host "target: $dstHash"
if ($srcHash -ne $dstHash) {
    throw "Verification FAILED - target .exe does not match source. Is the app still running?"
}
Write-Host "Deployed and verified: $targetExe" -ForegroundColor Green

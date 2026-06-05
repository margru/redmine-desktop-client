<#
.SYNOPSIS
    Builds the Redmine Desktop Client and assembles a runnable folder under .\dist\RedmineClient.

.DESCRIPTION
    This machine has Visual Studio Build Tools (MSBuild + Roslyn csc) but NOT a .NET Framework
    SDK / Developer Pack, so two things are missing that a normal `msbuild RedmineClient.sln`
    needs:

      1. Reference assemblies (targeting packs) for the .NET Framework versions the projects
         target. We supply them by downloading the NuGet "reference assemblies" packages and
         pointing MSBuild at them via FrameworkPathOverride.
      2. al.exe (the Assembly Linker, used to build the localized satellite assemblies). We
         build the satellites with Roslyn csc.exe instead.

    If you install the .NET Framework 4.8 Developer Pack (https://aka.ms/msbuild/developerpacks)
    AND the 4.5.1 targeting pack, you can ignore this script and just run:
        msbuild RedmineClient.sln /p:Configuration=Release
    The Developer Pack provides both the reference assemblies and al.exe.

    This script needs nothing pre-installed beyond VS Build Tools and internet access (for the
    one-time NuGet reference-assembly download) and the git submodule.

.PARAMETER Configuration
    Build configuration. Default: Release.

.EXAMPLE
    .\build.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$clientProj = Join-Path $root "Redmine.Client\Redmine.Client.csproj"
$apiProj    = Join-Path $root "Redmine.Api\redmine-net451-api\redmine-net451-api.csproj"
$refRoot    = Join-Path $root ".refassemblies"

function Write-Step($msg) { Write-Host "`n==> $msg" -ForegroundColor Cyan }

# --- 1. Locate MSBuild and csc -------------------------------------------------
Write-Step "Locating MSBuild and Roslyn csc"
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = $null
if (Test-Path $vswhere) {
    $msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild `
        -find MSBuild\**\Bin\MSBuild.exe | Select-Object -First 1
}
if (-not $msbuild -or -not (Test-Path $msbuild)) {
    throw "MSBuild.exe not found. Install Visual Studio or the Build Tools for Visual Studio."
}
$csc = Join-Path (Split-Path $msbuild) "Roslyn\csc.exe"
if (-not (Test-Path $csc)) { throw "Roslyn csc.exe not found next to MSBuild at: $csc" }
Write-Host "MSBuild: $msbuild"
Write-Host "csc:     $csc"

# --- 2. Ensure the Redmine.Api submodule is checked out ------------------------
Write-Step "Ensuring Redmine.Api submodule is initialized"
if (-not (Test-Path (Join-Path $root "Redmine.Api\redmine-net451-api\redmine-net451-api.csproj"))) {
    & git -C $root submodule update --init --recursive
    if ($LASTEXITCODE -ne 0) { throw "git submodule update failed." }
} else {
    Write-Host "Submodule already present."
}

# --- 3. Download reference assemblies (targeting packs) as NuGet packages -------
function Get-RefAssemblies {
    param([string]$PackageId, [string]$Version, [string]$RelativeRefDir)
    $dest = Join-Path $refRoot $PackageId
    $refDir = Join-Path $dest $RelativeRefDir
    if (Test-Path $refDir) { return $refDir }
    Write-Host "Downloading $PackageId $Version ..."
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    $zip = Join-Path $dest "$PackageId.zip"
    Invoke-WebRequest -Uri "https://www.nuget.org/api/v2/package/$PackageId/$Version" `
        -OutFile $zip -UseBasicParsing
    Expand-Archive -Path $zip -DestinationPath $dest -Force
    if (-not (Test-Path $refDir)) { throw "Expected reference assemblies at: $refDir" }
    return $refDir
}

Write-Step "Ensuring reference assemblies"
$ref451 = Get-RefAssemblies "Microsoft.NETFramework.ReferenceAssemblies.net451" "1.0.3" "build\.NETFramework\v4.5.1"
$ref48  = Get-RefAssemblies "Microsoft.NETFramework.ReferenceAssemblies.net48"  "1.0.3" "build\.NETFramework\v4.8"
Write-Host "net4.5.1 refs: $ref451"
Write-Host "net4.8   refs: $ref48"

# --- 4. Build the Redmine.Api dependency (targets net4.5.1) --------------------
Write-Step "Building Redmine.Api (net4.5.1)"
& $msbuild $apiProj /p:Configuration=$Configuration /p:Platform=AnyCPU `
    /p:FrameworkPathOverride="$ref451" /t:Build /v:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw "Redmine.Api build failed." }

# --- 5. Build the client (targets net4.8) --------------------------------------
# The al.exe (satellite) step runs after the main assembly is compiled and will fail on this
# machine, so we compile to obj, then copy the fresh exe out and build satellites with csc.
Write-Step "Compiling Redmine.Client (net4.8)"
$objDir = Join-Path $root "Redmine.Client\obj\$Configuration"
$objExe = Join-Path $objDir "RedmineClient.exe"
$objCfg = Join-Path $objDir "RedmineClient.exe.config"
Remove-Item $objExe -Force -ErrorAction SilentlyContinue
# Allow the satellite/al.exe step to fail without aborting the script.
$global:LASTEXITCODE = 0
& $msbuild $clientProj /p:Configuration=$Configuration /p:Platform=AnyCPU `
    /p:FrameworkPathOverride="$ref48" /p:BuildProjectReferences=false `
    /t:Build /v:minimal /nologo
if (-not (Test-Path $objExe)) {
    throw "Client compilation failed - $objExe was not produced."
}
Write-Host "Compiled: $objExe"

# --- 6. Build satellite (localized) assemblies with csc ------------------------
Write-Step "Building satellite assemblies with csc"
$asmVersion = [System.Reflection.AssemblyName]::GetAssemblyName($objExe).Version.ToString()
Write-Host "Assembly version: $asmVersion"
$prefix = "Redmine.Client.Languages.Lang."
$satellites = @()
Get-ChildItem $objDir -Filter "$prefix*.resources" | ForEach-Object {
    # e.g. Redmine.Client.Languages.Lang.de.resources -> culture "de"
    $culture = $_.BaseName.Substring($prefix.Length)
    if ([string]::IsNullOrEmpty($culture)) { return }  # neutral resources (embedded in the exe)
    $outDir = Join-Path $objDir $culture
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
    $out = Join-Path $outDir "RedmineClient.resources.dll"
    $srcFile = Join-Path $env:TEMP ("sat_" + ($culture -replace '[^A-Za-z0-9]', '_') + ".cs")
    Set-Content -Path $srcFile -Encoding UTF8 -Value @"
[assembly: System.Reflection.AssemblyVersion("$asmVersion")]
[assembly: System.Reflection.AssemblyCulture("$culture")]
"@
    & $csc /nologo /target:library /out:"$out" `
        /resource:"$($_.FullName)","$($_.Name)" "$srcFile" | Out-Null
    Remove-Item $srcFile -Force -ErrorAction SilentlyContinue
    if (-not (Test-Path $out)) { throw "Failed to build satellite for culture '$culture'." }
    $satellites += [pscustomobject]@{ Culture = $culture; Path = $out }
}
Write-Host ("Built {0} satellite assemblies: {1}" -f $satellites.Count, (($satellites.Culture) -join ", "))

# --- 7. Assemble the dist folder ----------------------------------------------
Write-Step "Assembling dist\RedmineClient"
$dist = Join-Path $root "dist\RedmineClient"
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Force -Path $dist | Out-Null

Copy-Item $objExe $dist
Copy-Item $objCfg $dist
Copy-Item (Join-Path $root "Redmine.Api\redmine-net451-api\bin\$Configuration\redmine-net451-api.dll") $dist
foreach ($s in $satellites) {
    $cultureDir = Join-Path $dist $s.Culture
    New-Item -ItemType Directory -Force -Path $cultureDir | Out-Null
    Copy-Item $s.Path $cultureDir
}

Write-Step "Done"
Write-Host "Output: $dist" -ForegroundColor Green
Get-ChildItem $dist -Recurse | ForEach-Object {
    $rel = $_.FullName.Substring($dist.Length + 1)
    if ($_.PSIsContainer) { "  [dir]  $rel" } else { "         $rel" }
}

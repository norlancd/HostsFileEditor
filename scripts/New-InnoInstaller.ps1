<#
.SYNOPSIS
    Publishes Hosts File Editor and packages it into classic Inno Setup installers (setup.exe).
.DESCRIPTION
    Unlike the MSIX installers (New-WinFormInstaller.ps1 / New-WinUIInstaller.ps1), the Inno Setup output
    is a traditional setup.exe that installs into Program Files with Start Menu / optional desktop
    shortcuts and an uninstaller. It does NOT require trusting a code-signing certificate to install
    (MSIX packages signed with the repo's self-signed test cert cannot be installed until that cert is
    trusted; a setup.exe has no such restriction - at most Windows SmartScreen shows a dismissable prompt
    because the exe isn't signed).

    The app is framework-dependent, so target machines need the matching .NET Desktop Runtime installed.

    Publishing is done with -p:SkipMsixPackaging=true so the (slow, cert-dependent) makeappx/signtool
    MSIX pipeline in Directory.Build.targets is bypassed - this script only needs the plain publish output.
.PARAMETER Flavor
    Which variant(s) to package: classic (WinForms), modern (WinUI), or both. Defaults to both.
.PARAMETER Configuration
    Build configuration, Debug or Release. Defaults to Release.
.PARAMETER SkipPublish
    Reuse the existing publish output instead of running dotnet publish first.
.PARAMETER Clean
    Run `dotnet clean` before publishing.
.PARAMETER IsccPath
    Explicit path to ISCC.exe (the Inno Setup compiler). Auto-detected from common install locations.
.PARAMETER PublishTimeoutSeconds
    Max seconds to wait for `dotnet publish` before treating it as hung and retrying. Defaults to 120.
.PARAMETER UseEnvironmentalTools
    modern (WinUI) publishes with Native AOT, whose linker step shells out to link.exe located via
    vswhere.exe. On machines where vswhere isn't on PATH the AOT recompile fails; loading the MSVC dev
    environment (vcvarsall.bat) and passing -p:IlcUseEnvironmentalTools=true resolves the linker from
    PATH instead. Ignored for the classic flavor (no AOT). Mirrors New-WinUIInstaller.ps1.
.PARAMETER VcVarsAllPath
    Explicit path to vcvarsall.bat. Auto-detected when not specified. Only used for the modern flavor.
.EXAMPLE
    .\scripts\New-InnoInstaller.ps1
.EXAMPLE
    .\scripts\New-InnoInstaller.ps1 -Flavor classic -Clean
#>
[CmdletBinding()]
param(
    [ValidateSet('classic', 'modern', 'both')]
    [string]$Flavor = 'both',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$SkipPublish,

    [switch]$Clean,

    [string]$IsccPath,

    [int]$PublishTimeoutSeconds = 120,

    [bool]$UseEnvironmentalTools = $true,

    [string]$VcVarsAllPath
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'Common.ps1')

$repoRoot = Split-Path -Parent $PSScriptRoot
$arch = 'x64'
$rid = 'win-x64'
$appExeName = 'HostsFileEditor.exe'
$issFile = Join-Path $PSScriptRoot 'installer\HostsFileEditor.iss'

function Import-VcVarsEnvironment {
    param([string]$VcVarsAllBat, [string]$Arch)

    $envDump = cmd /c "`"$VcVarsAllBat`" $Arch >NUL 2>&1 && set"
    if ($LASTEXITCODE -ne 0) { throw "vcvarsall.bat failed for arch '$Arch' (exit code $LASTEXITCODE)" }

    foreach ($line in $envDump) {
        if ($line -match '^([^=]+)=(.*)$') {
            [System.Environment]::SetEnvironmentVariable($matches[1], $matches[2], 'Process')
        }
    }
}

function New-InnoInstallerForFlavor {
    param([Parameter(Mandatory)][ValidateSet('classic', 'modern')][string]$Flavor)

    # Per-flavor project + publish layout.
    if ($Flavor -eq 'classic') {
        $project      = Join-Path $repoRoot 'HostsFileEditor.WinForm\HostsFileEditor.WinForm.csproj'
        $publishDir   = Join-Path $repoRoot "HostsFileEditor.WinForm\bin\$Configuration\net10.0-windows\$rid\publish"
        $iconFile     = Join-Path $repoRoot 'HostsFileEditor.WinForm\HostsFileEditor.ico'
        $platformArgs = @()
    }
    else {
        $project      = Join-Path $repoRoot 'HostsFileEditor.WinUI\HostsFileEditor.WinUI.csproj'
        $publishDir   = Join-Path $repoRoot "HostsFileEditor.WinUI\bin\$arch\$Configuration\net10.0-windows10.0.19041.0\$rid\publish"
        $iconFile     = Join-Path $repoRoot 'HostsFileEditor.WinUI\Assets\HostsFileEditor.ico'
        $platformArgs = @('-r', $rid, "-p:Platform=$arch")
    }

    $outputDir = Join-Path $repoRoot "artifacts\$Flavor"

    if (-not (Test-Path $project))  { throw "Project not found: $project" }
    if (-not (Test-Path $iconFile)) { throw "Icon not found: $iconFile" }

    if ($Clean) {
        Write-Host "Cleaning $Flavor project..." -ForegroundColor Cyan
        dotnet clean $project -c $Configuration
        if ($LASTEXITCODE -ne 0) { throw "dotnet clean failed with exit code $LASTEXITCODE" }
    }

    if (-not $SkipPublish) {
        $publishArgs = @($project, '-c', $Configuration) + $platformArgs + @('-p:SkipMsixPackaging=true')

        # modern uses Native AOT - load the MSVC toolchain so a native recompile can find its linker.
        if ($Flavor -eq 'modern' -and $UseEnvironmentalTools) {
            if (-not $script:VcVarsAllPath) {
                $candidates = Get-ChildItem -Path 'C:\Program Files*\Microsoft Visual Studio\*\*\VC\Auxiliary\Build\vcvarsall.bat' -ErrorAction SilentlyContinue
                if (-not $candidates) { throw "Could not auto-detect vcvarsall.bat. Pass -VcVarsAllPath explicitly." }
                $script:VcVarsAllPath = $candidates[0].FullName
            }
            Write-Host "Using vcvarsall.bat: $script:VcVarsAllPath" -ForegroundColor DarkGray
            Import-VcVarsEnvironment -VcVarsAllBat $script:VcVarsAllPath -Arch 'x64'
            $publishArgs += '-p:IlcUseEnvironmentalTools=true'
        }

        Write-Host "Publishing $Flavor ($Configuration) for Inno packaging..." -ForegroundColor Cyan
        Invoke-DotnetPublishWithTimeout -PublishArgs $publishArgs -WorkingDirectory $repoRoot -TimeoutSeconds $PublishTimeoutSeconds
    }

    if (-not (Test-Path (Join-Path $publishDir $appExeName))) {
        throw "Expected published exe not found: $(Join-Path $publishDir $appExeName). Run without -SkipPublish."
    }

    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

    Write-Host "Compiling Inno Setup installer ($Flavor)..." -ForegroundColor Cyan
    $version = (Get-Item (Join-Path $publishDir $appExeName)).VersionInfo.FileVersion
    if (-not $version) { $version = '1.0.0.0' }

    $isccArgs = @(
        "/DMyFlavor=$Flavor",
        "/DMyArch=$arch",
        "/DMyVersion=$version",
        "/DMySourceDir=$publishDir",
        "/DMyAppExeName=$appExeName",
        "/DMyAppIcon=$iconFile",
        "/DMyOutputDir=$outputDir",
        $issFile
    )
    & $script:IsccPath @isccArgs
    if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE" }

    $setup = Join-Path $outputDir "HostsFileEditor-$Flavor-$arch-setup.exe"
    if (-not (Test-Path $setup)) { throw "ISCC reported success but $setup was not found." }
    return $setup
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw "dotnet SDK not found on PATH." }
if (-not (Test-Path $issFile)) { throw "Inno script not found: $issFile" }

# Locate the Inno Setup compiler.
if (-not $IsccPath) {
    $candidates = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    )
    $IsccPath = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $IsccPath) {
        $found = Get-ChildItem "$env:LOCALAPPDATA\Programs", "C:\Program Files*" -Filter ISCC.exe -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($found) { $IsccPath = $found.FullName }
    }
    if (-not $IsccPath) {
        throw "ISCC.exe (Inno Setup compiler) not found. Install it with: winget install --id JRSoftware.InnoSetup -e"
    }
}
Write-Host "Using ISCC: $IsccPath" -ForegroundColor DarkGray

# classic before modern: modern's vcvars import mutates this process's env, so run the flavor that
# doesn't need it first.
$flavorsToBuild = if ($Flavor -eq 'both') { @('classic', 'modern') } else { @($Flavor) }

# One lock for the whole run (not per-flavor) so a 'both' run and a single-flavor run can't overlap.
$lock = Enter-SingleInstanceLock -Name 'HostsFileEditor-New-InnoInstaller'
try {
    $built = @()
    foreach ($f in $flavorsToBuild) {
        $built += New-InnoInstallerForFlavor -Flavor $f
    }

    Write-Host "`nInno installer(s) created:" -ForegroundColor Green
    $built | ForEach-Object { Write-Host "  $_" }
}
finally {
    $lock.ReleaseMutex()
    $lock.Dispose()
}

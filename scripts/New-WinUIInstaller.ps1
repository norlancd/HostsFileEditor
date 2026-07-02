<#
.SYNOPSIS
    Publishes and packages the WinUI 3 (modern) flavor of Hosts File Editor as signed MSIX installers.
.DESCRIPTION
    Wraps `dotnet publish` for HostsFileEditor.WinUI, one run per target platform (x64/arm64). The actual
    MSIX creation and signing is done by the SignAndPackage MSBuild target (Directory.Build.targets),
    which runs automatically after publish and requires makeappx.exe / signtool.exe from the Windows SDK
    to be resolvable (PATH, VS dev shell, or registry-discovered install).

    SignAndPackage always writes to the project's fixed OutputPackagingDir (artifacts\modern\) using the
    assembly name, so each platform's MSIX would overwrite the previous one. This script renames each
    MSIX to HostsFileEditor-modern-<platform>.msix immediately after publishing so both architectures
    survive, matching the HostsFileEditor-classic-* naming used by New-WinFormInstaller.ps1.
.PARAMETER Configuration
    Build configuration, Debug or Release. Defaults to Release.
.PARAMETER Platforms
    One or more target platforms to build: x64, arm64. Defaults to x64 only, since arm64 needs the
    separate "C++ ARM64 build tools" component - pass -Platforms x64,arm64 once that's installed.
.PARAMETER CertFile
    Path to the code-signing certificate (.pfx). Defaults to the repo test certificate.
.PARAMETER CertPassword
    Password for the certificate. Defaults to the test certificate's password.
.PARAMETER SkipSigning
    Skip signing the exe/msix (useful when no certificate is available).
.PARAMETER Clean
    Run `dotnet clean` before publishing.
.PARAMETER UseEnvironmentalTools
    Native AOT normally locates link.exe/lib.exe via `vswhere.exe`. On this machine (and others where a
    C++ workload was installed moments earlier, or on minimal CI images) vswhere fails to see the VS/Build
    Tools instance even though the toolset is on disk - this only bites when a Native AOT recompile is
    actually triggered, which doesn't happen on every run, so a plain "no flags" invocation can work for a
    while and then fail unpredictably once something forces a rebuild. Defaults to true so a no-args run
    is reliable either way: it loads the MSVC dev environment via vcvarsall.bat into this process and
    passes -p:IlcUseEnvironmentalTools=true so the linker is resolved from PATH instead of vswhere. Pass
    -UseEnvironmentalTools:$false to opt out (e.g. on a machine where vswhere works fine).
.PARAMETER VcVarsAllPath
    Explicit path to vcvarsall.bat. Only used with -UseEnvironmentalTools. Auto-detected from common VS
    2022/Build Tools install locations when not specified.
.PARAMETER PublishTimeoutSeconds
    Max seconds to wait for `dotnet publish` (per platform) before treating it as hung and retrying.
    Defaults to 120 - a real successful run (including a from-clean Native AOT compile) takes well under
    that, so anything past it is very likely a hang, not a slow-but-working build.
.EXAMPLE
    .\scripts\New-WinUIInstaller.ps1
.EXAMPLE
    .\scripts\New-WinUIInstaller.ps1 -Platforms x64 -Configuration Debug -SkipSigning
.EXAMPLE
    .\scripts\New-WinUIInstaller.ps1 -Platforms x64 -UseEnvironmentalTools:$false
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateSet('x64', 'arm64')]
    [string[]]$Platforms = @('x64'),

    [string]$CertFile,

    [string]$CertPassword,

    [switch]$SkipSigning,

    [switch]$Clean,

    [bool]$UseEnvironmentalTools = $true,

    [string]$VcVarsAllPath,

    [int]$PublishTimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'Common.ps1')

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'HostsFileEditor.WinUI\HostsFileEditor.WinUI.csproj'
$outputDir = Join-Path $repoRoot 'artifacts\modern'

$ridByPlatform = @{ x64 = 'win-x64'; arm64 = 'win-arm64' }
$vcVarsArchByPlatform = @{ x64 = 'x64'; arm64 = 'x64_arm64' }

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

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet SDK not found on PATH."
}

if (-not (Test-Path $project)) {
    throw "Project not found: $project"
}

if ($UseEnvironmentalTools -and -not $VcVarsAllPath) {
    $candidates = Get-ChildItem -Path 'C:\Program Files*\Microsoft Visual Studio\*\*\VC\Auxiliary\Build\vcvarsall.bat' -ErrorAction SilentlyContinue
    if (-not $candidates) {
        throw "Could not auto-detect vcvarsall.bat. Pass -VcVarsAllPath explicitly."
    }
    $VcVarsAllPath = $candidates[0].FullName
    Write-Host "Using vcvarsall.bat: $VcVarsAllPath" -ForegroundColor DarkGray
}

$lock = Enter-SingleInstanceLock -Name 'HostsFileEditor-New-WinUIInstaller'
try {
    if ($Clean) {
        Write-Host "Cleaning WinUI project..." -ForegroundColor Cyan
        dotnet clean $project -c $Configuration
        if ($LASTEXITCODE -ne 0) { throw "dotnet clean failed with exit code $LASTEXITCODE" }
    }

    foreach ($platform in $Platforms) {
        $rid = $ridByPlatform[$platform]

        Write-Host "`nPublishing WinUI (modern) installer for $rid ($Configuration)..." -ForegroundColor Cyan

        $publishArgs = @($project, '-c', $Configuration, '-r', $rid, "-p:Platform=$platform")
        if ($CertFile)     { $publishArgs += "-p:CertFile=$CertFile" }
        if ($CertPassword) { $publishArgs += "-p:CertPassword=$CertPassword" }
        if ($SkipSigning)  { $publishArgs += '-p:EnableSigning=false' }

        if ($UseEnvironmentalTools) {
            Import-VcVarsEnvironment -VcVarsAllBat $VcVarsAllPath -Arch $vcVarsArchByPlatform[$platform]
            $publishArgs += '-p:IlcUseEnvironmentalTools=true'
        }

        # SignAndPackage is incremental and only tracks "did I run before" via a stamp file - it has no
        # idea whether artifacts\modern\ still has the deliverables it copied last time. If that folder
        # got wiped (or never existed) but the stamp/inputs look unchanged, MSBuild skips packaging
        # entirely and this script - whose whole job is "produce an installer" - would silently produce
        # nothing. Deleting the stamp before every run forces SignAndPackage to always execute.
        $stampFile = Join-Path $repoRoot 'HostsFileEditor.WinUI\obj\packaging\HostsFileEditor.WinUI.stamp'
        Remove-Item -Path $stampFile -Force -ErrorAction SilentlyContinue

        Invoke-DotnetPublishWithTimeout -PublishArgs $publishArgs -WorkingDirectory $repoRoot -TimeoutSeconds $PublishTimeoutSeconds

        $msix = Join-Path $outputDir 'HostsFileEditor.msix'
        $renamedMsix = Join-Path $outputDir "HostsFileEditor-modern-$platform.msix"
        if (Test-Path $msix) {
            Move-Item -Path $msix -Destination $renamedMsix -Force
        } elseif (-not (Test-Path $renamedMsix)) {
            # SignAndPackage is incremental - if nothing changed since the last run it's skipped and no
            # HostsFileEditor.msix is produced. That's only a problem if the already-renamed artifact from
            # a previous run isn't there either.
            Write-Warning "Expected MSIX not found at $msix after publishing $rid"
        }

        $zip = Join-Path $outputDir 'HostsFileEditor.zip'
        if (Test-Path $zip) {
            Move-Item -Path $zip -Destination (Join-Path $outputDir "HostsFileEditor-modern-$platform-portable.zip") -Force
        }
    }

    $artifacts = Get-ChildItem -Path $outputDir -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -in '.msix', '.zip' }

    if (-not $artifacts) {
        Write-Warning "No installer artifacts found in $outputDir"
    } else {
        Write-Host "`nWinUI installer artifacts:" -ForegroundColor Green
        $artifacts | ForEach-Object { Write-Host "  $($_.FullName)" }
    }
}
finally {
    $lock.ReleaseMutex()
    $lock.Dispose()
}

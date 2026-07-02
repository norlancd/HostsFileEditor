<#
.SYNOPSIS
    Publishes and packages the WinForms (classic) flavor of Hosts File Editor as a signed MSIX installer.
.DESCRIPTION
    Wraps `dotnet publish` for HostsFileEditor.WinForm. The actual MSIX creation and signing is done by
    the SignAndPackage MSBuild target (Directory.Build.targets), which runs automatically after publish
    and requires makeappx.exe / signtool.exe from the Windows SDK to be resolvable (PATH, VS dev shell,
    or registry-discovered install).
.PARAMETER Configuration
    Build configuration, Debug or Release. Defaults to Release.
.PARAMETER CertFile
    Path to the code-signing certificate (.pfx). Defaults to the repo test certificate.
.PARAMETER CertPassword
    Password for the certificate. Defaults to the test certificate's password.
.PARAMETER SkipSigning
    Skip signing the exe/msix (useful when no certificate is available).
.PARAMETER Clean
    Run `dotnet clean` before publishing.
.PARAMETER PublishTimeoutSeconds
    Max seconds to wait for `dotnet publish` before treating it as hung and retrying. Defaults to 120 -
    a real successful run takes well under a minute, so anything past that is very likely a hang, not a
    slow-but-working build.
.EXAMPLE
    .\scripts\New-WinFormInstaller.ps1
.EXAMPLE
    .\scripts\New-WinFormInstaller.ps1 -Configuration Debug -SkipSigning
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$CertFile,

    [string]$CertPassword,

    [switch]$SkipSigning,

    [switch]$Clean,

    [int]$PublishTimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'Common.ps1')

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'HostsFileEditor.WinForm\HostsFileEditor.WinForm.csproj'
$outputDir = Join-Path $repoRoot 'artifacts\classic'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet SDK not found on PATH."
}

if (-not (Test-Path $project)) {
    throw "Project not found: $project"
}

$lock = Enter-SingleInstanceLock -Name 'HostsFileEditor-New-WinFormInstaller'
try {
    if ($Clean) {
        Write-Host "Cleaning WinForm project..." -ForegroundColor Cyan
        dotnet clean $project -c $Configuration
        if ($LASTEXITCODE -ne 0) { throw "dotnet clean failed with exit code $LASTEXITCODE" }
    }

    $publishArgs = @($project, '-c', $Configuration)
    if ($CertFile)     { $publishArgs += "-p:CertFile=$CertFile" }
    if ($CertPassword) { $publishArgs += "-p:CertPassword=$CertPassword" }
    if ($SkipSigning)  { $publishArgs += '-p:EnableSigning=false' }

    # SignAndPackage is incremental and only tracks "did I run before" via a stamp file - it has no idea
    # whether artifacts\classic\ still has the deliverables it copied last time. If that folder got wiped
    # (or never existed) but the stamp/inputs look unchanged, MSBuild skips packaging entirely and this
    # script - whose whole job is "produce an installer" - would silently produce nothing. Deleting the
    # stamp before every run forces SignAndPackage to always execute.
    $stampFile = Join-Path $repoRoot 'HostsFileEditor.WinForm\obj\packaging\HostsFileEditor.WinForm.stamp'
    Remove-Item -Path $stampFile -Force -ErrorAction SilentlyContinue

    Write-Host "Publishing WinForm (classic) installer ($Configuration)..." -ForegroundColor Cyan
    Invoke-DotnetPublishWithTimeout -PublishArgs $publishArgs -WorkingDirectory $repoRoot -TimeoutSeconds $PublishTimeoutSeconds

    $msix = Join-Path $outputDir 'HostsFileEditor.msix'
    if (Test-Path $msix) {
        Move-Item -Path $msix -Destination (Join-Path $outputDir 'HostsFileEditor-classic-x64.msix') -Force
    }

    $zip = Join-Path $outputDir 'HostsFileEditor.zip'
    if (Test-Path $zip) {
        Move-Item -Path $zip -Destination (Join-Path $outputDir 'HostsFileEditor-classic-x64-portable.zip') -Force
    }

    $artifacts = Get-ChildItem -Path $outputDir -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -in '.msix', '.zip' }

    if (-not $artifacts) {
        Write-Warning "No installer artifacts found in $outputDir"
    } else {
        Write-Host "`nWinForm installer artifacts:" -ForegroundColor Green
        $artifacts | ForEach-Object { Write-Host "  $($_.FullName)" }
    }
}
finally {
    $lock.ReleaseMutex()
    $lock.Dispose()
}

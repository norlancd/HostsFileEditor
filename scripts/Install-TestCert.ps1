<#
.SYNOPSIS
    Trusts the self-signed code-signing certificate so its MSIX packages can be installed by double-click.
.DESCRIPTION
    Installing an MSIX signed with a self-signed certificate fails with 0x800B010A ("a certificate chain
    could not be built to a trusted root authority") until that certificate is trusted on the machine.
    This imports the public certificate into the Local Machine "Trusted Root Certification Authorities"
    and "Trusted People" stores (self-signed -> the cert is its own root, so it must live in Root; App
    Installer additionally consults Trusted People for sideloaded packages).

    Requires administrator elevation to write the Local Machine stores - the script relaunches itself
    elevated if needed.
.PARAMETER CertFile
    Path to the code-signing certificate (.pfx). Defaults to the repo test certificate.
.PARAMETER CertPassword
    Password for the certificate. Defaults to the test certificate's password.
.PARAMETER Uninstall
    Remove the certificate from those stores instead of adding it.
.EXAMPLE
    .\scripts\Install-TestCert.ps1
.EXAMPLE
    .\scripts\Install-TestCert.ps1 -Uninstall
#>
[CmdletBinding()]
param(
    [string]$CertFile,
    [string]$CertPassword = 'test',
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $CertFile) { $CertFile = Join-Path $repoRoot 'HostsFileEditorTestCert.pfx' }
$CertFile = (Resolve-Path $CertFile).Path

# Relaunch elevated if we can't write the Local Machine store.
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "Elevation required - relaunching as administrator..." -ForegroundColor Yellow
    $argList = @('-NoExit', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-CertFile', "`"$CertFile`"", '-CertPassword', "`"$CertPassword`"")
    if ($Uninstall) { $argList += '-Uninstall' }
    Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $argList
    return
}

$pfx = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($CertFile, $CertPassword)
# Public part only - trust stores never need the private key.
$pub = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(, $pfx.RawData)

Write-Host "Certificate: $($pub.Subject)  (thumbprint $($pub.Thumbprint))"

foreach ($name in 'Root', 'TrustedPeople') {
    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store($name, 'LocalMachine')
    $store.Open('ReadWrite')
    try {
        if ($Uninstall) {
            $existing = $store.Certificates | Where-Object { $_.Thumbprint -eq $pub.Thumbprint }
            if ($existing) { $store.Remove($pub); Write-Host "Removed from LocalMachine\$name" -ForegroundColor Green }
            else { Write-Host "Not present in LocalMachine\$name" -ForegroundColor DarkGray }
        }
        else {
            $store.Add($pub)
            Write-Host "Trusted in LocalMachine\$name" -ForegroundColor Green
        }
    }
    finally { $store.Close() }
}

if (-not $Uninstall) {
    Write-Host "`nDone. You can now double-click the .msix to install it." -ForegroundColor Cyan
}

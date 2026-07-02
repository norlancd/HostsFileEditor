<#
.SYNOPSIS
    Shared helper for the installer scripts: runs `dotnet publish` with a timeout and auto-retry.
.DESCRIPTION
    On this machine, `dotnet publish`'s SignAndPackage step could hang right after signtool signs the
    exe - makeappx.exe (and occasionally signtool.exe) would start but sit at ~0% CPU indefinitely instead
    of packaging. Root cause: when `dotnet publish` was launched via Start-Job, the job's host process has
    no console attached at all. makeappx.exe launched several process-generations deep under that
    (dotnet.exe -> MSBuild.exe -> cmd.exe -> makeappx.exe) would then stall, seemingly on a console-related
    call. Running the exact same publish from a process that has a real console attached (this script's
    own process) completed in ~5 seconds every time instead of hanging for minutes. So this launches
    `dotnet publish` as a direct child of this script's own process (raw Process/ProcessStartInfo, not
    Start-Job and not the Start-Process cmdlet - the latter's -PassThru object left .ExitCode unreadable
    here even after the process had exited). The timeout/kill/retry logic is kept as a safety net in case
    of a genuine hang from some other cause.
#>

function Enter-SingleInstanceLock {
    <#
    .SYNOPSIS
        Acquires a named mutex so two concurrent runs of the same installer script don't collide.
    .DESCRIPTION
        Two overlapping runs both invoking makeappx/signtool against the same publish output path
        deadlock each other (two makeappx.exe processes fighting over the same .msix file handle).
        Call this once near the top of a script and dispose the returned mutex in a finally block.
    #>
    param([Parameter(Mandatory)][string]$Name)

    $mutex = New-Object System.Threading.Mutex($false, "Global\$Name")
    if (-not $mutex.WaitOne(0)) {
        $mutex.Dispose()
        throw "Another run of this script is already in progress (lock '$Name'). Wait for it to finish - running two at once makes them collide over the same output files and both get stuck."
    }
    return $mutex
}

function Invoke-DotnetPublishWithTimeout {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string[]]$PublishArgs,
        [string]$WorkingDirectory = (Get-Location).Path,
        [int]$TimeoutSeconds = 120,
        [int]$MaxAttempts = 2
    )

    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        if ($attempt -gt 1) {
            Write-Host "Retry $attempt of $MaxAttempts..." -ForegroundColor Yellow
        }

        $startTime = Get-Date
        $deadline = $startTime.AddSeconds($TimeoutSeconds)

        # MSBUILDDISABLENODEREUSE + -nodeReuse:false: without both, `dotnet publish` can leave a
        # persistent MSBuild worker node running after this process ends, which can itself still be
        # mid-spawn of cmd.exe/makeappx.exe and outlive a kill of the top-level process.
        $fullArgs = @('publish') + $PublishArgs + @('-nodeReuse:false')
        $argLine = ($fullArgs | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' '

        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = 'dotnet'
        $psi.Arguments = $argLine
        $psi.WorkingDirectory = $WorkingDirectory
        $psi.UseShellExecute = $false
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.EnvironmentVariables['MSBUILDDISABLENODEREUSE'] = '1'

        $proc = New-Object System.Diagnostics.Process
        $proc.StartInfo = $psi
        $proc.EnableRaisingEvents = $true

        $outHandler = { if ($null -ne $EventArgs.Data) { Write-Host $EventArgs.Data } }
        $errHandler = { if ($null -ne $EventArgs.Data) { Write-Host $EventArgs.Data } }
        $outEvent = Register-ObjectEvent -InputObject $proc -EventName OutputDataReceived -Action $outHandler
        $errEvent = Register-ObjectEvent -InputObject $proc -EventName ErrorDataReceived -Action $errHandler

        try {
            $proc.Start() | Out-Null
            $proc.BeginOutputReadLine()
            $proc.BeginErrorReadLine()

            $timedOut = $false
            while (-not $proc.WaitForExit(400)) {
                if ((Get-Date) -gt $deadline) { $timedOut = $true; break }
            }

            if (-not $timedOut) {
                $proc.WaitForExit()
                # Let the last buffered OutputDataReceived/ErrorDataReceived events flush through.
                Start-Sleep -Milliseconds 200
                $exitCode = $proc.ExitCode

                if ($exitCode -eq 0) { return }
                if ($attempt -eq $MaxAttempts) { throw "dotnet publish failed with exit code $exitCode" }
                continue
            }

            Write-Warning "dotnet publish did not finish within $TimeoutSeconds s. Killing the process tree (PID $($proc.Id)) and retrying..."
            & taskkill.exe /T /F /PID $proc.Id 2>&1 | Out-Null
            Start-Sleep -Seconds 1

            # Defensive fallback sweep in case taskkill /T missed a grandchild that had already detached
            # (e.g. an MSBuild node-reuse worker spawned just before the kill).
            Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
                Where-Object {
                    ($_.Name -in 'makeappx.exe', 'signtool.exe', 'MSBuild.exe', 'dotnet.exe', 'VBCSCompiler.exe') -or
                    ($_.Name -eq 'cmd.exe' -and $_.CommandLine -match 'MSBuildTemp')
                } |
                Where-Object {
                    if ([string]::IsNullOrEmpty($_.CreationDate)) { return $false }
                    try { [Management.ManagementDateTimeConverter]::ToDateTime($_.CreationDate) -ge $startTime }
                    catch { $false }
                } |
                ForEach-Object {
                    Write-Host "  Killing stuck process $($_.Name) (PID $($_.ProcessId))" -ForegroundColor Yellow
                    Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
                }

            if ($attempt -eq $MaxAttempts) {
                throw "dotnet publish kept hanging after $MaxAttempts attempts."
            }
        }
        finally {
            Unregister-Event -SourceIdentifier $outEvent.Name -ErrorAction SilentlyContinue
            Unregister-Event -SourceIdentifier $errEvent.Name -ErrorAction SilentlyContinue
            Remove-Job -Job $outEvent, $errEvent -Force -ErrorAction SilentlyContinue
            $proc.Dispose()
        }
    }
}

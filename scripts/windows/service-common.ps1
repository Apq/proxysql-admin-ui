<#
.SYNOPSIS
    Shared WinSW service lifecycle helpers for ProxySQL Admin UI.
#>

$ServiceName = "ProxySQL-Admin-UI"
$ServiceWaitTimeoutSeconds = 90
$ServicePollMilliseconds = 500
$ServiceHealthUrl = "http://127.0.0.1:8000/health"
$WinSwVersion = "2.12.0"
$WinSwSha256 = "05B82D46AD331CC16BDC00DE5C6332C1EF818DF8CEEFCD49C726553209B3A0DA"

function Assert-Administrator {
    $isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $isAdmin) {
        throw "Please run this script as Administrator."
    }
}

function Get-WinSwExecutable {
    param(
        [Parameter(Mandatory)]
        [string]$ScriptDir
    )

    $winswExe = Join-Path $ScriptDir "ProxySQL-Admin-UI.exe"
    if (-not (Test-Path $winswExe)) {
        $downloadUrl = "https://github.com/winsw/winsw/releases/download/v$WinSwVersion/WinSW-x64.exe"
        Write-Host "Downloading WinSW $WinSwVersion..."
        Invoke-WebRequest -UseBasicParsing -Uri $downloadUrl -OutFile $winswExe
    }

    $actualHash = (Get-FileHash -LiteralPath $winswExe -Algorithm SHA256).Hash
    if ($actualHash -ne $WinSwSha256) {
        throw "WinSW checksum mismatch. Expected $WinSwSha256, got $actualHash."
    }

    return $winswExe
}

function Get-ManagedService {
    return Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
}

function Wait-ManagedServiceState {
    param(
        [Parameter(Mandatory)]
        [ValidateSet("Stopped", "Running", "Absent")]
        [string]$State,
        [int]$TimeoutSeconds = $ServiceWaitTimeoutSeconds
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $service = Get-ManagedService
        if ($State -eq "Absent") {
            if ($null -eq $service) { return }
        } elseif ($null -ne $service -and $service.Status.ToString() -eq $State) {
            return
        }
        Start-Sleep -Milliseconds $ServicePollMilliseconds
    } while ([DateTime]::UtcNow -lt $deadline)

    $service = Get-ManagedService
    $actual = if ($null -eq $service) { "Absent" } else { $service.Status.ToString() }
    throw "Timed out waiting for service '$ServiceName' to reach '$State'; actual state: '$actual'."
}

function Wait-ManagedServiceReady {
    param(
        [string]$HealthUrl = $ServiceHealthUrl,
        [int]$TimeoutSeconds = $ServiceWaitTimeoutSeconds
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $service = Get-ManagedService
        if ($null -eq $service) {
            throw "Service '$ServiceName' disappeared before becoming ready."
        }
        if ($service.Status.ToString() -in @("Stopped", "StopPending")) {
            throw "Service '$ServiceName' stopped before becoming ready."
        }

        try {
            $response = Invoke-WebRequest -UseBasicParsing -Uri $HealthUrl -TimeoutSec 5
            if ([int]$response.StatusCode -eq 200) { return }
        } catch {
            # The service can report Running before Kestrel binds the port.
        }
        Start-Sleep -Milliseconds $ServicePollMilliseconds
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "Timed out waiting for service health endpoint: $HealthUrl"
}

function Invoke-WinSw {
    param(
        [Parameter(Mandatory)] [string]$WinswExe,
        [Parameter(Mandatory)] [string[]]$Arguments,
        [Parameter(Mandatory)] [string]$ErrorMessage
    )

    & $WinswExe @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$ErrorMessage (exit code: $LASTEXITCODE)"
    }
}

function Stop-ManagedService {
    param([Parameter(Mandatory)] [string]$WinswExe)

    $service = Get-ManagedService
    if ($null -eq $service) { return }
    if ($service.Status.ToString() -ne "Stopped") {
        Write-Host "Stopping service..."
        Invoke-WinSw -WinswExe $WinswExe -Arguments @("stopwait") -ErrorMessage "Service stop failed"
    }
    Wait-ManagedServiceState -State "Stopped"
}

function Start-ManagedService {
    param([Parameter(Mandatory)] [string]$WinswExe)

    if ($null -eq (Get-ManagedService)) {
        throw "Service '$ServiceName' is not installed."
    }
    if ((Get-ManagedService).Status.ToString() -ne "Running") {
        Invoke-WinSw -WinswExe $WinswExe -Arguments @("start") -ErrorMessage "Service start failed"
    }
    Wait-ManagedServiceState -State "Running"
    Wait-ManagedServiceReady
}

function Install-ManagedService {
    param([Parameter(Mandatory)] [string]$WinswExe)

    Invoke-WinSw -WinswExe $WinswExe -Arguments @("install") -ErrorMessage "Service install failed"
    Wait-ManagedServiceState -State "Stopped"
}

function Uninstall-ManagedService {
    param([Parameter(Mandatory)] [string]$WinswExe)

    if ($null -eq (Get-ManagedService)) { return }
    Stop-ManagedService -WinswExe $WinswExe
    Invoke-WinSw -WinswExe $WinswExe -Arguments @("uninstall") -ErrorMessage "Service uninstall failed"
    Wait-ManagedServiceState -State "Absent"
}

function Restart-ManagedService {
    param([Parameter(Mandatory)] [string]$WinswExe)

    Stop-ManagedService -WinswExe $WinswExe
    Start-ManagedService -WinswExe $WinswExe
}

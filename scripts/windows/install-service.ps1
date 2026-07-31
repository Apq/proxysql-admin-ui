param(
    [switch]$Uninstall,
    [switch]$NoStart
)

$ErrorActionPreference = "Stop"
$ScriptDir = $PSScriptRoot
$WinswXml = Join-Path $ScriptDir "ProxySQL-Admin-UI.xml"
. (Join-Path $ScriptDir "service-common.ps1")

Assert-Administrator
$WinswExe = Get-WinSwExecutable -ScriptDir $ScriptDir

if (-not (Test-Path $WinswXml)) {
    throw "Local service configuration not found: $WinswXml. Run deploy-inplace.ps1 first."
}

if ($Uninstall) {
    Write-Host "Stopping and uninstalling ProxySQL Admin UI service..."
    Uninstall-ManagedService -WinswExe $WinswExe
    Write-Host "Service uninstalled. Publish and data directories were not removed."
    return
}

if ($null -ne (Get-ManagedService)) {
    Write-Host "Reinstalling existing ProxySQL Admin UI service..."
    Uninstall-ManagedService -WinswExe $WinswExe
}

Install-ManagedService -WinswExe $WinswExe
Write-Host "Service installed."

if (-not $NoStart) {
    Start-ManagedService -WinswExe $WinswExe
    Write-Host "Service started: http://localhost:8001"
}

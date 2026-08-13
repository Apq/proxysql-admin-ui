param(
    [string]$ProxySqlConnectionString = "",
    [string]$GaleraUsername = "",
    [string]$GaleraPassword = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($GaleraUsername) -ne [string]::IsNullOrWhiteSpace($GaleraPassword)) {
    throw "-GaleraUsername and -GaleraPassword must be provided together."
}

$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    $arguments = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $PSCommandPath)
    if ($ProxySqlConnectionString) { $arguments += "-ProxySqlConnectionString"; $arguments += $ProxySqlConnectionString }
    if ($GaleraUsername) { $arguments += "-GaleraUsername"; $arguments += $GaleraUsername }
    if ($GaleraPassword) { $arguments += "-GaleraPassword"; $arguments += $GaleraPassword }
    $elevated = Start-Process -FilePath "pwsh.exe" -ArgumentList $arguments -Verb RunAs -Wait -PassThru
    exit $elevated.ExitCode
}

$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$ProjectFile = Join-Path $ProjectRoot "ProxysqlAdminUi.Web\ProxysqlAdminUi.Web.csproj"
$PublishDir = Join-Path $ProjectRoot "publish"
$StagingDir = Join-Path $ProjectRoot "publish.staging"
$DataDir = Join-Path $ProjectRoot "data"
$LogsDir = Join-Path $ProjectRoot "logs"
$WinswXml = Join-Path $PSScriptRoot "ProxySQL-Admin-UI.xml"
$WinswXmlExample = Join-Path $PSScriptRoot "ProxySQL-Admin-UI.xml.example"
. (Join-Path $PSScriptRoot "service-common.ps1")

function Convert-ToXmlAttributeValue {
    param([Parameter(Mandatory)] [string]$Value)
    return [System.Security.SecurityElement]::Escape($Value)
}

function Initialize-ServiceConfiguration {
    if (Test-Path $WinswXml) {
        Write-Host "Using existing local service configuration: $WinswXml"
        return
    }

    if (-not (Test-Path $WinswXmlExample)) {
        throw "Service configuration template not found: $WinswXmlExample"
    }
    if ([string]::IsNullOrWhiteSpace($ProxySqlConnectionString)) {
        throw "First deployment requires -ProxySqlConnectionString. No default ProxySQL credentials will be used."
    }

    $xml = Get-Content -LiteralPath $WinswXmlExample -Raw
    $xml = $xml.Replace('$(PROXYSQL_CONNECTION_STRING)', (Convert-ToXmlAttributeValue $ProxySqlConnectionString))
    if ([string]::IsNullOrWhiteSpace($GaleraUsername) -or [string]::IsNullOrWhiteSpace($GaleraPassword)) {
        $xml = $xml -replace '(?m)^\s*<env name="PAI_GALERA_USERNAME"[^>]*?/>(\r?\n)?', ''
        $xml = $xml -replace '(?m)^\s*<env name="PAI_GALERA_PASSWORD"[^>]*?/>(\r?\n)?', ''
    } else {
        $xml = $xml.Replace('$(GALERA_USERNAME)', (Convert-ToXmlAttributeValue $GaleraUsername))
        $xml = $xml.Replace('$(GALERA_PASSWORD)', (Convert-ToXmlAttributeValue $GaleraPassword))
    }
    [IO.File]::WriteAllText($WinswXml, $xml, [Text.UTF8Encoding]::new($false))

    Write-Host "Created local service configuration: $WinswXml"
}

function Invoke-Publish {
    if (Test-Path $StagingDir) {
        Remove-Item -LiteralPath $StagingDir -Recurse -Force
    }

    $arguments = @(
        "publish", $ProjectFile,
        "-c", "Release",
        "-r", "win-x64",
        "--self-contained", "false",
        "-o", $StagingDir,
        "/p:UseAppHost=true"
    )
    Write-Host "Publishing ProxySQL Admin UI to staging..."
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed (exit code: $LASTEXITCODE)"
    }
    if (-not (Test-Path (Join-Path $StagingDir "ProxysqlAdminUi.Web.exe"))) {
        throw "Publish output is incomplete: ProxysqlAdminUi.Web.exe is missing."
    }
}

function Publish-Staging {
    if (Test-Path $PublishDir) {
        Remove-Item -LiteralPath $PublishDir -Recurse -Force
    }
    Move-Item -LiteralPath $StagingDir -Destination $PublishDir
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK was not found in PATH. Install the .NET 8 SDK first."
}

Write-Host "Project root: $ProjectRoot"
Initialize-ServiceConfiguration
$WinswExe = Get-WinSwExecutable -ScriptDir $PSScriptRoot
New-Item -ItemType Directory -Force -Path $DataDir, $LogsDir | Out-Null

# Build before the stop window. The live service keeps using the current publish directory.
Invoke-Publish

$serviceWasInstalled = $null -ne (Get-ManagedService)
try {
    if ($serviceWasInstalled) {
        Stop-ManagedService -WinswExe $WinswExe
    }
    Publish-Staging
} catch {
    if (Test-Path $StagingDir) {
        Remove-Item -LiteralPath $StagingDir -Recurse -Force -ErrorAction SilentlyContinue
    }
    throw
}

if (-not $serviceWasInstalled) {
    Install-ManagedService -WinswExe $WinswExe
}
Start-ManagedService -WinswExe $WinswExe

Write-Host "Deployment completed."
Write-Host "URL: http://localhost:8001"
Write-Host "Persistent identity data: $DataDir"

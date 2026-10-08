param(
	[string]$ServerInstance = "RKNAIDUSADHU\MSSQLSERVER01",
	[string]$TargetDb = "LMSDB",
	[string]$IdentityDb = "LMSIdentityDb",
	[string]$MasterDb = "LMSMasterDb",
	[string]$UiDb = "LMSWebUI",
	[switch]$UseSqlAuthentication,
	[string]$SqlUser = "",
	[string]$SqlPassword = ""
)

$ErrorActionPreference = "Stop"

$scriptPath = Join-Path $PSScriptRoot "Consolidate-To-LMSDB.sql"
if (-not (Test-Path $scriptPath)) {
	throw "Consolidation SQL script not found: $scriptPath"
}

$sqlCmd = Get-Command sqlcmd -ErrorAction SilentlyContinue
if (-not $sqlCmd) {
	throw "sqlcmd is required but was not found in PATH. Install SQL Server command-line tools."
}

Write-Host "Running consolidation into $TargetDb on $ServerInstance ..."

$commonArgs = @(
	"-S", $ServerInstance,
	"-d", "master",
	"-b",
	"-i", $scriptPath,
	"-v",
	"TargetDb=$TargetDb",
	"IdentityDb=$IdentityDb",
	"MasterDb=$MasterDb",
	"UiDb=$UiDb"
)

if ($UseSqlAuthentication) {
	if ([string]::IsNullOrWhiteSpace($SqlUser) -or [string]::IsNullOrWhiteSpace($SqlPassword)) {
		throw "SqlUser and SqlPassword are required when -UseSqlAuthentication is set."
	}

	$authArgs = @("-U", $SqlUser, "-P", $SqlPassword)
	& sqlcmd @commonArgs @authArgs
}
else {
	$trustedArgs = @("-E")
	& sqlcmd @commonArgs @trustedArgs
}

Write-Host "Consolidation script completed."

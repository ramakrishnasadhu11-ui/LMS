param(
	[switch]$UseUserSecrets,
	[string]$ProjectPath = "..\LMS.Identity.WebApi.csproj",
	[string]$Host = $null,
	[int]$Port = 0,
	[string]$Username = $null,
	[string]$Password = $null,
	[string]$From = $null,
	[string]$To = $null,
	[string]$Subject = "SMTP Test Message",
	[string]$Body = "This is a test message sent by smtp-send-wrapper.ps1",
	[switch]$VerboseMode
)

function Write-Info($msg) { if ($VerboseMode) { Write-Host $msg -ForegroundColor Cyan } }

# Helper to read key/value from dotnet user-secrets list output
function Get-UserSecretsMap($project) {
	$map = @{}
	try {
		$output = dotnet user-secrets list --project $project 2>$null
		if ($LASTEXITCODE -ne 0) {
			Write-Host "dotnet user-secrets failed or no user-secrets configured for project: $project" -ForegroundColor Yellow
			return $map
		}
		foreach ($line in $output) {
			if (-not $line) { continue }
			$m = [regex]::Match($line, '^(?<key>[^=]+?)\s*=\s*(?<value>.*)$')
			if ($m.Success) {
				$key = $m.Groups['key'].Value.Trim()
				$val = $m.Groups['value'].Value.Trim()
				$map[$key] = $val
			}
		}
	}
	catch {
		Write-Host "Failed to run dotnet user-secrets: $($_.Exception.Message)" -ForegroundColor Yellow
	}
	return $map
}

# Read environment variables first
$envHost = $env:MAIL_HOST
$envPort = $env:MAIL_PORT
$envUser = $env:MAIL_USERNAME
$envPass = $env:MAIL_PASSWORD
$envFrom = $env:MAIL_FROM
$envTo = $env:MAIL_TO

if ($UseUserSecrets) {
	Write-Info "Reading user-secrets from project path: $ProjectPath"
	$secrets = Get-UserSecretsMap $ProjectPath
	# Keys expected like: MailConfiguration:Username
	if ($secrets.ContainsKey('MailConfiguration:Username')) { $envUser = $secrets['MailConfiguration:Username'] }
	if ($secrets.ContainsKey('MailConfiguration:Password')) { $envPass = $secrets['MailConfiguration:Password'] }
	if ($secrets.ContainsKey('MailConfiguration:SmtpServer')) { $envHost = $secrets['MailConfiguration:SmtpServer'] }
	if ($secrets.ContainsKey('MailConfiguration:Port')) { $envPort = $secrets['MailConfiguration:Port'] }
	if ($secrets.ContainsKey('MailConfiguration:MailFrom')) { $envFrom = $secrets['MailConfiguration:MailFrom'] }
}

# Merge precedence: explicit params > env/user-secrets
$host = if ($Host) { $Host } elseif ($envHost) { $envHost } else { 'smtp.gmail.com' }
$port = if ($Port -gt 0) { $Port } elseif ($envPort) { [int]$envPort } else { 465 }
$username = if ($Username) { $Username } elseif ($envUser) { $envUser } else { '' }
$password = if ($Password) { $Password } elseif ($envPass) { $envPass } else { '' }
$from = if ($From) { $From } elseif ($envFrom) { $envFrom } else { $username }
$to = if ($To) { $To } elseif ($envTo) { $envTo } else { '' }

if (-not $to) {
	Write-Host "Recipient (-To) is required. Provide -To or set MAIL_TO environment variable or MailConfiguration:Receiver user-secret." -ForegroundColor Yellow
	exit 1
}

# Build path to direct script
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$directScript = Join-Path $scriptDir "smtp-test-direct.ps1"
if (-not (Test-Path $directScript)) {
	Write-Host "Direct SMTP script not found: $directScript" -ForegroundColor Red
	exit 2
}

Write-Host "Using SMTP host=$host port=$port user=$([string]::IsNullOrEmpty($username) ? '<none>' : $username) from=$from to=$to"

# Call direct script
$invokeArgs = @(
	'-Host', $host,
	'-Port', $port,
	'-SendTest',
	'-To', $to,
	'-From', $from,
	'-Subject', $Subject,
	'-Body', $Body
)
if ($username) { $invokeArgs += @('-Username', $username) }
if ($password) { $invokeArgs += @('-Password', $password) }

& $directScript @invokeArgs

exit $LASTEXITCODE

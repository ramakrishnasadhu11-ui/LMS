param(
	[string]$BaseUrl = "http://localhost:5000",
	[int]$TimeoutSeconds = 10
)

$uri = "$BaseUrl/Diagnostics/SmtpTest"
Write-Host "Calling $uri ..."

try {
	$response = Invoke-RestMethod -Uri $uri -Method Get -TimeoutSec $TimeoutSeconds
	if ($response -and $response.success -eq $true) {
		Write-Host "SMTP Test succeeded:`n$response.message" -ForegroundColor Green
		exit 0
	}
	else {
		Write-Host "SMTP Test failed: $($response.message)" -ForegroundColor Red
		exit 2
	}
}
catch {
	Write-Host "HTTP request failed: $($_.Exception.Message)" -ForegroundColor Red
	exit 1
}
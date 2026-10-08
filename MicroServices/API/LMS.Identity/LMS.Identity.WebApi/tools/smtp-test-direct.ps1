param(
	[string]$Host = "smtp.gmail.com",
	[int]$Port = 465,
	[string]$Username = "",
	[string]$Password = "",
	[int]$TimeoutSeconds = 10,
	[switch]$SendTest,
	[string]$To = "",
	[string]$From = "",
	[string]$Subject = "SMTP Test Message",
	[string]$Body = "This is a test message sent by smtp-test-direct.ps1"
)

function Write-ErrAndExit($msg, $code) {
	Write-Host $msg -ForegroundColor Red
	exit $code
}

[void][System.Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$endCodeSuccess = 0
$endCodeNetFail = 1
$endCodeAuthFail = 2
$endCodeOtherFail = 3

try {
	Write-Host "Connecting to $Host:$Port (timeout ${TimeoutSeconds}s) ..."
	$client = New-Object System.Net.Sockets.TcpClient
	$async = $client.BeginConnect($Host, $Port, $null, $null)
	if (-not $async.AsyncWaitHandle.WaitOne($TimeoutSeconds * 1000)) {
		throw "Connection timed out"
	}
	$client.EndConnect($async)
	$client.ReceiveTimeout = $TimeoutSeconds * 1000
	$client.SendTimeout = $TimeoutSeconds * 1000

	$netStream = $client.GetStream()

	$useSsl = $false
	if ($Port -eq 465) { $useSsl = $true }

	if ($useSsl) {
		$ssl = New-Object System.Net.Security.SslStream($netStream, $false, ({ $true }))
		$ssl.AuthenticateAsClient($Host)
		$reader = New-Object System.IO.StreamReader($ssl)
		$writer = New-Object System.IO.StreamWriter($ssl)
	}
	else {
		$reader = New-Object System.IO.StreamReader($netStream)
		$writer = New-Object System.IO.StreamWriter($netStream)
	}
	$writer.NewLine = "`r`n"
	$writer.AutoFlush = $true

	function Read-Response {
		$lines = @()
		while ($true) {
			$line = $null
			try { $line = $reader.ReadLine() } catch { }
			if ($null -eq $line) { break }
			$lines += $line
			if ($line.Length -ge 4 -and $line[3] -eq ' ') { break }
		}
		return ,$lines
	}

	# Read initial banner
	$banner = Read-Response
	if ($banner.Count -eq 0) { throw "No banner received from server." }
	Write-Host "<-" ($banner -join "`n<- ")

	# Send EHLO
	$ehlo = "EHLO localhost"
	Write-Host "-> $ehlo"
	$writer.WriteLine($ehlo)
	$resp = Read-Response
	Write-Host "<-" ($resp -join "`n<- ")

	# If not using implicit SSL, try STARTTLS if supported
	if (-not $useSsl) {
		$respText = ($resp -join " `n").ToUpper()
		if ($respText -match "STARTTLS") {
			Write-Host "-> STARTTLS"
			$writer.WriteLine("STARTTLS")
			$resp2 = Read-Response
			Write-Host "<-" ($resp2 -join "`n<- ")
			if ($resp2 -and $resp2[0].StartsWith("220")) {
				# upgrade to SSL
				$ssl = New-Object System.Net.Security.SslStream($netStream, $false, ({ $true }))
				$ssl.AuthenticateAsClient($Host)
				$reader = New-Object System.IO.StreamReader($ssl)
				$writer = New-Object System.IO.StreamWriter($ssl)
				$writer.NewLine = "`r`n"
				$writer.AutoFlush = $true

				# EHLO again
				Write-Host "-> $ehlo"
				$writer.WriteLine($ehlo)
				$resp3 = Read-Response
				Write-Host "<-" ($resp3 -join "`n<- ")
			}
			else {
				throw "STARTTLS refused by server"
			}
		}
		else {
			Write-Host "Server does not advertise STARTTLS. Proceeding without TLS (not recommended)." -ForegroundColor Yellow
		}
	}

	# If credentials provided, attempt AUTH LOGIN
	if ($Username -and $Password) {
		Write-Host "-> AUTH LOGIN"
		$writer.WriteLine("AUTH LOGIN")
		$respAuth = Read-Response
		Write-Host "<-" ($respAuth -join "`n<- ")
		if (-not ($respAuth -and $respAuth[0].StartsWith("334"))) {
			throw "Server did not accept AUTH LOGIN request"
		}

		$bUser = [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($Username))
		Write-Host "-> (username base64) $bUser"
		$writer.WriteLine($bUser)
		$respUser = Read-Response
		Write-Host "<-" ($respUser -join "`n<- ")
		if (-not ($respUser -and $respUser[0].StartsWith("334"))) {
			throw "Server did not request password after username"
		}

		$bPass = [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($Password))
		Write-Host "-> (password base64) <hidden>"
		$writer.WriteLine($bPass)
		$respPass = Read-Response
		Write-Host "<-" ($respPass -join "`n<- ")
		if ($respPass -and $respPass[0].StartsWith("235")) {
			Write-Host "SMTP authentication succeeded" -ForegroundColor Green
			# Optionally send a test message
			if ($SendTest) {
				if (-not $From) { $From = $Username }
				if (-not $To) { Write-ErrAndExit "Recipient (-To) is required when -SendTest is specified." $endCodeOtherFail }

				Write-Host "-> MAIL FROM:<$From>"
				$writer.WriteLine("MAIL FROM:<$From>")
				$r1 = Read-Response
				Write-Host "<-" ($r1 -join "`n<- ")
				if (-not ($r1 -and $r1[0].StartsWith("250"))) { Write-ErrAndExit "MAIL FROM rejected: $($r1 -join ' | ')" $endCodeOtherFail }

				Write-Host "-> RCPT TO:<$To>"
				$writer.WriteLine("RCPT TO:<$To>")
				$r2 = Read-Response
				Write-Host "<-" ($r2 -join "`n<- ")
				if (-not ($r2 -and ($r2[0].StartsWith("250") -or $r2[0].StartsWith("251")))) { Write-ErrAndExit "RCPT TO rejected: $($r2 -join ' | ')" $endCodeOtherFail }

				Write-Host "-> DATA"
				$writer.WriteLine("DATA")
				$r3 = Read-Response
				Write-Host "<-" ($r3 -join "`n<- ")
				if (-not ($r3 -and $r3[0].StartsWith("354"))) { Write-ErrAndExit "DATA command rejected: $($r3 -join ' | ')" $endCodeOtherFail }

				# Send headers and body
				$writer.WriteLine("Date: $(Get-Date -Format 'r')")
				$writer.WriteLine("From: $From")
				$writer.WriteLine("To: $To")
				$writer.WriteLine("Subject: $Subject")
				$writer.WriteLine("Content-Type: text/plain; charset=utf-8")
				$writer.WriteLine("")
				$writer.WriteLine($Body)
				$writer.WriteLine(".")
				$r4 = Read-Response
				Write-Host "<-" ($r4 -join "`n<- ")
				if (-not ($r4 -and $r4[0].StartsWith("250"))) { Write-ErrAndExit "Message not accepted: $($r4 -join ' | ')" $endCodeOtherFail }

				Write-Host "Message sent successfully" -ForegroundColor Green
			}

			# QUIT
			$writer.WriteLine("QUIT")
			Read-Response | Out-Null
			$client.Close()
			exit $endCodeSuccess
		}
		else {
			Write-ErrAndExit "SMTP authentication failed: $($respPass -join ' | ')" $endCodeAuthFail
		}
	}
	else {
		Write-Host "No credentials provided. Connection and (if applicable) TLS upgrade succeeded." -ForegroundColor Green
		$writer.WriteLine("QUIT")
		Read-Response | Out-Null
		$client.Close()
		exit $endCodeSuccess
	}
}
catch [System.Net.Sockets.SocketException] {
	Write-ErrAndExit "Network error connecting to $Host:$Port - $($_.Exception.Message)" $endCodeNetFail
}
catch {
	Write-ErrAndExit "Error: $($_.Exception.Message)" $endCodeOtherFail
}

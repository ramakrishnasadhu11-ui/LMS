$ErrorActionPreference='Stop'

$base = 'https://localhost:56082'

function Invoke-ApiJson {
	param(
		[string]$Method,
		[string]$Url,
		[string]$JsonBody
	)

	$tmp = [System.IO.Path]::GetTempFileName()
	try {
		if ([string]::IsNullOrWhiteSpace($JsonBody)) {
			$code = & curl.exe -k -sS -o $tmp -w "%{http_code}" -X $Method $Url
		} else {
			$bodyFile = [System.IO.Path]::GetTempFileName()
			[System.IO.File]::WriteAllText($bodyFile, $JsonBody, (New-Object System.Text.UTF8Encoding($false)))
			try {
				$code = & curl.exe -k -sS -o $tmp -w "%{http_code}" -X $Method $Url -H "Content-Type: application/json" --data-binary "@$bodyFile"
			}
			finally {
				Remove-Item $bodyFile -ErrorAction SilentlyContinue
			}
		}

		$raw = Get-Content $tmp -Raw
		$obj = $null
		if (-not [string]::IsNullOrWhiteSpace($raw)) {
			try { $obj = $raw | ConvertFrom-Json } catch { $obj = $raw }
		}

		[pscustomobject]@{ HttpStatus = [int]$code; Body = $obj; Raw = $raw }
	}
	finally { Remove-Item $tmp -ErrorAction SilentlyContinue }
}

function New-TestCustomer {
	param([string]$Tenant,[string]$Store,[string]$Suffix)
	$name = "Adv-Concurrency-$Suffix"
	$payload = @{ CustomerName=$name; Address='Runtime Test Address'; PhoneNumber='9876543210'; Email=''; StoreCode=$Store; TenantName=$Tenant } | ConvertTo-Json -Compress
	$resp = Invoke-ApiJson -Method 'POST' -Url "$base/Customer/InsertCustomer" -JsonBody $payload

	$custCode = $null
	if ($resp.Body -and $resp.Body.resultSet) { $custCode = $resp.Body.resultSet.custCode }
	if (-not $custCode -and $resp.Body -and $resp.Body.custCode) { $custCode = $resp.Body.custCode }
	if (-not $custCode -and $resp.Body -and $resp.Body.CustCode) { $custCode = $resp.Body.CustCode }

	[pscustomobject]@{ Name=$name; CustCode=$custCode; Response=$resp }
}

function Save-Advance {
	param([string]$Tenant,[string]$Store,[string]$CustCode,[decimal]$Amount,[string]$Type,[string]$Notes)
	$payload = @{ TenantName=$Tenant; StoreCode=$Store; CustCode=$CustCode; AdvanceAmount=$Amount; TransactionType=$Type; Notes=$Notes } | ConvertTo-Json -Compress
	Invoke-ApiJson -Method 'POST' -Url "$base/Customer/SaveCustomerAdvance" -JsonBody $payload
}

function Get-Balance {
	param([string]$Tenant,[string]$Store,[string]$CustCode)
	$url = "$base/Customer/GetCustomerAdvances?tenantName=$([uri]::EscapeDataString($Tenant))&storeCode=$([uri]::EscapeDataString($Store))&custCode=$([uri]::EscapeDataString($CustCode))"
	$resp = Invoke-ApiJson -Method 'GET' -Url $url -JsonBody ''
	$balance = $null
	if ($resp.Body -and $resp.Body.resultSet) { $balance = [decimal]$resp.Body.resultSet.balance }
	elseif ($resp.Body -and $resp.Body.balance -ne $null) { $balance = [decimal]$resp.Body.balance }
	elseif ($resp.Body -and $resp.Body.Balance -ne $null) { $balance = [decimal]$resp.Body.Balance }
	[pscustomobject]@{ Response=$resp; Balance=$balance }
}

$tenant = "tenant-concurrency-runtime@example.com"
$store = "STORE-CONC-RUNTIME"
$runId = [DateTime]::UtcNow.ToString('yyyyMMddHHmmss') + '-' + ([Guid]::NewGuid().ToString('N').Substring(0,6))

$results = [ordered]@{}

# Scenario A: valid/exact/overdraft/invalid
$custA = New-TestCustomer -Tenant $tenant -Store $store -Suffix "A-$runId"
$results.CustomerA_Create = $custA.Response
if (-not $custA.CustCode) { throw ("Failed to create Customer A. Raw=" + ($custA.Response.Raw)) }

$results.CustomerA_Credit1000 = Save-Advance -Tenant $tenant -Store $store -CustCode $custA.CustCode -Amount 1000 -Type 'Credit' -Notes 'seed credit'
$results.CustomerA_Debit300 = Save-Advance -Tenant $tenant -Store $store -CustCode $custA.CustCode -Amount 300 -Type 'Debit' -Notes 'valid debit'
$results.CustomerA_BalanceAfterDebit300 = Get-Balance -Tenant $tenant -Store $store -CustCode $custA.CustCode
$results.CustomerA_Debit700_ExactRemaining = Save-Advance -Tenant $tenant -Store $store -CustCode $custA.CustCode -Amount 700 -Type 'Debit' -Notes 'exact debit'
$results.CustomerA_BalanceAfterExact = Get-Balance -Tenant $tenant -Store $store -CustCode $custA.CustCode
$results.CustomerA_Debit1_Overdraft = Save-Advance -Tenant $tenant -Store $store -CustCode $custA.CustCode -Amount 1 -Type 'Debit' -Notes 'overdraft expected fail'
$results.CustomerA_ZeroAmount = Save-Advance -Tenant $tenant -Store $store -CustCode $custA.CustCode -Amount 0 -Type 'Debit' -Notes 'zero amount expected fail'
$results.CustomerA_NegativeAmount = Save-Advance -Tenant $tenant -Store $store -CustCode $custA.CustCode -Amount -10 -Type 'Debit' -Notes 'negative amount expected fail'

# Scenario B: concurrency with initial 1000 and two debits of 700
$custB = New-TestCustomer -Tenant $tenant -Store $store -Suffix "B-$runId"
$results.CustomerB_Create = $custB.Response
if (-not $custB.CustCode) { throw ("Failed to create Customer B. Raw=" + ($custB.Response.Raw)) }

$results.CustomerB_Credit1000 = Save-Advance -Tenant $tenant -Store $store -CustCode $custB.CustCode -Amount 1000 -Type 'Credit' -Notes 'seed for concurrency'

$payload1 = @{ TenantName=$tenant; StoreCode=$store; CustCode=$custB.CustCode; AdvanceAmount=700; TransactionType='Debit'; Notes='concurrency debit 1' } | ConvertTo-Json -Compress
$payload2 = @{ TenantName=$tenant; StoreCode=$store; CustCode=$custB.CustCode; AdvanceAmount=700; TransactionType='Debit'; Notes='concurrency debit 2' } | ConvertTo-Json -Compress

$scriptBlock = {
	param($baseUrl, $payload)
	$tmp = [System.IO.Path]::GetTempFileName()
	try {
		$bodyFile = [System.IO.Path]::GetTempFileName()
		[System.IO.File]::WriteAllText($bodyFile, $payload, (New-Object System.Text.UTF8Encoding($false)))
		try {
			$code = & curl.exe -k -sS -o $tmp -w "%{http_code}" -X POST "$baseUrl/Customer/SaveCustomerAdvance" -H "Content-Type: application/json" --data-binary "@$bodyFile"
		}
		finally {
			Remove-Item $bodyFile -ErrorAction SilentlyContinue
		}
		$raw = Get-Content $tmp -Raw
		[pscustomobject]@{ HttpStatus=[int]$code; Raw=$raw }
	}
	finally { Remove-Item $tmp -ErrorAction SilentlyContinue }
}

$j1 = Start-Job -ScriptBlock $scriptBlock -ArgumentList $base, $payload1
$j2 = Start-Job -ScriptBlock $scriptBlock -ArgumentList $base, $payload2
Wait-Job -Job $j1, $j2 | Out-Null
$r1 = Receive-Job $j1
$r2 = Receive-Job $j2
Remove-Job $j1, $j2

$results.CustomerB_ConcurrentDebit1 = $r1
$results.CustomerB_ConcurrentDebit2 = $r2
$results.CustomerB_BalanceAfterConcurrent = Get-Balance -Tenant $tenant -Store $store -CustCode $custB.CustCode

# Scenario C: DB exception-style rollback check (oversized notes)
$custC = New-TestCustomer -Tenant $tenant -Store $store -Suffix "C-$runId"
$results.CustomerC_Create = $custC.Response
if ($custC.CustCode) {
	$results.CustomerC_Credit100 = Save-Advance -Tenant $tenant -Store $store -CustCode $custC.CustCode -Amount 100 -Type 'Credit' -Notes 'baseline'
	$before = Get-Balance -Tenant $tenant -Store $store -CustCode $custC.CustCode
	$longNotes = ('X' * 6000)
	$results.CustomerC_Credit10_LongNotes = Save-Advance -Tenant $tenant -Store $store -CustCode $custC.CustCode -Amount 10 -Type 'Credit' -Notes $longNotes
	$after = Get-Balance -Tenant $tenant -Store $store -CustCode $custC.CustCode
	$results.CustomerC_BalanceBeforeLongNotes = $before
	$results.CustomerC_BalanceAfterLongNotes = $after
}

$results | ConvertTo-Json -Depth 20

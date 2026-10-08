if ($PSVersionTable.PSEdition -ne "Core") {
    # MailKit/MimeKit 4.x ship net8.0 assemblies that Windows PowerShell 5.1 (.NET Framework) cannot load.
    $pwsh = (Get-Command pwsh -ErrorAction SilentlyContinue).Source
    if (-not $pwsh) { throw "PowerShell 7 (pwsh) is required to run this script." }
    & $pwsh -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath
    exit $LASTEXITCODE
}

$nuget = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE ".nuget\packages" }

Add-Type -Path (Join-Path $nuget "bouncycastle.cryptography\2.7.0\lib\net6.0\BouncyCastle.Cryptography.dll")
Add-Type -Path (Join-Path $nuget "mimekit\4.18.1\lib\net8.0\MimeKit.dll")
Add-Type -Path (Join-Path $nuget "mailkit\4.18.1\lib\net8.0\MailKit.dll")

$secrets = Join-Path $env:APPDATA "Microsoft\UserSecrets\e3274cfb-b997-4573-9307-2537b80ffeea\secrets.json"
$j = Get-Content $secrets -Raw | ConvertFrom-Json
$u = $j."MailConfiguration:Username"
$pw = $j."MailConfiguration:Password"

$m = New-Object MimeKit.MimeMessage
$m.From.Add((New-Object MimeKit.MailboxAddress("Excel Laundry Services", $u)))
$m.To.Add((New-Object MimeKit.MailboxAddress("ELS Tenant", $u)))
$m.Subject = "LMS MailKit 465 test"
$bb = New-Object MimeKit.BodyBuilder
$bb.HtmlBody = "<b>MailKit over implicit TLS works</b>"
$m.Body = $bb.ToMessageBody()

$c = New-Object MailKit.Net.Smtp.SmtpClient
try {
    $c.Connect("smtp.gmail.com", 465, [MailKit.Security.SecureSocketOptions]::SslOnConnect)
    $c.Authenticate($u, $pw)
    $c.Send($m)
    $c.Disconnect($true)
    "SUCCESS: sent to $u"
}
catch {
    $e = $_.Exception
    while ($e) {
        "[" + $e.GetType().Name + "] " + $e.Message
        $e = $e.InnerException
    }
}

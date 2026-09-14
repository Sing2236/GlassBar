param(
    [Parameter(Mandatory)]
    [string]$Email,

    [Parameter(Mandatory)]
    [string]$PayPalTransactionId,

    [switch]$PaymentVerified,
    [switch]$DryRun,
    [string]$PrivateKeyPath = (Join-Path $env:USERPROFILE '.glassbar\license-private.pem'),
    [string]$SmtpHost = $(if ($env:GLASSBAR_SMTP_HOST) { $env:GLASSBAR_SMTP_HOST } else { 'smtp.gmail.com' }),
    [int]$SmtpPort = $(if ($env:GLASSBAR_SMTP_PORT) { [int]$env:GLASSBAR_SMTP_PORT } else { 587 }),
    [string]$SmtpUser = $(if ($env:GLASSBAR_SMTP_USER) { $env:GLASSBAR_SMTP_USER } else { 'Ethanhuynh365@gmail.com' }),
    [string]$FromEmail = $(if ($env:GLASSBAR_SMTP_FROM) { $env:GLASSBAR_SMTP_FROM } else { 'Ethanhuynh365@gmail.com' }),
    [securestring]$SmtpPassword
)

$ErrorActionPreference = 'Stop'

if (-not $DryRun -and -not $PaymentVerified) {
    throw 'Check the $5.00 PayPal payment first, then rerun with -PaymentVerified.'
}

try {
    $recipient = [System.Net.Mail.MailAddress]::new($Email.Trim())
    $sender = [System.Net.Mail.MailAddress]::new($FromEmail.Trim(), 'GlassBar')
}
catch {
    throw 'Email and FromEmail must both be valid email addresses.'
}

$transactionId = $PayPalTransactionId.Trim()
if ([string]::IsNullOrWhiteSpace($transactionId)) { throw 'PayPalTransactionId cannot be empty.' }
if (-not (Test-Path -LiteralPath $PrivateKeyPath)) { throw "The signing key was not found at $PrivateKeyPath." }

$ownerFolder = Join-Path $env:USERPROFILE '.glassbar'
$fulfillmentPath = Join-Path $ownerFolder 'fulfilled-payments.json'
$fulfilled = @()
if (Test-Path -LiteralPath $fulfillmentPath) {
    $fulfilled = @([IO.File]::ReadAllText($fulfillmentPath) | ConvertFrom-Json)
}
if (-not $DryRun -and @($fulfilled | Where-Object PayPalTransactionId -EQ $transactionId).Count -gt 0) {
    throw "PayPal transaction '$transactionId' has already been fulfilled."
}

$projectRoot = Split-Path $PSScriptRoot -Parent
$issuerProject = Join-Path $projectRoot 'tools\GlassBar.LicenseIssuer\GlassBar.LicenseIssuer.csproj'
$issuerOutput = @(& dotnet run --project $issuerProject -c Release -- issue $PrivateKeyPath $recipient.Address)
if ($LASTEXITCODE -ne 0) { throw 'The GlassBar license issuer failed.' }
$licenseKey = ($issuerOutput | Where-Object { $_ -like 'GB1.*' } | Select-Object -Last 1).Trim()
if ([string]::IsNullOrWhiteSpace($licenseKey)) { throw 'The GlassBar license issuer did not return a key.' }

$subject = 'Your GlassBar Pro lifetime license'
$body = @"
Thanks for supporting GlassBar.

Your one-time GlassBar Pro license unlocks Snow, Fireflies, Pulse, and the Custom Effect Lab.

LICENSE KEY
$licenseKey

To activate it:
1. Open GlassBar Settings.
2. Paste the full key into the GlassBar Pro box.
3. Select Activate license.

Keep this email so you can activate GlassBar again later.

PayPal transaction: $transactionId
"@

if ($DryRun) {
    Write-Host 'Dry run only — no email was sent and the transaction was not recorded.'
    Write-Output "Recipient: $($recipient.Address)"
    Write-Output $body
    exit 0
}

if ($null -eq $SmtpPassword) {
    if ($env:GLASSBAR_SMTP_PASSWORD) {
        $SmtpPassword = ConvertTo-SecureString $env:GLASSBAR_SMTP_PASSWORD -AsPlainText -Force
    }
    else {
        $SmtpPassword = Read-Host "SMTP app password for $SmtpUser" -AsSecureString
    }
}

$mail = [System.Net.Mail.MailMessage]::new()
$smtp = [System.Net.Mail.SmtpClient]::new($SmtpHost, $SmtpPort)
try {
    $mail.From = $sender
    $mail.To.Add($recipient)
    $mail.Subject = $subject
    $mail.Body = $body
    $mail.IsBodyHtml = $false

    $smtp.EnableSsl = $true
    $smtp.UseDefaultCredentials = $false
    $smtp.Credentials = [System.Net.NetworkCredential]::new($SmtpUser, $SmtpPassword)
    $smtp.Send($mail)
}
finally {
    $mail.Dispose()
    $smtp.Dispose()
}

New-Item -ItemType Directory -Path $ownerFolder -Force | Out-Null
$fulfilled += [pscustomobject]@{
    PayPalTransactionId = $transactionId
    Email = $recipient.Address
    FulfilledAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
}
$temporaryPath = $fulfillmentPath + '.tmp'
[IO.File]::WriteAllText($temporaryPath, ($fulfilled | ConvertTo-Json -Depth 4))
Move-Item -LiteralPath $temporaryPath -Destination $fulfillmentPath -Force

Write-Host "License emailed to $($recipient.Address) and transaction $transactionId was recorded."

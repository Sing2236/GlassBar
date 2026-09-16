param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$Email,

    [string]$PrivateKeyPath = (Join-Path $env:USERPROFILE '.glassbar\license-private.pem')
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$issuerProject = Join-Path $projectRoot 'tools\GlassBar.LicenseIssuer\GlassBar.LicenseIssuer.csproj'

Write-Host 'Confirm the $5.00 GlassBar Pro payment in PayPal before issuing this key.' -ForegroundColor Yellow
dotnet run --project $issuerProject --configuration Release -- issue $PrivateKeyPath $Email

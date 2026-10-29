$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$localEnvironment = Join-Path $repository '.env'
if (!(Test-Path -LiteralPath $localEnvironment)) { throw 'Initialize PostgreSQL first.' }
if (!(Select-String -LiteralPath $localEnvironment -Pattern '^JWT__SigningKey=' -Quiet)) {
    $generatedJwtKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(64))
    Add-Content -LiteralPath $localEnvironment -Value "`nJWT__SigningKey=$generatedJwtKey"
}
Write-Output 'JWT signing key configured in the ignored local environment. No key value printed.'

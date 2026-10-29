$ErrorActionPreference = 'Stop'
$repositoryPath = Split-Path -Parent $PSScriptRoot
$environmentPath = Join-Path $repositoryPath '.env'
if (-not (Test-Path -LiteralPath $environmentPath)) {
    $localPassword = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    [System.IO.File]::WriteAllText($environmentPath, "Database__Password=$localPassword`n")
}
. (Join-Path $PSScriptRoot 'Import-LocalEnvironment.ps1')
if ([string]::IsNullOrWhiteSpace($env:Database__Password)) {
    throw 'Set Database__Password in the ignored .env file.'
}
docker compose --progress quiet --project-directory $repositoryPath up -d --wait postgres
if ($LASTEXITCODE -ne 0) { throw 'PostgreSQL startup failed.' }
Write-Output 'PostgreSQL is ready on localhost:55432. Local credentials are in the ignored .env file.'

$ErrorActionPreference = 'Stop'
$repositoryPath = Split-Path -Parent $PSScriptRoot
$environmentPath = Join-Path $repositoryPath '.env'
if (-not (Test-Path -LiteralPath $environmentPath)) {
    throw 'Missing .env. Run scripts/Initialize-Postgres.ps1 first.'
}
foreach ($line in [System.IO.File]::ReadAllLines($environmentPath)) {
    if ($line -match '^([A-Za-z_][A-Za-z0-9_]*)=(.*)$') {
        [Environment]::SetEnvironmentVariable($Matches[1], $Matches[2], 'Process')
    }
}

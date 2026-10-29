$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$fixture = Get-Content -LiteralPath (Join-Path $root 'artifacts/frontend/ticket-details-fixture.json') -Raw | ConvertFrom-Json
# Reuse the owned local fixture and test providers; no production credentials.
$env:MCP__Clients__0__UserId = [Guid]::Parse($fixture.authorId).ToString()
& (Join-Path $PSScriptRoot 'start-ticket-details-api.ps1')

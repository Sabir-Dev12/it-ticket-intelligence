$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Set-Location -LiteralPath $root
. ./scripts/Import-LocalEnvironment.ps1
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Rag__Ollama__BaseUrl = 'http://127.0.0.1:4909/ollama/'
$env:Rag__Ollama__Model = 'simulation-embedding'
$env:Rag__Ollama__ModelDigest = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
$env:Rag__Ollama__Dimensions = '3'
$env:LLM__Provider = 'Ollama'
$env:LLM__BaseUrl = 'http://127.0.0.1:4909/llm/'
$env:LLM__Model = 'agent-valid'
$temporaryToken = 'ticket-details-local-verification-0123456789abcdef'
$env:MCP__Clients__0__TokenHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($temporaryToken))).ToLowerInvariant()
$env:MCP__Clients__0__Subject = 'ticket-details-verification'
$env:MCP__Clients__0__Permissions = 'tickets.read'
$env:MCP__Clients__0__AllTickets = 'true'
$env:Realtime__AllowedOrigins = 'http://127.0.0.1:5173,http://127.0.0.1:5174'
dotnet run --project backend/src/ITTicketIntelligence.Api --configuration Release --no-build --no-launch-profile --urls http://127.0.0.1:5080

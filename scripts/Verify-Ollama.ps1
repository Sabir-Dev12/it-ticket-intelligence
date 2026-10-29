param([switch]$Simulation)
$ErrorActionPreference='Stop'
$repository=Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repository
. ./scripts/Import-LocalEnvironment.ps1
$database='itt_ollama_verify_'+[Guid]::NewGuid().ToString('N')
if($database -notmatch '^itt_ollama_verify_[a-f0-9]{32}$'){throw 'Invalid verification database.'}
$logs=Join-Path $repository 'artifacts/ollama-tests'
New-Item -ItemType Directory -Path $logs -Force | Out-Null
$temporaryEnvironment=Join-Path $logs 'temporary.environment.yaml'
$apiProcess=$null;$simulationProcess=$null;$created=$false
try {
    if($Simulation){
        $simulationProcess=Start-Process (Get-Command postman).Source -ArgumentList 'simulation','run','postman/simulations/llm-provider/llm-provider.sim.yaml','--port','4914' -WorkingDirectory $repository -WindowStyle Hidden -RedirectStandardOutput (Join-Path $logs 'simulation.log') -RedirectStandardError (Join-Path $logs 'simulation-error.log') -PassThru
        $env:LLM__BaseUrl='http://127.0.0.1:4914/llm/'
        $env:LLM__Model='chat-sim'
    } else {$env:LLM__BaseUrl='http://localhost:11434/';$env:LLM__Model='qwen3:1.7b'}
    $env:LLM__Provider='Ollama'
    docker exec it-ticket-intelligence-postgres-1 psql -U ticket_app -d postgres -v ON_ERROR_STOP=1 -c "CREATE DATABASE $database" | Out-Null
    if($LASTEXITCODE -ne 0){throw 'Cannot create verification database.'};$created=$true
    $env:ConnectionStrings__Postgres="Host=localhost;Port=55432;Database=$database;Username=ticket_app;Password=$env:Database__Password"
    $env:AUTH__BootstrapEmail=$null;$env:AUTH__BootstrapPassword=$null
    dotnet ef database update --no-build --project backend/src/ITTicketIntelligence.Infrastructure --startup-project backend/src/ITTicketIntelligence.Api --configuration Release | Out-Null
    if($LASTEXITCODE -ne 0){throw 'Migration failed.'}
    $env:JWT__SigningKey=[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(64))
    $env:AUTH__BootstrapEmail=[Guid]::NewGuid().ToString('N')+'@example.test'
    $env:AUTH__BootstrapPassword=[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    $env:ASPNETCORE_ENVIRONMENT='Development'
    $runtime=Join-Path $repository 'backend/src/ITTicketIntelligence.Api/bin/Release/net10.0'
    $apiProcess=Start-Process dotnet -ArgumentList 'ITTicketIntelligence.Api.dll','--urls','http://127.0.0.1:5082' -WorkingDirectory $runtime -WindowStyle Hidden -RedirectStandardOutput (Join-Path $logs 'api.log') -RedirectStandardError (Join-Path $logs 'api-error.log') -PassThru
    $ready=$false
    for($attempt=0;$attempt -lt 40;$attempt++){try{$response=Invoke-WebRequest 'http://127.0.0.1:5082/api/auth/me' -SkipHttpErrorCheck -TimeoutSec 1;if($response.StatusCode -eq 401){$ready=$true;break}}catch{};Start-Sleep -Milliseconds 250}
    if(!$ready){throw 'Verification API did not start.'}
    @"
name: Local isolated Ollama verification
values:
  - key: baseUrl
    value: 'http://127.0.0.1:5082'
    enabled: true
  - key: authEmail
    value: '$env:AUTH__BootstrapEmail'
    enabled: true
    type: secret
  - key: authPassword
    value: '$env:AUTH__BootstrapPassword'
    enabled: true
    type: secret
"@ | Set-Content -LiteralPath $temporaryEnvironment
    postman collection run 'postman/collections/Local Ollama Chat' -e $temporaryEnvironment --no-report-events --timeout-request 120000
    if($LASTEXITCODE -ne 0){throw 'Ollama API checks failed.'}
    if(!$Simulation){
        $vectors=Invoke-RestMethod 'http://localhost:11434/api/embed' -Method Post -ContentType 'application/json' -Body '{"model":"nomic-embed-text:latest","input":["search_query: SQL Server timeout"],"truncate":false}' -TimeoutSec 60
        if($vectors.embeddings.Count -ne 1 -or $vectors.embeddings[0].Count -ne 768){throw 'Unexpected embedding dimensions.'}
        Write-Output 'Real local embedding verified: nomic-embed-text:latest, 768 dimensions.'
    }
} finally {
    if(Test-Path -LiteralPath $temporaryEnvironment){Remove-Item -LiteralPath $temporaryEnvironment}
    if($apiProcess -and !$apiProcess.HasExited){Stop-Process -Id $apiProcess.Id}
    if($simulationProcess -and !$simulationProcess.HasExited){Stop-Process -Id $simulationProcess.Id}
    if($created){docker exec it-ticket-intelligence-postgres-1 psql -U ticket_app -d postgres -v ON_ERROR_STOP=1 -c "DROP DATABASE IF EXISTS $database WITH (FORCE)" | Out-Null}
}

$ErrorActionPreference='Stop'
$repository=Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repository
. ./scripts/Import-LocalEnvironment.ps1
$verificationDatabase='itt_auth_verify_'+[Guid]::NewGuid().ToString('N')
if($verificationDatabase -notmatch '^itt_auth_verify_[a-f0-9]{32}$'){throw 'Invalid verification target.'}
$apiProcess=$null
$uiProcess=$null
$postmanEnvironment=Join-Path $repository 'artifacts/frontend/auth-temporary.environment.yaml'
try {
    & docker exec it-ticket-intelligence-postgres-1 psql -U ticket_app -d postgres -v ON_ERROR_STOP=1 -c "CREATE DATABASE $verificationDatabase" | Out-Null
    if($LASTEXITCODE -ne 0){throw 'Cannot prepare the temporary database.'}
    $env:ConnectionStrings__Postgres="Host=localhost;Port=55432;Database=$verificationDatabase;Username=ticket_app;Password=$env:Database__Password"
    $env:JWT__SigningKey=[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(64))
    $env:AUTH__BootstrapEmail=$null
    $env:AUTH__BootstrapPassword=$null
    dotnet ef database update --project backend/src/ITTicketIntelligence.Infrastructure --startup-project backend/src/ITTicketIntelligence.Api --configuration Release | Out-Null
    if($LASTEXITCODE -ne 0){throw 'Cannot migrate the temporary database.'}
    $env:AUTH__BootstrapEmail=[Guid]::NewGuid().ToString('N')+'@example.test'
    $env:AUTH__BootstrapPassword=[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    $env:ASPNETCORE_ENVIRONMENT='Development'
    $env:Realtime__AllowedOrigins='http://127.0.0.1:5175'
    $apiFolder=Join-Path $repository 'backend/src/ITTicketIntelligence.Api/bin/Release/net10.0'
    $apiProcess=Start-Process dotnet -ArgumentList 'ITTicketIntelligence.Api.dll','--urls','http://127.0.0.1:5081' -WorkingDirectory $apiFolder -WindowStyle Hidden -RedirectStandardOutput (Join-Path $repository 'artifacts/frontend/auth-api.log') -RedirectStandardError (Join-Path $repository 'artifacts/frontend/auth-api-error.log') -PassThru
    $env:API_PROXY_TARGET='http://127.0.0.1:5081'
    $uiProcess=Start-Process node -ArgumentList 'node_modules/vite/bin/vite.js','--host','127.0.0.1','--port','5175' -WorkingDirectory (Join-Path $repository 'frontend') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $repository 'artifacts/frontend/auth-vite.log') -RedirectStandardError (Join-Path $repository 'artifacts/frontend/auth-vite-error.log') -PassThru
    $ready=$false
    for($attempt=0;$attempt -lt 40;$attempt++){
        try { $result=Invoke-WebRequest 'http://127.0.0.1:5081/api/auth/me' -SkipHttpErrorCheck -TimeoutSec 1; if($result.StatusCode -eq 401){$ready=$true;break} } catch {}
        Start-Sleep -Milliseconds 250
    }
    if(!$ready){throw 'The verification API did not start.'}
    @"
name: Isolated JWT verification
values:
  - key: baseUrl
    value: 'http://127.0.0.1:5081'
    enabled: true
  - key: authEmail
    value: '$env:AUTH__BootstrapEmail'
    enabled: true
    type: secret
  - key: authPassword
    value: '$env:AUTH__BootstrapPassword'
    enabled: true
    type: secret
"@ | Set-Content -LiteralPath $postmanEnvironment
    postman collection run 'postman/collections/JWT Authentication' -e $postmanEnvironment --no-report-events
    if($LASTEXITCODE -ne 0){throw 'The real Postman JWT checks failed.'}
    Set-Location -LiteralPath (Join-Path $repository 'frontend')
    node scripts/auth-live.mjs
    if($LASTEXITCODE -ne 0){throw 'The real authentication browser check failed.'}
}finally{
    if(Test-Path -LiteralPath $postmanEnvironment){Remove-Item -LiteralPath $postmanEnvironment}
    if($uiProcess -and !$uiProcess.HasExited){Stop-Process -Id $uiProcess.Id}
    if($apiProcess -and !$apiProcess.HasExited){Stop-Process -Id $apiProcess.Id}
    Set-Location -LiteralPath $repository
    & docker exec it-ticket-intelligence-postgres-1 psql -U ticket_app -d postgres -v ON_ERROR_STOP=1 -c "DROP DATABASE IF EXISTS $verificationDatabase WITH (FORCE)" | Out-Null
}

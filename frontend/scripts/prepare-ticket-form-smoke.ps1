$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$run = [Guid]::NewGuid().ToString('N')
$actor = [Guid]::NewGuid().ToString()
$team = [Guid]::NewGuid().ToString()
$teamName = "Form verification $($run.Substring(0,8))"
# Only locally generated GUIDs enter these statements.
$sql = "INSERT INTO app.users(id,name,email,role,is_active,created_at,updated_at) VALUES ('$actor','Form verification','$run@example.test','Agent',true,now(),now()); INSERT INTO app.teams(id,name,description,is_active,created_at,updated_at) VALUES ('$team','$teamName','Owned ticket form fixture',true,now(),now());"
& docker exec it-ticket-intelligence-postgres-1 psql -U ticket_app -d it_ticket_intelligence -v ON_ERROR_STOP=1 -c $sql | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not create owned form references.' }
@{ run=$run; authorId=$actor; teamId=$team; teamName=$teamName } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'artifacts/frontend/ticket-form-fixture.json')
'Observed SQL connection timeout. No changes were executed.' | Set-Content -LiteralPath (Join-Path $root 'artifacts/frontend/ticket-form-upload.txt') -NoNewline
Write-Output 'Owned active form author and team prepared. No credentials recorded.'

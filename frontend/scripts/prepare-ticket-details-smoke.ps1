param([string]$ApiUrl = 'http://127.0.0.1:5080')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$run = [Guid]::NewGuid().ToString('N')
$actor = [Guid]::NewGuid().ToString()
$team = [Guid]::NewGuid().ToString()
$teamName = "Ticket details verification $($run.Substring(0,8))"
# Fixture identifiers are generated here; no external input enters SQL.
$sql = "INSERT INTO app.users(id,name,email,role,is_active,created_at,updated_at) VALUES ('$actor','Ticket Details verification','$run@example.test','Agent',true,now(),now()); INSERT INTO app.teams(id,name,description,is_active,created_at,updated_at) VALUES ('$team','$teamName','Owned browser verification fixture',true,now(),now());"
& docker exec it-ticket-intelligence-postgres-1 psql -U ticket_app -d it_ticket_intelligence -v ON_ERROR_STOP=1 -c $sql | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not prepare the owned fixture.' }
$ticket = Invoke-RestMethod "$ApiUrl/api/tickets" -Method Post -ContentType 'application/json' -Body (@{ title='SQL Server connection timeout'; description='Browser verification fixture: cannot connect to SQL Server after deployment. Check the SQL listener and network path.'; createdById=$actor } | ConvertTo-Json)
$document = Invoke-RestMethod "$ApiUrl/api/knowledge" -Method Post -ContentType 'application/json' -Body (@{ title="SQL connection verification $($run.Substring(0,8))"; content='Verify the configured SQL host and TCP port 1433. Collect the observed error. Ask an operator to review firewall settings before any change.'; source="verification/ticket-details/$run.md"; category='Database'; kind='Procedure'; format='PlainText' } | ConvertTo-Json)
Invoke-RestMethod "$ApiUrl/api/rag/documents/$($document.id)/index" -Method Post | Out-Null
$artifact = Join-Path $root 'artifacts/frontend/ticket-details-fixture.json'
@{ ticketId=$ticket.id; authorId=$actor; teamId=$team; teamName=$teamName; documentId=$document.id; documentVersion=$document.version; run=$run } | ConvertTo-Json | Set-Content -LiteralPath $artifact
Write-Output 'Owned ticket, author, team and knowledge fixture prepared; no credentials recorded.'

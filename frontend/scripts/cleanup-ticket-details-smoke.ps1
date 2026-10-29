param([string]$ApiUrl = 'http://127.0.0.1:5080')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$fixture = Get-Content -LiteralPath (Join-Path $root 'artifacts/frontend/ticket-details-fixture.json') -Raw | ConvertFrom-Json
$actor = [Guid]::Parse($fixture.authorId).ToString()
$team = [Guid]::Parse($fixture.teamId).ToString()
$ticketId = [Guid]::Parse($fixture.ticketId).ToString()
$ticket = Invoke-RestMethod "$ApiUrl/api/tickets/$ticketId"
if ($ticket.createdById -ne $actor -or $ticket.description -notlike 'Browser verification fixture:*') { throw 'Fixture ownership check failed.' }
if (!$ticket.isArchived) { Invoke-RestMethod "$ApiUrl/api/tickets/$($ticket.id)?revision=$($ticket.revision)" -Method Delete | Out-Null }
$document = Invoke-RestMethod "$ApiUrl/api/knowledge/$($fixture.documentId)"
if ($document.source -ne "verification/ticket-details/$($fixture.run).md") { throw 'Knowledge fixture ownership check failed.' }
Invoke-RestMethod "$ApiUrl/api/knowledge/$($fixture.documentId)?version=$($document.version)" -Method Delete | Out-Null
$sql = "UPDATE app.users SET is_active=false,updated_at=now() WHERE id='$actor' AND name='Ticket Details verification'; UPDATE app.teams SET is_active=false,updated_at=now() WHERE id='$team' AND description='Owned browser verification fixture';"
& docker exec it-ticket-intelligence-postgres-1 psql -U ticket_app -d it_ticket_intelligence -v ON_ERROR_STOP=1 -c $sql | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not deactivate the owned fixture references.' }
Write-Output 'Owned ticket archived; synthetic source removed; fixture author/team deactivated; audit and predictions preserved.'

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$fixture = Get-Content -LiteralPath (Join-Path $root 'artifacts/frontend/ticket-form-fixture.json') -Raw | ConvertFrom-Json
$actor = [Guid]::Parse($fixture.authorId).ToString()
$team = [Guid]::Parse($fixture.teamId).ToString()
$page = Invoke-RestMethod "http://127.0.0.1:5080/api/tickets?createdById=$actor&includeArchived=true&pageSize=100"
if ($page.totalCount -gt 100) { throw 'Unexpected fixture size; cleanup stopped.' }
foreach ($entry in $page.items) {
    if ($entry.createdById -ne $actor -or $entry.description -notlike 'Form verification fixture:*') { throw 'Ticket ownership check failed.' }
    $ticketId = [Guid]::Parse($entry.id).ToString()
    $ticket = Invoke-RestMethod "http://127.0.0.1:5080/api/tickets/$ticketId"
    $files = Invoke-RestMethod "http://127.0.0.1:5080/api/tickets/$ticketId/attachments"
    foreach ($file in $files.items) {
        if ($ticket.isArchived) { throw 'Unexpected remaining file on an archived fixture.' }
        $removed = Invoke-RestMethod "http://127.0.0.1:5080/api/tickets/$ticketId/attachments/$($file.id)?revision=$($ticket.revision)" -Method Delete
        $ticket.revision = $removed.revision
    }
    if (!$ticket.isArchived) { Invoke-RestMethod "http://127.0.0.1:5080/api/tickets/$($ticketId)?revision=$($ticket.revision)" -Method Delete | Out-Null }
}
$sql = "UPDATE app.users SET is_active=false,updated_at=now() WHERE id='$actor' AND name='Form verification'; UPDATE app.teams SET is_active=false,updated_at=now() WHERE id='$team' AND description='Owned ticket form fixture';"
& docker exec it-ticket-intelligence-postgres-1 psql -U ticket_app -d it_ticket_intelligence -v ON_ERROR_STOP=1 -c $sql | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not deactivate owned references.' }
Write-Output 'Owned form tickets archived, evidence removed and author/team deactivated. Audit retained.'

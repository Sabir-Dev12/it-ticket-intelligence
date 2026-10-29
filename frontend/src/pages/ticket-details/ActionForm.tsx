import { useWorkspace } from '../../app/Workspace';
import { useState } from 'react';
import type { Ticket, TicketStatus, TicketWorkspace } from '../../api/contracts';
import { Button } from '../../components/ui';
import { nextStatuses, validId } from './model';

export type FormKind = 'assignment' | 'status' | 'comment';
export interface FormSubmission { revision: number; teamId: string | null; status: TicketStatus; resolution: string | null; authorId: string; message: string }
export function ActionForm({ kind, ticket, workspace, pending, onClose, onSubmit }: {
  kind: FormKind; ticket: Ticket; workspace?: TicketWorkspace; pending: boolean; onClose: () => void;
  onSubmit: (data: FormSubmission) => void;
}) {
  const { user } = useWorkspace();
  const [revision, setRevision] = useState(ticket.revision);
  const [teamId, setTeamId] = useState(ticket.assignedTeamId ?? '');
  const [status, setStatus] = useState<TicketStatus>(nextStatuses(ticket.status)[0] ?? ticket.status);
  const [resolution, setResolution] = useState('');
  const [authorId, setAuthorId] = useState(user?.id ?? '');
  const [message, setMessage] = useState('');
  const [search, setSearch] = useState('');
  const [error, setError] = useState('');
  const changed = ticket.revision !== revision;
  const title = kind === 'assignment' ? 'Assign Team' : kind === 'status' ? 'Change Status' : 'Add Comment';
  const allowed = nextStatuses(ticket.status);
  const filteredTeams = workspace?.assignableTeams.filter(team => team.name.toLowerCase().includes(search.toLowerCase())) ?? [];
  function submit(event: React.FormEvent) {
    event.preventDefault();
    if (changed) return;
    if (kind === 'assignment' && teamId && !validId(teamId)) { setError('Choose an available team or enter a valid team ID.'); return; }
    if (kind === 'status' && (!allowed.includes(status) || (status === 'Resolved' && !resolution.trim()))) { setError('Choose an allowed status and provide a resolution when resolving the ticket.'); return; }
    if (kind === 'comment' && !validId(authorId.trim())) { setError('Enter a valid active user ID.'); return; }
    if (kind === 'comment' && !message.trim()) { setError('Enter a non-empty comment.'); return; }
    setError(''); onSubmit({ revision, teamId: teamId || null, status, resolution: status === 'Resolved' ? resolution.trim() : null, authorId: authorId.trim(), message: message.trim() });
  }
  return <section className="panel ticket-action-form" aria-labelledby="action-heading"><div className="section-header"><div><h2 id="action-heading">{title}</h2><p>Review this change before saving. Ticket revision {revision}.</p></div><Button onClick={onClose} disabled={pending}>Cancel</Button></div><form onSubmit={submit}>
    {changed && <div className="inline-error" role="alert"><p>The ticket changed while you were editing. Review its current properties before continuing.</p><Button type="button" onClick={() => { setRevision(ticket.revision); if (kind === 'status') setStatus(allowed[0] ?? ticket.status); }}>Use current revision {ticket.revision}</Button></div>}
    <fieldset disabled={pending || changed}>
      {kind === 'assignment' && <><label htmlFor="team-search">Find a team</label><input id="team-search" autoFocus value={search} onChange={event => setSearch(event.target.value)} placeholder="Search available teams" /><fieldset className="choice-list"><legend>Available teams</legend><label className={teamId === '' ? 'selected' : ''}><input type="radio" name="team" checked={teamId === ''} onChange={() => setTeamId('')} />Unassigned</label>{filteredTeams.map(team => <label className={teamId === team.id ? 'selected' : ''} key={team.id}><input type="radio" name="team" checked={teamId === team.id} onChange={() => setTeamId(team.id)} />{team.name}</label>)}</fieldset>{workspace && filteredTeams.length === 0 && <p className="field-help" role="status">No teams match this search. Try another name.</p>}{workspace?.teamsTruncated && <p className="field-help">More teams exist than can be listed. Enter the required team ID below.</p>}{!workspace && <p className="field-help">Team names are unavailable. You can still supply a known active team ID.</p>}<details><summary>Use a team ID</summary><label htmlFor="team-id">Team ID</label><input id="team-id" value={teamId} onChange={event => setTeamId(event.target.value.trim())} maxLength={36} spellCheck={false} /><p className="field-help">Only active teams can be assigned. Leave empty to unassign.</p></details></>}
      {kind === 'status' && <><fieldset className="choice-list"><legend>Next status</legend>{allowed.map(value => <label key={value} className={status === value ? 'selected' : ''}><input type="radio" name="status" autoFocus={value === allowed[0]} checked={status === value} onChange={() => setStatus(value)} />{value === 'InProgress' ? ticket.status === 'Resolved' ? 'Reopen as in progress' : 'In progress' : value}</label>)}</fieldset>{status === 'Resolved' && <><label htmlFor="resolution">Resolution</label><textarea id="resolution" required value={resolution} onChange={event => setResolution(event.target.value)} maxLength={12000} rows={4} /><p className="field-help">Describe the resolution before marking this ticket resolved.</p></>}</>}
      {kind === 'comment' && <>{!user && <><label htmlFor="author-id">Your user ID</label><input id="author-id" required maxLength={36} value={authorId} onChange={event => setAuthorId(event.target.value)} spellCheck={false} /></>}<label htmlFor="comment-message">Comment</label><textarea id="comment-message" autoFocus required maxLength={4000} rows={4} value={message} onChange={event => setMessage(event.target.value)} /><p className="field-help">{message.length} / 4,000 characters{user ? ` · Posting as ${user.name}` : ''}</p></>}
    </fieldset>{error && <p className="inline-error" role="alert">{error}</p>}<div className="form-actions"><Button className="primary" type="submit" loading={pending} disabled={changed}>{kind === 'comment' ? 'Save comment' : kind === 'status' ? 'Save status' : 'Save assignment'}</Button></div></form></section>;
}

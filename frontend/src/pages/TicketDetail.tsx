import { ArrowLeft, Bot, BrainCircuit, CircleCheck, MessageSquare, RefreshCw, Users, Workflow } from 'lucide-react';
import { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useParams, useLocation } from 'react-router-dom';
import { api } from '../api/client';
import { useWorkspace } from '../app/Workspace';
import { useResource } from '../hooks/useResource';
import { Button, date, FeatureNotice, PageHeader, PriorityBadge, ResourceState, SectionHeader, StatusBadge } from '../components/ui';
import { AiPanel, MlPanel } from './ticket-details/IntelligencePanels';
import { ActionForm } from './ticket-details/ActionForm';
import { AttachmentsPanel } from './ticket-details/AttachmentsPanel';
import type { FormKind, FormSubmission } from './ticket-details/ActionForm';
import { mayAnalyze, nextStatuses, validId } from './ticket-details/model';
export { validId } from './ticket-details/model';

export default function TicketDetail() {
  const location = useLocation();
  const { id = '' } = useParams();
  const { revision, liveState, live, refresh, user } = useWorkspace();
  const [joinError, setJoinError] = useState(false);
  const [form, setForm] = useState<FormKind | null>(null);
  const [pending, setPending] = useState<string | null>(null);
  const [feedback, setFeedback] = useState<{ error: boolean; text: string } | null>(null);
  const operation = useRef<AbortController | null>(null);
  const resource = useResource(useCallback((signal: AbortSignal) => { void revision; if (!validId(id)) return Promise.reject(new Error('This ticket link is invalid. Return to the queue and choose a ticket.')); return api.ticket(id, signal); }, [id, revision]), true);
  const context = useResource(useCallback(async (signal: AbortSignal) => { void revision; if (!validId(id)) throw new Error('Invalid ticket link.'); return { ticketId: id, ...await api.workspace(id, signal) }; }, [id, revision]), true);
  const ticket = resource.data?.id === id ? resource.data : undefined;
  const workspace = context.data?.ticketId === id ? context.data : undefined;
  const assignedTeam = workspace?.assignedTeam?.id === ticket?.assignedTeamId ? workspace?.assignedTeam : null;
  useEffect(() => { setForm(null); setFeedback(null); setPending(null); return () => { operation.current?.abort(); operation.current = null; }; }, [id]);
  useEffect(() => {
    setJoinError(false);
    const client = live.current;
    if (liveState !== 'connected' || !client || !validId(id)) return;
    let active = true;
    void client.join(id).then(() => { if (active) refresh(); }).catch(() => { if (active) setJoinError(true); });
    return () => { active = false; void client.leave(id).catch(() => {}); };
    // Rejoin only when the ticket or connection changes, not on invalidation.
  }, [id, liveState, live]);
  async function execute(label: string, work: (signal: AbortSignal) => Promise<unknown>) {
    if (operation.current) return;
    const controller = new AbortController(); operation.current = controller;
    setPending(label); setFeedback(null);
    try {
      await work(controller.signal);
      if (!controller.signal.aborted) { setFeedback({ error: false, text: `${label} completed. Refreshing the saved ticket data.` }); setForm(null); }
    } catch (error) {
      if (!controller.signal.aborted) setFeedback({ error: true, text: `${label} could not be confirmed. ${error instanceof Error ? error.message : 'Refresh and review the ticket before trying again.'}` });
    } finally {
      if (!controller.signal.aborted) { operation.current = null; setPending(null); resource.reload(); context.reload(); refresh(); }
    }
  }
  function submit(data: FormSubmission) {
    if (form === 'assignment') void execute('Assignment', signal => api.assign(id, data.teamId, data.revision, signal));
    if (form === 'status') void execute('Status change', signal => api.changeStatus(id, data.status, data.resolution, data.revision, signal));
    if (form === 'comment') void execute('Comment', signal => api.comment(id, data.authorId, data.message, data.revision, signal));
  }
  function openForm(kind: FormKind) { setFeedback(null); setForm(kind); }
  const manage = user?.role === 'Admin' || user?.role === 'Support';
  const active = ticket && mayAnalyze(ticket) && user?.role !== 'User';
  return <div className="ticket-details-page"><Link className="back-link" to="/tickets"><ArrowLeft size={16} aria-hidden="true" />Back to tickets</Link>
    <PageHeader eyebrow={ticket?.reference ?? 'TICKET DETAIL'} title={ticket?.title ?? 'Ticket context'} description="Reported facts, model predictions and proposed next steps.">{active && manage && <Link className="button" to={`/tickets/${id}/edit`}>Edit ticket</Link>}<Button onClick={() => { resource.reload(); context.reload(); }} loading={resource.loading && !ticket}><RefreshCw size={16} aria-hidden="true" />Refresh</Button></PageHeader>
    {typeof location.state?.ticketNotice === 'string' && <div className="action-feedback success" role="status">{location.state.ticketNotice}</div>}
    {!ticket && <ResourceState {...resource} retry={resource.reload} />}
    {ticket && <>
      <div className="ticket-command-bar" aria-label="Ticket actions"><div className="analysis-actions"><Button className="primary" disabled={!active || Boolean(pending)} loading={pending === 'Ticket analysis'} onClick={() => void execute('Ticket analysis', signal => api.analyze(id, signal))}><BrainCircuit size={17} aria-hidden="true" />Analyze Ticket</Button><Button disabled={!active || Boolean(pending)} loading={pending === 'AI investigation'} onClick={() => void execute('AI investigation', signal => api.investigate(id, signal))}><Bot size={17} aria-hidden="true" />Ask AI</Button>{active && <Link className="button" to={`/ai-assistant?ticketId=${id}`}>Discuss with assistant</Link>}</div><div className="management-actions"><Button disabled={!active || !manage || Boolean(pending)} onClick={() => openForm('assignment')}><Users size={16} aria-hidden="true" />Assign Team</Button><Button disabled={!manage || ticket.isArchived || nextStatuses(ticket.status).length === 0 || Boolean(pending)} onClick={() => openForm('status')}><Workflow size={16} aria-hidden="true" />Change Status</Button><Button disabled={ticket.isArchived || ticket.status === 'Closed' || Boolean(pending)} onClick={() => openForm('comment')}><MessageSquare size={16} aria-hidden="true" />Add Comment</Button></div></div>
      <div className="detail-live-line" role="status">{pending ? `${pending} in progress. ${pending === 'AI investigation' ? 'An investigation can take up to three minutes.' : 'Please wait for the saved result.'}` : resource.loading || context.loading ? 'Refreshing saved ticket data…' : liveState === 'connected' && !joinError ? 'Live updates connected. Saved changes refresh automatically.' : 'Manual snapshot. Enable live updates in Settings to receive changes.'}{!pending && liveState !== 'connected' && <Link className="text-link" to="/settings">Live settings</Link>}</div>
      {feedback && <div className={feedback.error ? 'inline-error' : 'action-success'} role={feedback.error ? 'alert' : 'status'}>{!feedback.error && <CircleCheck size={17} aria-hidden="true" />}<p>{feedback.text}</p></div>}
      {resource.error && <div className="inline-error" role="alert"><p>The current ticket could not be refreshed. {resource.error.message}</p><Button onClick={resource.reload}>Retry ticket read</Button></div>}
      {joinError && <FeatureNotice>Live access to this ticket was refused. Check the token’s ticket scope in Settings; use Refresh for a current snapshot.</FeatureNotice>}
      {ticket.isArchived || ticket.status === 'Closed' ? <FeatureNotice>This ticket is {ticket.isArchived ? 'archived' : 'closed'}. Its history is available for review; active actions are disabled.</FeatureNotice> : ticket.status === 'Resolved' && <FeatureNotice>This ticket is resolved. Reopen it before requesting a new investigation or assignment.</FeatureNotice>}
      {form && <ActionForm key={`${id}-${form}`} kind={form} ticket={ticket} workspace={workspace} pending={Boolean(pending)} onClose={() => setForm(null)} onSubmit={submit} />}
      <div className="detail-grid"><section className="panel detail-body"><div className="detail-status"><StatusBadge status={ticket.status} /><PriorityBadge priority={ticket.priority} />{ticket.isArchived && <span className="badge">Archived</span>}<span className="subtle-label">Reported ticket information</span></div><SectionHeader title="Description" /><p className="document-content">{ticket.description}</p>{ticket.resolution && <><SectionHeader title="Recorded resolution" /><p className="document-content">{ticket.resolution}</p></>}<div className="detail-meta"><span>Created {date(ticket.createdAt, true)}</span><span>Updated {date(ticket.updatedAt, true)}</span><span>Revision {ticket.revision}</span></div></section><aside className="panel context-panel"><SectionHeader title="Ticket properties" /><dl className="properties"><dt>Category</dt><dd>{ticket.category ?? 'Unclassified'}</dd><dt>Priority</dt><dd><PriorityBadge priority={ticket.priority} /></dd><dt>Status</dt><dd><StatusBadge status={ticket.status} /></dd><dt>Assigned to</dt><dd>{ticket.assignedUserName ?? (ticket.assignedUserId ? 'Name unavailable' : 'Unassigned')}</dd><dt>Assigned team</dt><dd>{assignedTeam?.name ?? (ticket.assignedTeamId ? 'Team name unavailable' : 'Not assigned')}{assignedTeam && !assignedTeam.isActive && <small>Inactive team</small>}{ticket.assignedTeamId && <small className="mono">{ticket.assignedTeamId}</small>}</dd></dl></aside></div>
      <MlPanel ticket={ticket} />
      <AttachmentsPanel id={ticket.id} revision={ticket.revision} />
      <AiPanel workspace={workspace} revision={ticket.revision} loading={context.loading} error={context.error} retry={context.reload} />
      <section className="panel"><SectionHeader title="Conversation" description={`${ticket.comments.length} ${ticket.comments.length === 1 ? 'comment' : 'comments'} on this ticket`} />{ticket.comments.length ? <div className="comment-list">{[...ticket.comments].sort((a, b) => a.createdAt.localeCompare(b.createdAt)).map(comment => <article className="comment" key={comment.id}><MessageSquare size={18} aria-hidden="true" /><div><strong>Ticket comment</strong><time dateTime={comment.createdAt}>{date(comment.createdAt, true)}</time><p>{comment.message}</p><details className="technical-details"><summary>Author details</summary><small className="muted mono">{comment.authorId}</small></details></div></article>)}</div> : <p className="section-empty">No comments yet. Add the first observation to help the investigation.</p>}</section>
    </>}
  </div>;
}

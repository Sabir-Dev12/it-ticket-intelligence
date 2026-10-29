import { ArrowRight, Bot, History, LoaderCircle, MessageSquarePlus, RefreshCw, Send, ShieldCheck } from 'lucide-react';
import { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { assistantApi } from '../api/assistant';
import { ApiError } from '../api/client';
import { useWorkspace } from '../app/Workspace';
import { Button, PageHeader, date } from '../components/ui';
import { useResource } from '../hooks/useResource';
import { ConversationTurn } from './assistant/ConversationTurn';
import { validId } from './ticket-details/model';
import { ArticleAssistant } from './assistant/ArticleAssistant';

function errorText(error?: Error) {
  if (error instanceof ApiError && (error.status === 401 || error.status === 403)) return 'Your account cannot access this conversation or ticket. Choose a ticket within your assigned scope.';
  return error instanceof ApiError ? error.validation || error.message : error?.message ?? 'The assistant could not complete this request.';
}
function TicketAssistant() {
  const { getAccessToken, hasSession, sessionVersion, revision } = useWorkspace();
  const [params, setParams] = useSearchParams();
  const selected = params.get('conversation');
  const ticketId = params.get('ticketId') ?? undefined;
  const token = getAccessToken();
  const [page, setPage] = useState(1);
  const [draft, setDraft] = useState('');
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [sending, setSending] = useState<{ id: string; content: string } | null>(null);
  const creation = useRef<{ id: string; question: string } | null>(null);
  const operation = useRef<AbortController | null>(null);
  const input = useRef<HTMLTextAreaElement>(null);
  const history = useResource(useCallback(async (signal: AbortSignal) => hasSession ? { sessionVersion, ...await assistantApi.list(token, page, signal) } : null, [hasSession, token, sessionVersion, page]), true);
  const detail = useResource(useCallback(async (signal: AbortSignal) => {
    void revision;
    if (!hasSession || !selected) return null;
    if (!validId(selected)) throw new Error('This conversation link is invalid. Start a new conversation.');
    return { sessionVersion, ...await assistantApi.get(token, selected, signal) };
  }, [hasSession, token, selected, sessionVersion, revision]), true);
  const conversation = detail.data?.sessionVersion === sessionVersion && detail.data.id === selected ? detail.data : null;
  const conversations = history.data?.sessionVersion === sessionVersion ? history.data : null;
  const activeTurn = conversation?.turns.some(turn => turn.state === 'Pending') ?? false;
  useEffect(() => {
    operation.current?.abort(); operation.current = null; setPending(false); setSending(null); setDraft(''); setPage(1); setError(null); creation.current = null;
    return () => { operation.current?.abort(); };
  }, [sessionVersion]);
  useEffect(() => {
    if (!hasSession || !selected || detail.loading || (!pending && !activeTurn)) return;
    const timer = setTimeout(detail.reload, 2000);
    return () => clearTimeout(timer);
  }, [hasSession, selected, pending, activeTurn, detail.loading, conversation, detail.reload]);
  function newConversation() { setParams(ticketId ? { ticketId } : {}); setError(null); setSending(null); creation.current = null; setDraft(''); input.current?.focus(); }
  async function submit(event: React.FormEvent) {
    event.preventDefault();
    const content = draft.trim();
    if (!content || content.length > 2000 || !hasSession || operation.current || activeTurn) return;
    const controller = new AbortController(); operation.current = controller;
    const requestId = sending?.content === content ? sending.id : crypto.randomUUID(); setPending(true); setError(null); setSending({ id: requestId, content });
    try {
      let id = selected;
      if (!id) {
        if (!creation.current || creation.current.question !== content) creation.current = { id: crypto.randomUUID(), question: content };
        const created = await assistantApi.create(token, creation.current.id, content, ticketId && validId(ticketId) ? ticketId : undefined, controller.signal);
        if (controller.signal.aborted) return;
        id = created.id; setParams({ conversation: id }); setPage(1); history.reload();
      }
      await assistantApi.send(token, id, requestId, content, controller.signal);
      if (!controller.signal.aborted) { setDraft(''); setSending(null); detail.reload(); history.reload(); }
    } catch (failure) {
      if (!controller.signal.aborted) setError(errorText(failure instanceof Error ? failure : undefined));
    } finally {
      if (operation.current === controller) { operation.current = null; setPending(false); detail.reload(); history.reload(); }
    }
  }
  const busy = pending || activeTurn;
  return <div className="assistant-page"><PageHeader eyebrow="AI ASSISTANT" title="Investigate with context." description="Ask about a ticket. Review the evidence, then decide what happens next."><Button onClick={newConversation} disabled={busy}><MessageSquarePlus size={17} aria-hidden="true" />New conversation</Button></PageHeader>
    {!hasSession ? <section className="panel assistant-session"><ShieldCheck size={30} aria-hidden="true" /><h2>Connect your private session</h2><p>Your conversation history belongs to your user account. Connect an authorized session to ask a question and retrieve saved conversations.</p><Link className="button primary" to="/settings">Open session settings<ArrowRight size={16} aria-hidden="true" /></Link></section> : <div className="assistant-workspace">
      <aside className="panel conversation-history" aria-label="Conversation history"><div className="section-header"><h2><History size={18} aria-hidden="true" />History</h2><Button aria-label="Refresh conversation history" onClick={history.reload} loading={history.loading}><RefreshCw size={15} /></Button></div>
        {history.error && <div className="inline-error" role="alert"><p>{errorText(history.error)}</p><Button onClick={history.reload}>Retry history</Button></div>}
        {history.loading && !conversations && <p className="section-empty" role="status">Loading saved conversations…</p>}
        {conversations && (conversations.items.length ? <ul>{conversations.items.map(item => <li key={item.id}><button className={selected === item.id ? 'selected' : ''} aria-pressed={selected === item.id} disabled={busy} onClick={() => { setParams({ conversation: item.id }); setError(null); setSending(null); setDraft(''); }}><span>{item.ticketReference}</span><strong>{item.title}</strong><small>{date(item.updatedAt, true)}</small></button></li>)}</ul> : <p className="section-empty">No conversations yet. Your first question starts the history.</p>)}
        {conversations && <div className="panel-footer"><span>Page {page}</span><div className="pagination"><Button disabled={page === 1 || busy} onClick={() => setPage(value => value - 1)} aria-label="Previous conversations">Previous</Button><Button disabled={!conversations.hasMore || busy} onClick={() => setPage(value => value + 1)} aria-label="Next conversations">Next</Button></div></div>}
      </aside>
      <section className="panel assistant-chat" aria-label="AI conversation"><header className="chat-header"><span className="assistant-symbol"><Bot size={23} aria-hidden="true" /></span><div><h2>{conversation ? `Conversation · ${conversation.ticketReference}` : 'Ticket investigation'}</h2><p>{conversation ? 'Saved history and current ticket evidence' : 'One ticket per conversation. Follow-up questions keep the same context.'}</p></div>{conversation && <Link className="text-link" to={`/tickets/${conversation.ticketId}`}>View ticket<ArrowRight size={15} aria-hidden="true" /></Link>}</header>
        <div className="chat-messages">
          {detail.loading && selected && !conversation && <p className="section-empty" role="status">Loading conversation…</p>}
          {detail.error && <div className="inline-error" role="alert"><p>{errorText(detail.error)}</p><Button onClick={detail.reload}>Reload conversation</Button></div>}
          {!selected && <div className="chat-welcome"><span className="eyebrow">START WITH A TICKET</span><h2>What needs investigating?</h2><p>The assistant reads the ticket and its history, checks ML predictions, searches the knowledge base and returns a proposal with sources.</p><button className="example-question" onClick={() => { setDraft('Why is ticket #1045 failing?'); input.current?.focus(); }}>Why is ticket #1045 failing?<ArrowRight size={16} aria-hidden="true" /></button></div>}
          {conversation?.turns.map((turn, index) => <ConversationTurn key={turn.id} turn={turn} revision={conversation.ticketRevision} latest={index === conversation.turns.length - 1} />)}
          {sending && !conversation?.turns.some(turn => turn.id === sending.id) && <div className="chat-unsent"><strong>Your question{pending ? ' · Sending' : ' · Not confirmed'}</strong><p>{sending.content}</p></div>}
        </div>
        <form className="chat-composer" onSubmit={event => void submit(event)}><label htmlFor="assistant-question">{selected ? 'Ask a follow-up question' : 'Your question'}</label><textarea ref={input} id="assistant-question" rows={3} maxLength={2000} value={draft} disabled={busy} onChange={event => setDraft(event.target.value)} placeholder={selected ? 'What should I check first?' : 'Why is ticket #1045 failing?'} aria-describedby="assistant-composer-help" />
          {error && <div className="inline-error" role="alert"><p>{error}</p>{selected && <Button type="button" onClick={detail.reload}>Check saved response</Button>}</div>}
          <div className="composer-actions"><p id="assistant-composer-help">{draft.length} / 2,000 · Corrective actions remain proposals.</p><Button type="submit" className="primary" loading={pending} disabled={busy || !draft.trim() || Boolean(selected && !conversation)}><Send size={16} aria-hidden="true" />Send question</Button></div>
          {busy && <div className="chat-loading" role="status"><LoaderCircle size={16} className="spin" aria-hidden="true" /><span>Preparing your response. This may take a few moments.</span></div>}
        </form><div className="chat-safety-note"><ShieldCheck size={16} aria-hidden="true" /><p>Read-only investigation. The assistant cannot assign, resolve or change a ticket from this conversation.</p></div>
      </section>
    </div>}
  </div>;
}
export default function Assistant(){const [params]=useSearchParams();const documentId=params.get('documentId');return documentId?<ArticleAssistant key={documentId} documentId={documentId}/>:<TicketAssistant/>;}

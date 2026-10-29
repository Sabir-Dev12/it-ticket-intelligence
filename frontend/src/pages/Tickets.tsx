import { ChevronLeft, ChevronRight, RefreshCw, Search } from 'lucide-react';
import { useCallback, useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { api } from '../api/client';
import { statuses } from '../api/contracts';
import type { TicketPriority, TicketStatus } from '../api/contracts';
import { useWorkspace } from '../app/Workspace';
import { useResource } from '../hooks/useResource';
import { Button, EmptyState, PageHeader, ResourceState, TicketTable } from '../components/ui';

export default function Tickets() {
  const [params, setParams] = useSearchParams();
  const search = params.get('search') ?? '';
  const [draft, setDraft] = useState(search);
  useEffect(() => setDraft(search), [search]);
  const status = statuses.includes(params.get('status') as TicketStatus) ? params.get('status') as TicketStatus : undefined;
  const priority = params.get('priority') === 'Critical' ? 'Critical' as TicketPriority : undefined;
  const page = Math.min(1000000, Math.max(1, Math.floor(Number(params.get('page'))) || 1));
  const { revision, user } = useWorkspace();
  const resource = useResource(useCallback((signal: AbortSignal) => { void revision; return api.tickets({ page, pageSize: 15, search, status, priority }, signal); }, [page, search, status, priority, revision]));
  function filter(key: string, value?: string) { const next = new URLSearchParams(params); next.delete('page'); if (value) next.set(key, value); else next.delete(key); setParams(next); }
  return <><PageHeader eyebrow="TICKET MANAGEMENT" title="Every issue. In focus." description="Find the work that needs attention, then explore the full context.">{user?.role !== 'Agent' && <Link className="button primary" to="/tickets/new">Create ticket</Link>}<Button onClick={resource.reload} loading={resource.loading}><RefreshCw size={16} aria-hidden="true" />Refresh</Button></PageHeader><section className="panel"><div className="ticket-toolbar"><div className="filter-tabs" aria-label="Filter by status"><button className={!status ? 'selected' : ''} aria-pressed={!status} onClick={() => filter('status')}>All tickets</button>{statuses.map(value => <button key={value} aria-pressed={status === value} className={status === value ? 'selected' : ''} onClick={() => filter('status', value)}>{value === 'InProgress' ? 'In progress' : value}</button>)}</div><form className="search-field" role="search" onSubmit={event => { event.preventDefault(); filter('search', draft.trim()); }}><Search size={17} aria-hidden="true" /><input aria-label="Search tickets" placeholder="Search tickets…" value={draft} maxLength={200} onChange={event => setDraft(event.target.value)} /><button className="icon-button" type="submit" aria-label="Apply ticket search"><ChevronRight size={17} /></button></form></div>{priority && <div className="filter-summary">Filtered to Critical priority <button className="text-link" onClick={() => filter('priority')}>Clear priority filter</button></div>}<ResourceState {...resource} retry={resource.reload} />{resource.data && <>{resource.data.items.length ? <TicketTable tickets={resource.data.items} /> : <EmptyState title="No tickets match this view" description="Try another search or status filter. Create a ticket or try another filter." />}<div className="panel-footer"><span>{resource.data.totalCount.toLocaleString()} results · Page {page}</span><div className="pagination"><Button aria-label="Previous page" disabled={page <= 1} onClick={() => { const next = new URLSearchParams(params); next.set('page', String(page - 1)); setParams(next); }}><ChevronLeft size={16} /></Button><Button aria-label="Next page" disabled={page * 15 >= resource.data.totalCount} onClick={() => { const next = new URLSearchParams(params); next.set('page', String(page + 1)); setParams(next); }}><ChevronRight size={16} /></Button></div></div></>}</section></>;
}

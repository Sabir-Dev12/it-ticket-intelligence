import { ArrowRight, ArrowUpRight, BookOpen, CircleAlert, Layers3, RefreshCw, ShieldCheck } from 'lucide-react';
import { useCallback } from 'react';
import { Link } from 'react-router-dom';
import { loadOverview } from '../api/client';
import type { TicketSummary } from '../api/contracts';
import { useResource } from '../hooks/useResource';
import { useWorkspace } from '../app/Workspace';
import { Button, PageHeader, ResourceState, SectionHeader, TicketTable } from '../components/ui';

export function categoryCounts(tickets: TicketSummary[]) {
  const counts = new Map<string, number>();
  tickets.forEach(ticket => { const name = ticket.category ?? 'Unclassified'; counts.set(name, (counts.get(name) ?? 0) + 1); });
  return [...counts.entries()].sort((a, b) => b[1] - a[1]);
}
export function CategoryBars({ tickets }: { tickets: TicketSummary[] }) {
  const counts = categoryCounts(tickets);
  return <div className="category-bars" role="img" aria-label={counts.length ? `Categories in the loaded sample: ${counts.map(([name, count]) => `${name}: ${count}`).join(', ')}` : 'No categories available yet'}>{counts.length ? counts.map(([name, count]) => <div className="category-row" key={name}><div><span>{name}</span><strong>{count}</strong></div><div className="bar-track"><span style={{ width: `${100 * count / tickets.length}%` }} /></div></div>) : <p>Category distribution will appear when tickets are available.</p>}</div>;
}
export default function Dashboard() {
  const { revision } = useWorkspace();
  const resource = useResource(useCallback((signal: AbortSignal) => { void revision; return loadOverview(signal); }, [revision]));
  const { data } = resource;
  return <><PageHeader eyebrow="OPERATIONS OVERVIEW" title="A clearer view of support." description="Your ticket queue, knowledge and intelligence in one workspace."><Button onClick={resource.reload} loading={resource.loading}><RefreshCw size={16} aria-hidden="true" />Refresh</Button><Link className="button primary" to="/tickets">Open ticket queue<ArrowRight size={16} aria-hidden="true" /></Link></PageHeader>
    <ResourceState {...resource} retry={resource.reload} />
    {data && <><div className="overview-band"><section className="lead-metric"><div className="eyebrow"><span className="status-marker" />SUPPORT WORKLOAD</div><div className="metric-value">{data.active.toLocaleString()}<span>active tickets</span></div><p>Open and in progress. Ready for your team’s next move.</p><Link className="text-link" to="/tickets?status=Open">Review open tickets<ArrowUpRight size={16} aria-hidden="true" /></Link><Layers3 className="metric-watermark" aria-hidden="true" /></section><div className="secondary-metrics"><Link to="/tickets?priority=Critical" className="secondary-metric"><span className="metric-icon critical-icon"><CircleAlert size={20} aria-hidden="true" /></span><div><span>Critical priority</span><strong>{data.critical.toLocaleString()}</strong><small>Across all current tickets</small></div><ArrowUpRight size={17} aria-hidden="true" /></Link><Link to="/tickets" className="secondary-metric"><span className="metric-icon"><Layers3 size={20} aria-hidden="true" /></span><div><span>Total tickets</span><strong>{data.recent.totalCount.toLocaleString()}</strong><small>Archived tickets excluded</small></div><ArrowUpRight size={17} aria-hidden="true" /></Link></div></div>
    <div className="dashboard-grid"><section className="panel queue-panel"><SectionHeader title="Latest tickets" description="Recently created tickets in your workspace." to="/tickets" /><TicketTable tickets={data.recent.items.slice(0, 6)} /><div className="panel-footer"><span>{Math.min(data.recent.items.length, 6)} of {data.recent.totalCount.toLocaleString()} tickets</span><Link className="text-link" to="/tickets">Browse queue<ArrowRight size={15} aria-hidden="true" /></Link></div></section><section className="panel category-panel"><SectionHeader title="Where work lands" description={`Categories · latest ${data.recent.items.length} tickets`} /><CategoryBars tickets={data.recent.items} /><p className="panel-caption">Assigned categories, not model predictions.</p></section></div>
    <div className="support-band"><section className="knowledge-summary"><BookOpen size={25} aria-hidden="true" /><div><h2>Knowledge, ready to reference.</h2><p>{data.knowledge.toLocaleString()} documents in your knowledge base.</p></div><Link className="button" to="/knowledge">Browse knowledge<ArrowRight size={15} aria-hidden="true" /></Link></section><section className="oversight-note"><ShieldCheck size={22} aria-hidden="true" /><div><strong>Built for human review</strong><p>AI suggestions never change ticket assignments automatically.</p></div></section></div><div className="data-footnote"><span className="live-dot" />API snapshot · {data.loadedAt.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' })} · Refresh to resync</div></>}
  </>;
}

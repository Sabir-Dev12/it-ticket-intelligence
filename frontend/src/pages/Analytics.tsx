import { useCallback } from 'react';
import { api } from '../api/client';
import { useWorkspace } from '../app/Workspace';
import { useResource } from '../hooks/useResource';
import { EmptyState, FeatureNotice, PageHeader, ResourceState, SectionHeader } from '../components/ui';
import { CategoryBars } from './Dashboard';

export default function Analytics() {
  const { revision } = useWorkspace();
  const resource = useResource(useCallback((signal: AbortSignal) => { void revision; return api.tickets({ pageSize: 100 }, signal); }, [revision]));
  const items = resource.data?.items ?? [];
  return <><PageHeader eyebrow="SUPPORT ANALYTICS" title="Understand the workload." description="A transparent snapshot of the tickets currently available to this view." /><FeatureNotice>These charts describe the latest {items.length} loaded tickets, from {resource.data?.totalCount ?? '…'} total. Resolution times and AI success rates are not available in this view.</FeatureNotice><ResourceState {...resource} retry={resource.reload} />{resource.data && (items.length ? <div className="analytics-grid"><section className="panel"><SectionHeader title="Category distribution" description="Recorded categories in this sample" /><CategoryBars tickets={items} /></section><section className="panel"><SectionHeader title="Priority mix" description="Recorded priorities, including unclassified tickets" /><div className="priority-breakdown">{['Critical', 'High', 'Medium', 'Low', 'Unclassified'].map(priority => { const count = items.filter(ticket => (ticket.priority ?? 'Unclassified') === priority).length; return <div key={priority}><span>{priority}</span><strong>{count}</strong><span>{Math.round(100 * count / items.length)}%</span></div>; })}</div><p className="panel-caption">Percentages are rounded. This is a sample, not a historical trend.</p></section><section className="panel analytics-wide"><SectionHeader title="Status at a glance" description="Current state of the loaded sample" /><div className="status-breakdown">{['Open', 'InProgress', 'Resolved', 'Closed'].map(status => <div key={status}><span>{status === 'InProgress' ? 'In progress' : status}</span><strong>{items.filter(ticket => ticket.status === status).length}</strong></div>)}</div></section></div> : <EmptyState title="Analytics need a little history" description="Create tickets through the API to begin seeing the category, priority and status distributions." />)}</>;
}

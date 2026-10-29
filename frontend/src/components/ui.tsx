import { AlertCircle, ArrowRight, Inbox, LoaderCircle, RefreshCw } from 'lucide-react';
import { Link } from 'react-router-dom';
import type { ReactNode } from 'react';
import type { TicketPriority, TicketStatus, TicketSummary } from '../api/contracts';

export function PageHeader({ eyebrow, title, description, children }: { eyebrow: string; title: string; description: string; children?: ReactNode }) {
  return <header className="page-header"><div><div className="eyebrow">{eyebrow}</div><h1 tabIndex={-1}>{title}</h1><p>{description}</p></div><div className="header-actions">{children}</div></header>;
}
export function Button({ children, loading, ...props }: React.ButtonHTMLAttributes<HTMLButtonElement> & { loading?: boolean }) {
  return <button {...props} className={`button ${props.className ?? ''}`} disabled={props.disabled || loading} aria-busy={loading || undefined}>{loading && <LoaderCircle className="spin" size={16} aria-hidden="true" />}{children}</button>;
}
export function ResourceState({ loading, error, retry }: { loading: boolean; error?: Error; retry: () => void }) {
  if (error) return <div className="resource-state error-state" role="alert"><AlertCircle size={28} aria-hidden="true" /><h2>Unable to load this workspace</h2><p>{error.message}</p><Button onClick={retry}><RefreshCw size={16} aria-hidden="true" />Retry request</Button></div>;
  if (loading) return <div className="resource-state" role="status"><LoaderCircle className="spin" size={28} aria-hidden="true" /><h2>Loading your workspace</h2><p>Fetching the latest records from the API.</p><div className="skeleton" /><div className="skeleton short" /></div>;
  return null;
}
export function EmptyState({ title = 'No tickets to display', description = 'Tickets will appear here after they are created through the API.' }: { title?: string; description?: string }) {
  return <div className="resource-state"><Inbox size={30} aria-hidden="true" /><h2>{title}</h2><p>{description}</p></div>;
}
export function StatusBadge({ status }: { status: TicketStatus }) {
  return <span className={`badge status-${status}`}><span className="status-marker" aria-hidden="true" />{status === 'InProgress' ? 'In progress' : status}</span>;
}
export function PriorityBadge({ priority }: { priority: TicketPriority | null }) {
  return <span className={`priority priority-${priority ?? 'None'}`}><span aria-hidden="true">{priority === 'Critical' ? '!!' : priority === 'High' ? '↑' : priority === 'Medium' ? '=' : priority === 'Low' ? '↓' : '·'}</span>{priority ?? 'Unclassified'}</span>;
}
export function date(value: string, full = false) {
  const parsed = new Date(value);
  if (!Number.isFinite(parsed.getTime())) return 'Unknown date';
  return new Intl.DateTimeFormat('en-GB', full ? { dateStyle: 'medium', timeStyle: 'short' } : { month: 'short', day: 'numeric' }).format(parsed);
}
export function TicketTable({ tickets }: { tickets: TicketSummary[] }) {
  if (!tickets.length) return <EmptyState />;
  return <div className="ticket-table"><table><caption className="sr-only">Tickets, current status, priority and last update</caption><thead><tr><th scope="col">Ticket</th><th scope="col" className="desktop-cell">Category</th><th scope="col">Status</th><th scope="col" className="tablet-cell">Priority</th><th scope="col" className="desktop-cell">Updated</th></tr></thead><tbody>{tickets.map(ticket => <tr key={ticket.id}><th scope="row"><Link className="ticket-link" to={`/tickets/${ticket.id}`}><span className="reference">{ticket.reference}</span><span>{ticket.title}</span></Link><span className="mobile-priority"><PriorityBadge priority={ticket.priority} /></span></th><td className="desktop-cell">{ticket.category ?? 'Unclassified'}</td><td><StatusBadge status={ticket.status} /></td><td className="tablet-cell"><PriorityBadge priority={ticket.priority} /></td><td className="desktop-cell"><time dateTime={ticket.updatedAt}>{date(ticket.updatedAt)}</time></td></tr>)}</tbody></table></div>;
}
export function SectionHeader({ title, description, to, link = 'View all' }: { title: string; description?: string; to?: string; link?: string }) {
  return <div className="section-header"><div><h2>{title}</h2>{description && <p>{description}</p>}</div>{to && <Link className="text-link" to={to}>{link}<ArrowRight size={15} aria-hidden="true" /></Link>}</div>;
}
export function FeatureNotice({ children }: { children: ReactNode }) { return <div className="notice"><AlertCircle size={17} aria-hidden="true" /><p>{children}</p></div>; }

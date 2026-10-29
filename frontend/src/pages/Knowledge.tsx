import { ArrowLeft, ArrowUpRight, BookOpen, Search } from 'lucide-react';
import { useCallback, useEffect, useState } from 'react';
import { Link, useParams, useSearchParams, useLocation } from 'react-router-dom';
import { useWorkspace } from '../app/Workspace';
import { api } from '../api/client';
import { useResource } from '../hooks/useResource';
import { Button, date, EmptyState, PageHeader, ResourceState } from '../components/ui';
import { validId } from './TicketDetail';

export default function Knowledge() {
  const {user}=useWorkspace();
  const [params, setParams] = useSearchParams();
  const search = params.get('search') ?? '';
  const page = Math.min(1000000, Math.max(1, Math.floor(Number(params.get('page'))) || 1));
  const [draft, setDraft] = useState(search);
  useEffect(() => setDraft(search), [search]);
  const resource = useResource(useCallback((signal: AbortSignal) => api.knowledge({ page, pageSize: 12, search }, signal), [search, page]));
  return <><PageHeader eyebrow="KNOWLEDGE BASE" title="Answers worth keeping." description="Procedures, troubleshooting guides and the context behind a resolution.">{user?.role === 'Admin' && <Link className="button primary" to="/knowledge/new">Add document</Link>}</PageHeader>{user?.role !== 'Admin' && <p className="field-help">Your administrator publishes documents here. Open a document to read it or ask AI about it.</p>}<form className="knowledge-search search-field" role="search" onSubmit={event => { event.preventDefault(); setParams(draft.trim() ? { search: draft.trim() } : {}); }}><Search size={20} aria-hidden="true" /><input aria-label="Search knowledge base" placeholder="Find a guide, procedure or article…" value={draft} maxLength={200} onChange={event => setDraft(event.target.value)} /><Button type="submit" className="primary">Search</Button></form><ResourceState {...resource} retry={resource.reload} />{resource.data && <><div className="section-header"><h2>Document library</h2><span className="muted">{resource.data.totalCount} documents</span></div>{resource.data.items.length ? <div className="knowledge-grid">{resource.data.items.map(document => <Link className="panel knowledge-card" to={`/knowledge/${document.id}`} key={document.id}><div className="knowledge-card-top"><span className="metric-icon"><BookOpen size={21} aria-hidden="true" /></span><ArrowUpRight size={18} aria-hidden="true" /></div><span className="eyebrow">{document.category} · {document.kind.replace(/([a-z])([A-Z])/g, '$1 $2')}</span><h3>{document.title}</h3><p>Version {document.version} · {document.format}</p><small>Updated {date(document.updatedAt)}</small></Link>)}</div> : <EmptyState title="Your knowledge library starts here" description="No documents match this view. Try another search. Administrators can publish documents using Add document." />}<div className="panel-footer"><span>Page {page}</span><div className="pagination"><Button disabled={page === 1} onClick={() => setParams({ search, page: String(page - 1) })}>Previous</Button><Button disabled={page * 12 >= resource.data.totalCount} onClick={() => setParams({ search, page: String(page + 1) })}>Next</Button></div></div></>}</>;
}
export function KnowledgeDetail() {
  const { id = '' } = useParams();
  const location=useLocation();
  const notice=typeof location.state?.knowledgeNotice==='string'?location.state.knowledgeNotice:null;
  const resource = useResource(useCallback((signal: AbortSignal) => validId(id) ? api.document(id, signal) : Promise.reject(new Error('This document link is invalid. Return to the knowledge base.')), [id]));
  const document = resource.data;
  return <><Link className="back-link" to="/knowledge"><ArrowLeft size={16} aria-hidden="true" />Back to knowledge</Link><PageHeader eyebrow="KNOWLEDGE DOCUMENT" title={document?.title ?? 'Document context'} description={document ? `${document.category} · Version ${document.version} · Updated ${date(document.updatedAt, true)}` : 'Loading the saved document.'}><Link className="button primary" to={`/ai-assistant?documentId=${id}`}>Ask AI</Link></PageHeader>{notice&&<p className="notice" role="status">{notice}</p>}<ResourceState {...resource} retry={resource.reload} />{document && <article className="panel document-panel"><div className="notice"><BookOpen size={18} aria-hidden="true" /><p>Source: <span className="mono">{document.source}</span></p></div><p className="document-content">{document.content}</p></article>}</>;
}

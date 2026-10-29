import { useCallback } from 'react';
import { ticketForms } from '../../api/ticketForms';
import { useResource } from '../../hooks/useResource';
import { Button, SectionHeader } from '../../components/ui';
import { Preview } from '../ticket-form/AttachmentFields';
export function AttachmentsPanel({ id, revision }: { id: string; revision: number }) {
  const resource = useResource(useCallback((signal: AbortSignal) => { void revision; return ticketForms.attachments(id, signal); }, [id, revision]));
  return <section className="panel"><SectionHeader title="Attachments" description="Saved evidence attached to this ticket." />{resource.loading && <p className="section-empty" role="status">Loading attachments…</p>}{resource.error && <div className="inline-error" role="alert"><p>Attachments could not be loaded. {resource.error.message}</p><Button onClick={resource.reload}>Retry attachments</Button></div>}{resource.data && <div className="saved-attachments">{resource.data.items.length ? <ul className="attachment-list">{resource.data.items.map(item => <li key={item.id}><strong>{item.fileName}</strong><p className="field-help">{(item.length / 1024).toFixed(1)} KiB · {item.contentType}</p><Preview saved={item} /></li>)}</ul> : <p className="section-empty">No attachments saved.</p>}</div>}</section>;
}

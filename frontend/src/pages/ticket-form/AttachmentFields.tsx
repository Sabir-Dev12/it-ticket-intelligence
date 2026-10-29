import { FileText, Paperclip, Trash2 } from 'lucide-react';
import { useEffect, useState } from 'react';
import type { Attachment } from '../../api/ticketForms';
import { attachmentBlob } from '../../api/ticketForms';
import { Button } from '../../components/ui';
export interface PendingFile { id: string; file: File }
export function Preview({ file, saved }: { file?: File; saved?: Attachment }) {
  const [blob, setBlob] = useState<Blob | undefined>(file), [url, setUrl] = useState(''), [text, setText] = useState('');
  const [error, setError] = useState(''), [loading, setLoading] = useState(false), [requested, setRequested] = useState(0);
  useEffect(() => {
    if (!requested || !saved) return;
    const abort = new AbortController(); setLoading(true); setError('');
    void attachmentBlob(saved.ticketId, saved.id, abort.signal).then(setBlob).catch(() => { if (!abort.signal.aborted) setError('Preview could not be loaded. Try downloading the original file.'); }).finally(() => { if (!abort.signal.aborted) setLoading(false); });
    return () => abort.abort();
  }, [requested, saved]);
  useEffect(() => {
    if (!blob) return;
    const next = URL.createObjectURL(blob); let active = true; setUrl(next);
    if (blob.type === 'text/plain') void blob.slice(0, 4096).text().then(value => { if (active) setText(value); });
    return () => { active = false; URL.revokeObjectURL(next); };
  }, [blob]);
  const name = saved?.fileName ?? file?.name ?? 'Attachment';
  const type = saved?.contentType ?? file?.type;
  return <div className="attachment-preview">{url && type?.startsWith('image/') && !error && <img src={url} alt={`Preview of ${name}`} onError={() => setError('This image could not be decoded. Remove it or export a valid image.')} />}{text && <pre aria-label={`Text preview of ${name}`}>{text}<span>{(blob?.size ?? 0) > 4096 ? '\nPreview limited to the first 4 KiB.' : ''}</span></pre>}{type === 'application/pdf' && <p className="field-help">PDF document. Download to review its contents.</p>}{saved && !blob && <Button type="button" loading={loading} onClick={() => setRequested(value => value + 1)}>Preview {name}</Button>}{error && <p className="error-text" role="alert">{error}</p>}{saved && <Button type="button" onClick={async () => { try { const data = await attachmentBlob(saved.ticketId, saved.id, new AbortController().signal); const address = URL.createObjectURL(data); const anchor = document.createElement('a'); anchor.href = address; anchor.download = name; anchor.click(); setTimeout(() => URL.revokeObjectURL(address), 1000); } catch { setError('Download failed. Check your session and retry.'); } }}>Download original</Button>}</div>;
}
export function AttachmentFields({ pending, saved, removed, onAdd, onRemovePending, onToggleSaved, error }: { pending: PendingFile[]; saved: Attachment[]; removed: string[]; onAdd: (files: File[]) => void; onRemovePending: (id: string) => void; onToggleSaved: (id: string) => void; error?: string }) {
  return <section className="attachment-fields" aria-labelledby="attachments-heading"><h2 id="attachments-heading"><Paperclip size={18} aria-hidden="true" />Attachments</h2><p className="field-help" id="attachments-help">PNG, JPEG, PDF or UTF-8 TXT. Up to 5 MiB per file, five files per ticket. Files are uploaded after ticket details are saved.</p><label className="file-picker" htmlFor="ticket-files">Choose files<input id="ticket-files" type="file" multiple accept=".png,.jpg,.jpeg,.pdf,.txt,image/png,image/jpeg,application/pdf,text/plain" aria-describedby="attachments-help attachments-error" onChange={e => { onAdd(Array.from(e.target.files ?? [])); e.target.value = ''; }} /></label>{error && <p className="error-text" role="alert" id="attachments-error">{error}</p>}
    <ul className="attachment-list">{saved.map(item => <li key={item.id}><div className="attachment-heading"><FileText size={18} aria-hidden="true" /><div><strong>{item.fileName}</strong><small>{(item.length / 1024).toFixed(1)} KiB · {removed.includes(item.id) ? 'Will be removed on save' : 'Saved attachment'}</small></div><Button type="button" onClick={() => onToggleSaved(item.id)} aria-label={`${removed.includes(item.id) ? 'Keep' : 'Remove'} ${item.fileName}`}>{removed.includes(item.id) ? 'Keep' : <Trash2 size={16} aria-hidden="true" />}</Button></div>{!removed.includes(item.id) && <Preview saved={item} />}</li>)}{pending.map(item => <li key={item.id}><div className="attachment-heading"><Paperclip size={18} aria-hidden="true" /><div><strong>{item.file.name}</strong><small>{(item.file.size / 1024).toFixed(1)} KiB · Selected, upload not confirmed</small></div><Button type="button" aria-label={`Remove ${item.file.name}`} onClick={() => onRemovePending(item.id)}><Trash2 size={16} aria-hidden="true" /></Button></div><Preview file={item.file} /></li>)}</ul>{!saved.length && !pending.length && <p className="field-help">No attachments selected.</p>}
  </section>;
}

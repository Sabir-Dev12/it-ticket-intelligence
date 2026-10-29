import { BrainCircuit, Save, ShieldCheck } from 'lucide-react';
import { useRef, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import type { TeamOption, Ticket } from '../../api/contracts';
import { ApiError } from '../../api/client';
import { ticketForms, type Attachment, type DraftSuggestions } from '../../api/ticketForms';
import { Button, SectionHeader } from '../../components/ui';
import { ConfirmDialog } from '../../components/ConfirmDialog';
import { AttachmentFields, type PendingFile } from './AttachmentFields';
import { TicketFormFields } from './TicketFormFields';
import { creatorSchema, emptyValues, fileError, ticketFormSchema, ticketTextSchema, type TicketFormValues } from './schema';
import { useUnsavedChanges } from './useUnsavedChanges';
import { useWorkspace } from '../../app/Workspace';

export function EditTicketForm({ ticket, teams, attachments, truncated, assignees = [], assigneesTruncated = false }: { ticket?: Ticket; teams: TeamOption[]; attachments: Attachment[]; truncated: boolean; assignees?: import("../../api/ticketForms").AssigneeOption[]; assigneesTruncated?: boolean }) {
  const navigate = useNavigate();
  const { user } = useWorkspace();
  const initial: TicketFormValues = ticket ? { title: ticket.title, description: ticket.description, category: ticket.category ?? '', priority: ticket.priority ?? '', assignedTeamId: ticket.assignedTeamId ?? '', assignedUserId: ticket.assignedUserId ?? '' } : emptyValues;
  const [values, setValues] = useState<TicketFormValues>(initial), [creator, setCreator] = useState(user?.id ?? '');
  const [errors, setErrors] = useState<Record<string, string>>({}), [error, setError] = useState(''), [fileIssue, setFileIssue] = useState('');
  const [pendingFiles, setPendingFiles] = useState<PendingFile[]>([]), [savedFiles, setSavedFiles] = useState(attachments), [removed, setRemoved] = useState<string[]>([]);
  const [saving, setSaving] = useState(false), [analyzing, setAnalyzing] = useState(false), [progress, setProgress] = useState('');
  const [suggestions, setSuggestions] = useState<{ result: DraftSuggestions; text: string } | null>(null);
  const [suggestionNotice, setSuggestionNotice] = useState(''), [conflict, setConflict] = useState(false), [unknown, setUnknown] = useState(false);
  const [confirmRemoval, setConfirmRemoval] = useState(false);
  const operation = useRef(false), core = useRef<Ticket | null>(null);
  const original = useRef(JSON.stringify(initial));
  const busy = saving || analyzing;
  const dirty = JSON.stringify(values) !== original.current || Boolean(!ticket && creator) || pendingFiles.length > 0 || removed.length > 0 || saving;
  const { blocker, allow } = useUnsavedChanges(dirty);
  const textKey = JSON.stringify({ title: values.title.trim(), description: values.description.trim() });
  const stale = suggestions?.text !== textKey;
  function validate(textOnly = false) {
    const result = (textOnly ? ticketTextSchema : ticketFormSchema).safeParse(values);
    const messages: Record<string, string> = {};
    if (!result.success) for (const issue of result.error.issues) messages[String(issue.path[0])] ??= issue.message;
    if (!textOnly && !ticket && !creatorSchema.safeParse(creator.trim()).success) messages.creator = 'Enter an existing active user ID.';
    setErrors(messages);
    if (Object.keys(messages).length) { setError('Review the highlighted fields before continuing.'); requestAnimationFrame(() => document.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus()); return null; }
    return result.success ? result.data : null;
  }
  async function analyze() {
    if (operation.current || !validate(true)) return;
    const restoreFocus = document.activeElement?.id === 'analyze-draft';
    operation.current = true; setAnalyzing(true); setError(''); setSuggestionNotice('');
    try { setSuggestions({ result: await ticketForms.suggest({ title: values.title.trim(), description: values.description.trim() }), text: textKey }); }
    catch (failure) { setError(failure instanceof ApiError ? failure.validation ?? failure.message : 'Model suggestions could not be loaded. You can still fill and save the form.'); }
    finally {
      operation.current = false; setAnalyzing(false);
      if (restoreFocus) requestAnimationFrame(() => { if (document.activeElement === document.body) document.getElementById('analyze-draft')?.focus(); });
    }
  }
  function useSuggestions() {
    if (!suggestions || stale) return;
    const result = suggestions.result;
    const normalize = (name: string) => name.replace(/\s+team$/i, '').trim().toLowerCase();
    const matches = result.team.team && !result.team.needsHumanReview ? teams.filter(t => normalize(t.name) === normalize(result.team.team!)) : [];
    setValues({ ...values, category: result.category.category, priority: result.priority.predictedPriority, ...(matches.length === 1 ? { assignedTeamId: matches[0].id } : {}) });
    setSuggestionNotice(matches.length === 1 ? 'Suggestions copied into the form. Review them, then save to confirm.' : 'Category and priority copied into the form. Choose the team manually; the model label has no unique available team or needs human review.');
    setErrors({});
  }
  function addFiles(files: File[]) {
    const bad = files.map(fileError).find(Boolean); if (bad) { setFileIssue(bad); return; }
    if (savedFiles.length - removed.length + pendingFiles.length + files.length > 5) { setFileIssue('A ticket can contain at most five attachments. Remove a file before adding another.'); return; }
    if (files.some((f, i) => [...pendingFiles.map(p => p.file), ...files.slice(0, i)].some(p => p.name === f.name && p.size === f.size && p.lastModified === f.lastModified))) { setFileIssue('This file is already selected. Choose each file once.'); return; }
    setFileIssue(''); setPendingFiles(old => [...old, ...files.map(file => ({ id: crypto.randomUUID(), file }))]);
  }
  async function save(event?: React.FormEvent, confirmed = false) {
    event?.preventDefault(); if (operation.current || conflict || unknown) return;
    const parsed = validate(); if (!parsed) return;
    if (removed.length && !confirmed) { setConfirmRemoval(true); return; }
    operation.current = true; setSaving(true); setError('');
    try {
      if (!core.current) {
        setProgress(ticket ? 'Saving ticket changes…' : 'Creating ticket…');
        core.current = ticket ? await ticketForms.update(ticket, parsed as TicketFormValues) : await ticketForms.create(parsed as TicketFormValues, creator.trim());
      }
      for (const id of removed) {
        setProgress('Removing an attachment…'); const response = await ticketForms.remove(core.current.id, id, core.current.revision); core.current = { ...core.current, revision: response.revision };
        setRemoved(old => old.filter(value => value !== id)); setSavedFiles(old => old.filter(value => value.id !== id));
      }
      for (const item of pendingFiles) {
        setProgress(`Uploading ${item.file.name}…`); const response = await ticketForms.upload(core.current.id, item.id, core.current.revision, item.file);
        core.current = { ...core.current, revision: response.revision }; setPendingFiles(old => old.filter(value => value.id !== item.id)); setSavedFiles(old => [...old.filter(value => value.id !== item.id), response.attachment]);
      }
      allow.current = true;
      navigate(`/tickets/${core.current.id}`, { replace: !ticket, state: { ticketNotice: `${core.current.reference} ${ticket ? 'updated' : 'created'} successfully. All selected attachments were saved.` } });
    } catch (failure) {
      if (failure instanceof ApiError && failure.status === 409) setConflict(true);
      if (!core.current && failure instanceof ApiError && ([0, 408].includes(failure.status) || failure.status >= 500)) setUnknown(true);
      const detail = failure instanceof ApiError ? failure.validation ?? failure.message : 'The save could not be completed.';
      setError(`${core.current ? `Ticket ${core.current.reference} was saved. Some attachment changes have not been confirmed. ` : ''}${detail}`);
    } finally { operation.current = false; setSaving(false); setProgress(''); }
  }
  const disabled = saving || Boolean(core.current) || unknown;
  return <><form className="panel ticket-editor" onSubmit={event => void save(event)} noValidate aria-busy={saving}><SectionHeader title={ticket ? `Edit ticket ${ticket.reference}` : 'Ticket details'} description={ticket ? 'If someone else changes this ticket, review the latest version before saving your draft.' : 'Capture the issue and choose its initial classification and team.'} />
    {error && <div className="inline-error" role="alert"><p>{error}</p>{core.current && <Link className="text-link" to={`/tickets/${core.current.id}`}>View saved ticket</Link>}</div>}
    {unknown && <p className="notice" role="alert">The save outcome is unknown. Check the ticket list before starting another request. This form will not resend the ticket automatically.</p>}
    {conflict && <p className="notice" role="alert">The server version changed or the action is no longer allowed. Your draft is preserved. Open the ticket to review its latest version before starting a new edit.</p>}
    <fieldset disabled={disabled} className="ticket-form-fields">
      {!ticket && !user && <div className="form-field"><label htmlFor="ticket-creator">Your user ID <span className="required-label">Required</span></label><input id="ticket-creator" value={creator} maxLength={36} aria-required="true" aria-invalid={Boolean(errors.creator)} aria-describedby="creator-help creator-error" spellCheck={false} onChange={e => setCreator(e.target.value)} /><p id="creator-help" className="field-help">Use your existing active user ID. This local workspace does not yet provide user sign-in.</p><p id="creator-error" className="error-text">{errors.creator}</p></div>}
      <TicketFormFields values={values} setValues={setValues} teams={teams} assignees={assignees} errors={errors} />{assigneesTruncated && <p className="notice">Only the first 200 active people are listed. Contact an administrator if the person is missing.</p>}{truncated && <p className="notice">Only the first 200 active teams are listed. Contact an administrator if your team is unavailable.</p>}
    </fieldset>
    {user?.role !== 'User' && <section className="draft-intelligence" aria-labelledby="draft-intelligence-heading"><div className="section-header"><div><h2 id="draft-intelligence-heading"><BrainCircuit size={19} aria-hidden="true" />Optional intelligence</h2><p>ML.NET predictions from the title and description. No ticket is created by this analysis.</p></div><Button id="analyze-draft" type="button" loading={analyzing} disabled={saving || Boolean(core.current) || unknown} onClick={() => void analyze()}><BrainCircuit size={17} aria-hidden="true" />Analyze with AI</Button></div>{suggestions && <><p className="field-help" role="status">Suggestions ready. Review them before saving.</p><dl className="suggestion-grid"><div><dt>Suggested category</dt><dd>{suggestions.result.category.category}<small>{Math.round(suggestions.result.category.confidence * 100)}% model confidence</small></dd></div><div><dt>Suggested priority</dt><dd>{suggestions.result.priority.predictedPriority}<small>{Math.round(suggestions.result.priority.confidence * 100)}% model confidence</small></dd></div><div><dt>Suggested team</dt><dd>{suggestions.result.team.team ?? 'Human review needed'}<small>{Math.round(suggestions.result.team.confidence * 100)}% model confidence</small></dd></div></dl>{stale && <p className="notice" role="status">The title or description changed. Analyze again before using these suggestions.</p>}<Button type="button" disabled={stale || busy || Boolean(core.current)} onClick={useSuggestions}>Use these suggestions</Button><p className="field-help"><ShieldCheck size={15} aria-hidden="true" />Confidence is not a guarantee. Suggestions only fill the form; you confirm them by saving.</p></>}{suggestionNotice && <div className="notice" role="status"><p>{suggestionNotice}</p><Button type="button" onClick={() => document.getElementById('ticket-classification')?.querySelector<HTMLButtonElement>('button[role="combobox"]')?.focus()}>Review classification</Button></div>}</section>}
    <fieldset disabled={saving || unknown || conflict}><AttachmentFields pending={pendingFiles} saved={savedFiles} removed={removed} onAdd={addFiles} onRemovePending={id => setPendingFiles(old => old.filter(f => f.id !== id))} onToggleSaved={id => setRemoved(old => old.includes(id) ? old.filter(value => value !== id) : [...old, id])} error={fileIssue} /></fieldset>
    <div className="ticket-editor-footer"><p className="field-help">{core.current ? 'Ticket details saved. Complete the remaining attachment changes.' : dirty ? 'You have unsaved changes.' : 'No unsaved changes.'}</p><div className="form-actions"><Button type="button" disabled={busy} onClick={() => navigate(ticket ? `/tickets/${ticket.id}` : '/tickets')}>Cancel</Button><Button type="submit" className="primary" loading={saving} disabled={busy || conflict || unknown}><Save size={17} aria-hidden="true" />{core.current ? 'Retry remaining attachments' : ticket ? 'Save changes' : 'Create ticket'}</Button></div></div>{progress && <p className="save-progress" role="status">{progress}</p>}
  </form>{confirmRemoval && <ConfirmDialog title="Remove saved attachments?" confirmLabel="Save and remove attachments" onCancel={() => setConfirmRemoval(false)} onConfirm={() => { setConfirmRemoval(false); void save(undefined, true); }}><p>Saving permanently removes these files from the ticket:</p><ul>{savedFiles.filter(file => removed.includes(file.id)).map(file => <li key={file.id}>{file.fileName}</li>)}</ul><p>Review the other form changes before confirming.</p></ConfirmDialog>}{blocker.state === 'blocked' && <ConfirmDialog title={saving ? 'Saving in progress' : 'Leave this form?'} confirmLabel="Discard unsaved changes" pending={saving} onConfirm={() => blocker.proceed()} onCancel={() => blocker.reset()}><p>{saving ? 'Wait for the server result before leaving this page.' : core.current ? 'Ticket details were already saved. Leaving discards only pending form and attachment selections; it does not undo saved changes.' : 'Your unsaved changes and selected files will be discarded.'}</p></ConfirmDialog>}</>;
}

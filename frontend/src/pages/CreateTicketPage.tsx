import { ArrowLeft } from 'lucide-react';
import { useCallback } from 'react';
import { Link, useParams } from 'react-router-dom';
import { api } from '../api/client';
import { ticketForms } from '../api/ticketForms';
import { PageHeader, ResourceState } from '../components/ui';
import { useResource } from '../hooks/useResource';
import { EditTicketForm } from './ticket-form/EditTicketForm';
import { validId, mayAnalyze } from './ticket-details/model';
export default function CreateTicketPage({ edit = false }: { edit?: boolean }) {
  const { id } = useParams();
  const resource = useResource(useCallback(async (signal: AbortSignal) => {
    if (edit && (!id || !validId(id))) throw new Error('Choose a valid ticket to edit.');
    const [options, ticket, attachments] = await Promise.all([ticketForms.options(signal), edit ? api.ticket(id!, signal) : undefined, edit ? ticketForms.attachments(id!, signal) : undefined]);
    return { options, ticket, attachments };
  }, [edit, id]));
  const data = resource.data;
  return <div className="ticket-form-page"><Link className="back-link" to={edit ? `/tickets/${id}` : '/tickets'}><ArrowLeft size={16} aria-hidden="true" />{edit ? 'Back to ticket' : 'Back to tickets'}</Link><PageHeader eyebrow="TICKET MANAGEMENT" title={edit ? 'Update the context.' : 'Make the issue clear.'} description={edit ? 'Review the ticket details and save a confirmed change.' : 'Describe the issue, add evidence and choose who should investigate.'} />{!data && <ResourceState {...resource} retry={resource.reload} />}{data && (edit && data.ticket && !mayAnalyze(data.ticket) ? <div className="notice" role="alert">Only open or in-progress, unarchived tickets can be edited.</div> : <EditTicketForm key={edit ? id : 'create'} ticket={data.ticket} teams={data.options.teams} assignees={data.options.assignees ?? []} assigneesTruncated={data.options.assigneesTruncated} truncated={data.options.teamsTruncated} attachments={data.attachments?.items ?? []} />)}</div>;
}

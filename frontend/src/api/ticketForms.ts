import { ApiError, request } from './client';
import { sessionHeaders } from '../app/auth';
import type { TeamOption, Ticket, TicketCategory, TicketPriority } from './contracts';
import type { TicketFormValues } from '../pages/ticket-form/schema';
export interface AssigneeOption { id: string; name: string; role: string }
export interface Attachment { id: string; ticketId: string; fileName: string; contentType: string; length: number; sha256: string; createdAt: string }
export interface AttachmentList { items: Attachment[]; revision: number }
export interface DraftSuggestions {
  category: { category: TicketCategory; confidence: number; modelVersion: string };
  priority: { predictedPriority: TicketPriority; confidence: number; modelVersion: string };
  team: { team: string | null; confidence: number; needsHumanReview: boolean; threshold: number; modelVersion: string };
}
function fields(values: TicketFormValues) { return { title: values.title, description: values.description, category: values.category || null, priority: values.priority || null }; }
export const ticketForms = {
  options: (signal?: AbortSignal) => request<{ teams: TeamOption[]; teamsTruncated: boolean; assignees?: AssigneeOption[]; assigneesTruncated?: boolean }>('/tickets/form-options', signal),
  create: (values: TicketFormValues, createdById: string, signal?: AbortSignal) => request<Ticket>('/tickets', signal, 15000, { method: 'POST', body: { ...fields(values), createdById, assignedTeamId: values.assignedTeamId || null, assignedUserId: values.assignedUserId || null } }),
  update: (ticket: Ticket, values: TicketFormValues, signal?: AbortSignal) => request<Ticket>(`/tickets/${ticket.id}`, signal, 15000, { method: 'PUT', body: { ...fields(values), revision: ticket.revision, ...(values.assignedUserId !== (ticket.assignedUserId ?? '') ? { assignee: { userId: values.assignedUserId || null } } : {}), ...(values.assignedTeamId !== (ticket.assignedTeamId ?? '') ? { assignment: { teamId: values.assignedTeamId || null } } : {}) } }),
  attachments: (id: string, signal?: AbortSignal) => request<AttachmentList>(`/tickets/${id}/attachments`, signal),
  remove: (id: string, attachmentId: string, revision: number, signal?: AbortSignal) => request<{ revision: number }>(`/tickets/${id}/attachments/${attachmentId}?revision=${revision}`, signal, 15000, { method: 'DELETE' }),
  upload: async (id: string, attachmentId: string, revision: number, file: File, signal?: AbortSignal) => {
    const body = new FormData(); body.set('attachmentId', attachmentId); body.set('revision', String(revision)); body.set('file', file);
    return request<{ attachment: Attachment; revision: number; created: boolean }>(`/tickets/${id}/attachments`, signal, 60000, { method: 'POST', body });
  },
  suggest: async (values: Pick<TicketFormValues, 'title' | 'description'>, signal?: AbortSignal): Promise<DraftSuggestions> => {
    const body = { title: values.title, description: values.description };
    const [category, team] = await Promise.all([
      request<DraftSuggestions['category']>('/predictions/category', signal, 60000, { method: 'POST', body }),
      request<DraftSuggestions['team']>('/predictions/team', signal, 60000, { method: 'POST', body }),
    ]);
    const priority = await request<DraftSuggestions['priority']>('/predictions/priority', signal, 60000, { method: 'POST', body: { ...body, category: category.category } });
    return { category, priority, team };
  },
};
export async function attachmentBlob(ticketId: string, id: string, signal: AbortSignal) {
  const response = await fetch(`/api/tickets/${ticketId}/attachments/${id}`, { signal: AbortSignal.any([signal, AbortSignal.timeout(15000)]), headers: { Accept: 'application/octet-stream', ...sessionHeaders() } });
  if (!response.ok) throw new ApiError(response.status);
  return response.blob();
}

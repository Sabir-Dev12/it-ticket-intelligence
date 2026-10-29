import type { AgentAnalysis, IntelligenceResult, KnowledgeDocument, KnowledgeSummary, Page, Ticket, TicketCategory, TicketPriority, TicketStatus, TicketSummary, TicketWorkspace } from './contracts';
import { sessionHeaders } from '../app/auth';

export class ApiError extends Error {
  constructor(public readonly status: number, public readonly traceId?: string, public readonly validation?: string) {
    super(status === 404 ? 'This record could not be found.' : status === 401 || status === 403
      ? 'Access was refused. Check the API access configuration.' : status === 0
      ? 'The API is unreachable. Check that the backend is running, then retry.'
      : status === 408 ? 'The request timed out. Refresh to check its outcome before trying again.'
      : status === 409 ? 'This ticket changed or the action is no longer allowed. Refresh and review its current version before submitting again.'
      : status === 400 ? 'Check the entered values and try again.'
      : 'The API could not complete this request. Please retry.');
  }
}
export async function request<T>(path: string, signal?: AbortSignal, timeoutMs = 15000, options?: { method?: 'GET' | 'POST' | 'PATCH' | 'PUT' | 'DELETE'; body?: object | FormData; token?: string }): Promise<T> {
  const timeout = AbortSignal.timeout(timeoutMs);
  try {
    const response = await fetch(`/api${path}`, { signal: signal ? AbortSignal.any([signal, timeout]) : timeout,
      method: options?.method ?? 'GET', body: options?.body instanceof FormData ? options.body : options?.body ? JSON.stringify(options.body) : undefined,
      headers: { Accept: 'application/json', ...sessionHeaders(), ...(options?.body && !(options.body instanceof FormData) ? { 'Content-Type': 'application/json' } : {}), ...(options?.token ? { Authorization: `Bearer ${options.token}` } : {}) }, credentials: 'same-origin' });
    if (!response.ok) {
      if (response.status === 401 && path !== '/auth/login' && typeof window !== 'undefined') window.dispatchEvent(new Event('iti:unauthorized'));
      const problem: unknown = await response.json().catch(() => null);
      const trace = problem && typeof problem === 'object' && 'traceId' in problem && typeof problem.traceId === 'string' ? problem.traceId : undefined;
      let validation: string | undefined;
      if (response.status === 400 && problem && typeof problem === 'object' && 'errors' in problem && problem.errors && typeof problem.errors === 'object') {
        const messages = Object.values(problem.errors).flat().filter((value): value is string => typeof value === 'string' && value.length <= 300);
        validation = messages.slice(0, 3).join(' ');
      }
      throw new ApiError(response.status, trace, validation);
    }
    if (!response.headers.get('content-type')?.includes('application/json')) throw new ApiError(502);
    return await response.json() as T;
  } catch (error) {
    if (signal?.aborted) throw error;
    if (error instanceof ApiError) throw error;
    throw new ApiError(timeout.aborted ? 408 : 0);
  }
}
export interface TicketFilters { page?: number; pageSize?: number; search?: string; status?: TicketStatus; priority?: TicketPriority; category?: TicketCategory }
function query(values: object) {
  const params = new URLSearchParams();
  Object.entries(values).forEach(([key, value]) => { if (value !== undefined && value !== '') params.set(key, String(value)); });
  return params.toString();
}
export const api = {
  tickets: (filters: TicketFilters = {}, signal?: AbortSignal) => request<Page<TicketSummary>>(`/tickets?${query(filters)}`, signal),
  ticket: (id: string, signal?: AbortSignal) => request<Ticket>(`/tickets/${encodeURIComponent(id)}`, signal),
  workspace: (id: string, signal?: AbortSignal) => request<TicketWorkspace>(`/tickets/${encodeURIComponent(id)}/workspace`, signal),
  analyze: (id: string, signal?: AbortSignal) => request<IntelligenceResult>(`/tickets/${encodeURIComponent(id)}/analyze`, signal, 60000, { method: 'POST' }),
  investigate: (id: string, signal?: AbortSignal) => request<AgentAnalysis>(`/tickets/${encodeURIComponent(id)}/investigate`, signal, 195000, { method: 'POST' }),
  assign: (id: string, teamId: string | null, revision: number, signal?: AbortSignal) => request<Ticket>(`/tickets/${encodeURIComponent(id)}/assignment`, signal, 15000, { method: 'PATCH', body: { teamId, revision } }),
  changeStatus: (id: string, status: TicketStatus, resolution: string | null, revision: number, signal?: AbortSignal) => request<Ticket>(`/tickets/${encodeURIComponent(id)}/status`, signal, 15000, { method: 'PATCH', body: { status, resolution, revision } }),
  comment: (id: string, authorId: string, message: string, revision: number, signal?: AbortSignal) => request<{ comment: import('./contracts').TicketComment; ticketRevision: number }>(`/tickets/${encodeURIComponent(id)}/comments`, signal, 15000, { method: 'POST', body: { authorId, message, revision } }),
  knowledge: (filters: { page?: number; pageSize?: number; search?: string } = {}, signal?: AbortSignal) => request<Page<KnowledgeSummary>>(`/knowledge?${query(filters)}`, signal),
  document: (id: string, signal?: AbortSignal) => request<KnowledgeDocument>(`/knowledge/${encodeURIComponent(id)}`, signal),
};
export async function loadOverview(signal?: AbortSignal) {
  const [recent, open, inProgress, critical, knowledge] = await Promise.all([
    api.tickets({ pageSize: 50 }, signal), api.tickets({ status: 'Open', pageSize: 1 }, signal),
    api.tickets({ status: 'InProgress', pageSize: 1 }, signal), api.tickets({ priority: 'Critical', pageSize: 1 }, signal),
    api.knowledge({ pageSize: 1 }, signal),
  ]);
  return { recent, active: open.totalCount + inProgress.totalCount, critical: critical.totalCount,
    knowledge: knowledge.totalCount, loadedAt: new Date() };
}

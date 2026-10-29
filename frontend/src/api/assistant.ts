import { request } from './client';
import type { AssistantConversation, AssistantConversationPage } from './contracts';
export interface ArticleAnswer {
  answer: { summary: string; suggestedSteps: string[]; insufficientContext: boolean };
  citations: { documentId: string; chunkId: string; documentVersion: number; title: string; source: string; contentHash: string }[];
  provider: string; model: string; attempts: number; needsHumanReview: boolean;
}
export const assistantApi = {
  article: (documentId: string, question: string, signal?: AbortSignal) => request<ArticleAnswer>(`/assistant/knowledge/${encodeURIComponent(documentId)}/answers`,signal,100000,{method:'POST',body:{question}}),
  list: (token: string, page: number, signal?: AbortSignal) => request<AssistantConversationPage>(`/assistant/conversations?page=${page}`, signal, 15000, { token }),
  get: (token: string, id: string, signal?: AbortSignal) => request<AssistantConversation>(`/assistant/conversations/${encodeURIComponent(id)}`, signal, 15000, { token }),
  create: (token: string, requestId: string, question: string, ticketId?: string, signal?: AbortSignal) => request<AssistantConversation>('/assistant/conversations', signal, 15000, { token, method: 'POST', body: { requestId, question, ...(ticketId ? { ticketId } : {}) } }),
  send: (token: string, id: string, requestId: string, content: string, signal?: AbortSignal) => request<AssistantConversation>(`/assistant/conversations/${encodeURIComponent(id)}/messages`, signal, 195000, { token, method: 'POST', body: { requestId, content } }),
};

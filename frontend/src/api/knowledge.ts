import { z } from 'zod';
import { request } from './client';
import type { KnowledgeDocument } from './contracts';
import { categories } from '../pages/ticket-form/schema';
export const documentKinds = ['Document','Article','TroubleshootingGuide','Faq','Procedure'] as const;
export const documentFormats = ['PlainText','Markdown'] as const;
const text = (max: number) => z.string().max(max).trim().min(1, 'This field is required.').refine(v => !v.includes('\0'), 'Remove NUL characters.');
export const knowledgeSchema = z.object({title:text(160),content:text(100000),source:text(512),category:z.enum(categories),kind:z.enum(documentKinds),format:z.enum(documentFormats)});
export type KnowledgeValues = z.infer<typeof knowledgeSchema>;
export const knowledgeApi = {
  create: (values: KnowledgeValues, createdById: string) => request<KnowledgeDocument>('/knowledge', undefined, 30000, {method:'POST',body:{...values,createdById}}),
  index: (id: string) => request<{id:string;state:string}>(`/rag/documents/${id}/index`, undefined, 15000, {method:'POST'}),
};
export async function importKnowledgeText(file: File) {
  if (!/\.(txt|md)$/i.test(file.name) || file.size < 1 || file.size > 400000) throw new Error('Choose a UTF-8 TXT or Markdown file up to 400 KB. PDF and Word import are not available.');
  const content = new TextDecoder('utf-8', {fatal:true}).decode(await file.arrayBuffer());
  if (!content.trim() || content.length > 100000 || content.includes('\0')) throw new Error('Use a non-empty text document of at most 100,000 characters without NUL characters.');
  return {content,format:/\.md$/i.test(file.name)?'Markdown' as const:'PlainText' as const};
}

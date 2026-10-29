import { z } from 'zod';
export const categories = ['Billing', 'Network', 'Hardware', 'Software', 'Account', 'Security', 'Database', 'Application', 'Other'] as const;
export const priorities = ['Low', 'Medium', 'High', 'Critical'] as const;
const controlFree = (value: string) => !/[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]/.test(value);
export const ticketTextSchema = z.object({
  title: z.string().max(160, 'Use at most 160 characters.').trim().min(1, 'Enter a title.').refine(controlFree, 'Remove control characters.'),
  description: z.string().max(8000, 'Use at most 8,000 characters.').trim().min(1, 'Describe the issue.').refine(controlFree, 'Remove control characters.'),
});
const nonEmptyUuid = z.uuid().refine(id => id !== '00000000-0000-0000-0000-000000000000', 'Choose a valid identifier.');
export const ticketFormSchema = ticketTextSchema.extend({
  category: z.union([z.enum(categories), z.literal('')]), priority: z.union([z.enum(priorities), z.literal('')]),
  assignedTeamId: z.union([nonEmptyUuid, z.literal('')]), assignedUserId: z.union([nonEmptyUuid, z.literal('')]).default(''),
}).refine(value => Boolean(value.category) === Boolean(value.priority), { path: ['category'], message: 'Set both category and priority, or leave both unclassified.' });
export const creatorSchema = nonEmptyUuid;
export type TicketFormValues = z.infer<typeof ticketFormSchema>;
export const emptyValues: TicketFormValues = { title: '', description: '', category: '', priority: '', assignedTeamId: '', assignedUserId: '' };
export const maximumFileBytes = 5 * 1024 * 1024;
export const fileSchema = z.object({ name: z.string().min(1).max(180).refine(name => !/[\x00-\x1F\x7F/\\:<>"|?*]/.test(name), 'Use a plain filename.'),
  size: z.number().int().min(1).max(maximumFileBytes), type: z.enum(['image/png', 'image/jpeg', 'application/pdf', 'text/plain']),
}).refine(file => ({ 'image/png': ['png'], 'image/jpeg': ['jpg', 'jpeg'], 'application/pdf': ['pdf'], 'text/plain': ['txt'] })[file.type].includes(file.name.split('.').at(-1)?.toLowerCase() ?? ''), { message: 'File extension and content type must match.' });
export function fileError(file: Pick<File, 'name' | 'size' | 'type'>) { return fileSchema.safeParse(file).success ? null : 'Use PNG, JPEG, PDF or UTF-8 TXT files of 1 byte to 5 MiB, with a plain filename of at most 180 characters.'; }

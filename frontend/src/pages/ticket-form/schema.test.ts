import { describe, expect, it } from 'vitest';
import { creatorSchema, emptyValues, fileError, ticketFormSchema } from './schema';
describe('ticket form boundaries', () => {
  it('accepts an optional assignee and rejects malformed identities', () => {
    const values = { ...emptyValues, title: 'Issue', description: 'Description' };
    expect(ticketFormSchema.safeParse({ ...values, assignedUserId: '' }).success).toBe(true);
    expect(ticketFormSchema.safeParse({ ...values, assignedUserId: '10000000-0000-4000-8000-000000000001' }).success).toBe(true);
    for (const id of ['someone', '00000000-0000-0000-0000-000000000000'])
      expect(ticketFormSchema.safeParse({ ...values, assignedUserId: id }).success).toBe(false);
  });
  it('requires non-blank bounded text and a classification pair', () => {
    expect(ticketFormSchema.safeParse(emptyValues).success).toBe(false);
    expect(ticketFormSchema.safeParse({ ...emptyValues, title: 'A'.repeat(161), description: 'A' }).success).toBe(false);
    expect(ticketFormSchema.safeParse({ ...emptyValues, title: 'Issue', description: 'A'.repeat(8001) }).success).toBe(false);
    expect(ticketFormSchema.safeParse({ ...emptyValues, title: 'Issue', description: 'Description', category: 'Database' }).success).toBe(false);
    expect(ticketFormSchema.parse({ ...emptyValues, title: ' Issue ', description: ' Description ' }).title).toBe('Issue');
  });
  it('rejects invalid enum values, team IDs and missing author identities', () => {
    expect(ticketFormSchema.safeParse({ ...emptyValues, title: 'Issue', description: 'Description', category: 'Unknown', priority: 'High' }).success).toBe(false);
    expect(ticketFormSchema.safeParse({ ...emptyValues, title: 'Issue', description: 'Description', assignedTeamId: '00000000-0000-0000-0000-000000000000' }).success).toBe(false);
    expect(creatorSchema.safeParse('').success).toBe(false);
  });
  it('checks extension, MIME, size and filename without interpreting HTML', () => {
    expect(fileError({ name: 'image.svg', type: 'image/svg+xml', size: 100 })).not.toBeNull();
    expect(fileError({ name: '../log.txt', type: 'text/plain', size: 100 })).not.toBeNull();
    expect(fileError({ name: 'log.txt', type: 'text/plain', size: 5242881 })).not.toBeNull();
    expect(fileError({ name: 'image.png', type: 'text/plain', size: 100 })).not.toBeNull();
    expect(fileError({ name: 'log.txt', type: 'text/plain', size: 5242880 })).toBeNull();
    expect(ticketFormSchema.parse({ ...emptyValues, title: '<script>literal text</script>', description: '<img onerror=alert(1)>' }).description).toContain('<img');
  });
});

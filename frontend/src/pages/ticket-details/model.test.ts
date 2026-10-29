import { describe, expect, it } from 'vitest';
import type { Ticket, TicketPrediction } from '../../api/contracts';
import { latestPredictions, mayAnalyze, nextStatuses, validId } from './model';
describe('ticket detail policies', () => {
  it('offers only domain-supported status transitions', () => {
    expect(nextStatuses('Open')).toEqual(['InProgress']);
    expect(nextStatuses('InProgress')).toEqual(['Resolved']);
    expect(nextStatuses('Resolved')).toEqual(['InProgress', 'Closed']);
    expect(nextStatuses('Closed')).toEqual([]);
  });
  it('never permits analysis on archived or terminal tickets', () => {
    expect(mayAnalyze({ status: 'Open', isArchived: false } as Ticket)).toBe(true);
    expect(mayAnalyze({ status: 'Resolved', isArchived: false } as Ticket)).toBe(false);
    expect(mayAnalyze({ status: 'Open', isArchived: true } as Ticket)).toBe(false);
  });
  it('does not mix predictions from different batches to fill a missing output', () => {
    const rows = latestPredictions([
      { id: 'a', type: 'Category', createdAt: '2026-10-27', analysisId: 'new' },
      { id: 'b', type: 'Priority', createdAt: '2026-10-26', analysisId: 'old' },
    ] as TicketPrediction[]);
    expect(rows[0].prediction?.analysisId).toBe('new');
    expect(rows[1].prediction).toBeUndefined();
  });
  it('supports independent legacy predictions without selecting an unrelated batch', () => {
    const rows = latestPredictions([{ id: 'a', type: 'Category', createdAt: '2026-10-27', analysisId: null }] as TicketPrediction[]);
    expect(rows[0].prediction?.id).toBe('a'); expect(rows[2].prediction).toBeUndefined();
  });
  it('rejects malformed and empty identifiers', () => {
    expect(validId('00000000-0000-0000-0000-000000000000')).toBe(false);
    expect(validId('not-a-ticket')).toBe(false);
    expect(validId('10000000-0000-4000-8000-000000000001')).toBe(true);
  });
});

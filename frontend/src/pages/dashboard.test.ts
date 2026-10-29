import { describe, expect, it } from 'vitest';
import { categoryCounts } from './Dashboard';
import type { TicketSummary } from '../api/contracts';
describe('dashboard sample distribution', () => {
  it('retains unclassified records in the denominator and totals', () => {
    const records = [{ category: 'Network' }, { category: null }, { category: 'Network' }] as TicketSummary[];
    expect(categoryCounts(records)).toEqual([['Network', 2], ['Unclassified', 1]]);
    expect(categoryCounts(records).reduce((sum, [, count]) => sum + count, 0)).toBe(records.length);
  });
  it('keeps an empty sample empty', () => expect(categoryCounts([])).toEqual([]));
});

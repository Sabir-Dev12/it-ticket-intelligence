import { afterEach, describe, expect, it, vi } from 'vitest';
import { api, ApiError, loadOverview, request } from './client';

afterEach(() => vi.unstubAllGlobals());
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'content-type': 'application/json' } });
describe('typed read API client', () => {
  it('encodes filters and omits absent values', async () => {
    const fetcher = vi.fn().mockResolvedValue(json({ items: [], totalCount: 0, page: 1, pageSize: 15 }));
    vi.stubGlobal('fetch', fetcher);
    await api.tickets({ search: 'SQL & VPN', status: 'Open', page: 2 });
    const url = new URL(fetcher.mock.calls[0][0], 'http://local');
    expect(url.searchParams.get('search')).toBe('SQL & VPN');
    expect(url.searchParams.get('page')).toBe('2');
    expect(url.searchParams.has('priority')).toBe(false);
  });
  it('keeps the trace id but never surfaces untrusted server details', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json({ detail: 'database password sensitive', traceId: 'trace-123' }, 503)));
    await expect(request('/tickets')).rejects.toMatchObject({ status: 503, traceId: 'trace-123' });
    await expect(request('/tickets')).rejects.not.toThrow('password');
  });
  it('rejects a successful HTML fallback instead of rendering it as data', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('<html>fallback</html>')));
    await expect(request('/tickets')).rejects.toMatchObject({ status: 502 });
  });
  it('translates unreachable and timeout failures into actionable errors', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('secret infrastructure host')));
    await expect(request('/tickets')).rejects.toMatchObject({ status: 0 });
    vi.stubGlobal('fetch', vi.fn((_url, options: RequestInit) => new Promise((_resolve, reject) => options.signal?.addEventListener('abort', () => reject(options.signal?.reason)))));
    await expect(request('/tickets', undefined, 10)).rejects.toMatchObject({ status: 408 });
  });
  it('preserves consumer cancellation', async () => {
    const controller = new AbortController(); controller.abort();
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(controller.signal.reason));
    await expect(request('/tickets', controller.signal)).rejects.toHaveProperty('name', 'AbortError');
  });
  it('uses server totals for KPIs rather than extrapolating the recent page', async () => {
    vi.stubGlobal('fetch', vi.fn(async (path: string) => {
      const query = new URL(path, 'http://local');
      const totalCount = path.startsWith('/api/knowledge') ? 18 : query.searchParams.get('status') === 'Open' ? 120 : query.searchParams.get('status') === 'InProgress' ? 30 : query.searchParams.has('priority') ? 7 : 250;
      return json({ items: [], totalCount, page: 1, pageSize: 1 });
    }));
    const result = await loadOverview();
    expect(result).toMatchObject({ active: 150, critical: 7, knowledge: 18, recent: { totalCount: 250 } });
  });
  it('provides controlled missing-record and access errors', () => {
    expect(new ApiError(404).message).toContain('not be found');
    expect(new ApiError(403).message).toContain('Access was refused');
  });
  it('sends explicit write methods, nullable assignment and the captured revision', async () => {
    const fetcher = vi.fn(async () => json({ id: 'ticket-id' })); vi.stubGlobal('fetch', fetcher);
    await api.assign('ticket-id', null, 7);
    expect(fetcher.mock.calls[0]).toBeDefined();
    const options = (fetcher.mock.calls as unknown[][])[0][1] as RequestInit;
    expect(options.method).toBe('PATCH');
    expect(JSON.parse(options.body as string)).toEqual({ teamId: null, revision: 7 });
    expect(options.headers).toMatchObject({ 'Content-Type': 'application/json' });
  });
  it('does not retry a conflicted mutation', async () => {
    const fetcher = vi.fn(async () => json({ title: 'Conflict' }, 409)); vi.stubGlobal('fetch', fetcher);
    await expect(api.comment('ticket-id', 'author-id', 'Observed timeout', 3)).rejects.toMatchObject({ status: 409 });
    expect(fetcher).toHaveBeenCalledOnce();
  });
});

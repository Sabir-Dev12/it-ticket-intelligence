import { afterEach, describe, expect, it, vi } from 'vitest';
import { assistantApi } from './assistant';
afterEach(() => vi.unstubAllGlobals());
describe('private assistant API client', () => {
  it('sends identity only in Authorization and keeps the client request id', async () => {
    const fetcher = vi.fn(async () => new Response('{}', { headers: { 'content-type': 'application/json' } })); vi.stubGlobal('fetch', fetcher);
    await assistantApi.send('private-token', 'conversation-id', 'request-id', 'Why is this failing?');
    const call = fetcher.mock.calls[0] as unknown as [string, RequestInit];
    expect(call[0]).not.toContain('private-token');
    expect(call[1].headers).toMatchObject({ Authorization: 'Bearer private-token' });
    expect(JSON.parse(call[1].body as string)).toEqual({ requestId: 'request-id', content: 'Why is this failing?' });
  });
  it('does not retry failed messages automatically', async () => {
    const fetcher = vi.fn(async () => new Response('{}', { status: 503, headers: { 'content-type': 'application/json' } })); vi.stubGlobal('fetch', fetcher);
    await expect(assistantApi.send('token', 'c', 'request', 'Why?')).rejects.toHaveProperty('status', 503);
    expect(fetcher).toHaveBeenCalledOnce();
  });
});

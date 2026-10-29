import { describe, expect, it, vi } from 'vitest';
import { bindRealtime } from './client';
import type { ConnectionPort } from './client';
import type { Ticket, TicketEvent } from '../api/contracts';

function fixture() {
  const events = new Map<string, (event: TicketEvent) => void>();
  let reconnect = () => {}; let reconnecting = () => {}; let close = () => {};
  const port: ConnectionPort = { on: (name, callback) => { events.set(name, callback); },
    onreconnected: callback => { reconnect = callback; }, onreconnecting: callback => { reconnecting = callback; }, onclose: callback => { close = callback; },
    start: vi.fn().mockResolvedValue(undefined), stop: vi.fn().mockResolvedValue(undefined),
    invoke: vi.fn().mockResolvedValue({} as Ticket),
  };
  const onEvent = vi.fn(); const onState = vi.fn();
  const client = bindRealtime(port, onEvent, onState);
  return { port, client, events, onEvent, onState, reconnect: () => reconnect(), reconnecting: () => reconnecting(), close: () => close() };
}
describe('ticket event lifecycle', () => {
  it('listens to all seven contracts and deduplicates by event id, not revision', () => {
    const test = fixture(); expect(test.events.size).toBe(7);
    const event = { eventId: 'a', ticketRevision: 1 } as TicketEvent;
    test.events.get('PredictionCompleted')!(event); test.events.get('PredictionCompleted')!(event);
    test.events.get('AIAnalysisCompleted')!({ ...event, eventId: 'b' });
    expect(test.onEvent).toHaveBeenCalledTimes(2);
  });
  it('rejoins selected tickets and invalidates snapshots after reconnect', async () => {
    const test = fixture(); await test.client.start(); await test.client.join('ticket-a');
    test.onEvent.mockClear(); test.reconnecting(); expect(test.onState).toHaveBeenLastCalledWith('reconnecting');
    test.reconnect(); await vi.waitFor(() => expect(test.onEvent).toHaveBeenCalledOnce());
    expect(test.port.invoke).toHaveBeenLastCalledWith('JoinTicket', 'ticket-a');
  });
  it('leaves groups and prevents rejoining abandoned tickets', async () => {
    const test = fixture(); await test.client.join('ticket-a'); await test.client.leave('ticket-a');
    vi.mocked(test.port.invoke).mockClear(); test.reconnect(); await Promise.resolve();
    expect(test.port.invoke).not.toHaveBeenCalled();
  });
  it('handles initial connection denial without exposing transport errors', async () => {
    const test = fixture(); vi.mocked(test.port.start).mockRejectedValue(new Error('secret-token-in-url'));
    await test.client.start(); expect(test.onState).toHaveBeenLastCalledWith('error');
  });
  it('does not revive a stopped in-flight connection', async () => {
    const test = fixture(); let resolve!: () => void;
    vi.mocked(test.port.start).mockImplementation(() => new Promise<void>(done => { resolve = done; }));
    const pending = test.client.start(); await test.client.stop(); resolve(); await pending;
    expect(test.onState).not.toHaveBeenCalledWith('connected');
    expect(test.onEvent).not.toHaveBeenCalled();
  });
});

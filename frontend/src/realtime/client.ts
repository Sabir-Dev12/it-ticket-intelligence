import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { ticketEvents } from '../api/contracts';
import type { Ticket, TicketEvent } from '../api/contracts';

export type LiveState = 'offline' | 'connecting' | 'connected' | 'reconnecting' | 'error';
export interface ConnectionPort {
  on(name: string, callback: (event: TicketEvent) => void): void;
  onreconnecting(callback: () => void): void;
  onreconnected(callback: () => void): void;
  onclose(callback: () => void): void;
  start(): Promise<void>; stop(): Promise<void>;
  invoke<T>(method: string, id: string): Promise<T>;
}
// Kept separate from React and from the transport for lifecycle tests.
export function bindRealtime(connection: ConnectionPort, onEvent: (event?: TicketEvent) => void, onState: (state: LiveState) => void) {
  const tickets = new Set<string>();
  const seen = new Set<string>();
  let stopped = false;
  let generation = 0;
  ticketEvents.forEach(name => connection.on(name, event => {
    if (stopped || seen.has(event.eventId)) return;
    seen.add(event.eventId);
    if (seen.size > 500) seen.delete(seen.values().next().value!);
    onEvent(event);
  }));
  connection.onreconnecting(() => { if (!stopped) onState('reconnecting'); });
  connection.onclose(() => { if (!stopped) onState('error'); });
  connection.onreconnected(() => {
    void (async () => {
      try {
        for (const id of tickets) await connection.invoke<Ticket>('JoinTicket', id);
        if (!stopped) { onState('connected'); onEvent(); }
      } catch { if (!stopped) onState('error'); }
    })();
  });
  return {
    async start() {
      const current = ++generation;
      onState('connecting');
      try {
        await connection.start();
        if (stopped || current !== generation) { await connection.stop(); return; }
        onState('connected'); onEvent();
      } catch { if (!stopped) onState('error'); }
    },
    async stop() { stopped = true; generation++; tickets.clear(); await connection.stop(); onState('offline'); },
    async join(id: string) {
      tickets.add(id);
      try { return await connection.invoke<Ticket>('JoinTicket', id); }
      catch (error) { tickets.delete(id); throw error; }
    },
    async leave(id: string) { tickets.delete(id); await connection.invoke('LeaveTicket', id); },
  };
}
export function createRealtime(token: string, onEvent: (event?: TicketEvent) => void, onState: (state: LiveState) => void) {
  const connection = new HubConnectionBuilder().withUrl('/hubs/tickets', { accessTokenFactory: () => token })
    .withAutomaticReconnect([0, 2000, 5000, 10000]).configureLogging(LogLevel.None).build();
  return bindRealtime(connection, onEvent, onState);
}

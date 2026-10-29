import type { Ticket, TicketPrediction, TicketStatus } from '../../api/contracts';
export function nextStatuses(status: TicketStatus): TicketStatus[] {
  return status === 'Open' ? ['InProgress'] : status === 'InProgress' ? ['Resolved'] : status === 'Resolved' ? ['InProgress', 'Closed'] : [];
}
export function latestPredictions(predictions: TicketPrediction[]) {
  const sorted = [...predictions].sort((a, b) => b.createdAt.localeCompare(a.createdAt) || b.id.localeCompare(a.id));
  const batch = sorted[0]?.analysisId;
  const candidates = batch ? sorted.filter(p => p.analysisId === batch) : sorted.filter(p => !p.analysisId);
  return (['Category', 'Priority', 'TeamRouting'] as const).map(type => ({ type, prediction: candidates.find(p => p.type === type) }));
}
export function mayAnalyze(ticket: Ticket) { return !ticket.isArchived && (ticket.status === 'Open' || ticket.status === 'InProgress'); }
export const validId = (id: string) => /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id) && !/^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(id);

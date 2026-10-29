// Mirrors Application DTOs. Enums are serialized as strings by the existing API.
export const statuses = ['Open', 'InProgress', 'Resolved', 'Closed'] as const;
export type TicketStatus = typeof statuses[number];
export type TicketPriority = 'Low' | 'Medium' | 'High' | 'Critical';
export type TicketCategory = 'Billing' | 'Network' | 'Hardware' | 'Software' | 'Account' | 'Security' | 'Database' | 'Application' | 'Other';
export interface Page<T> { items: T[]; totalCount: number; page: number; pageSize: number }
export interface TicketSummary {
  id: string; reference: string; title: string; description: string; createdById: string;
  status: TicketStatus; category: TicketCategory | null; priority: TicketPriority | null;
  assignedTeamId: string | null; assignedUserId?: string | null; assignedUserName?: string | null; revision: number; createdAt: string; updatedAt: string;
  isArchived: boolean; archivedAt: string | null; commentCount: number; predictionCount: number;
}
export interface TicketComment { id: string; ticketId: string; authorId: string; message: string; createdAt: string }
export interface TicketPrediction {
  id: string; type: 'Category' | 'Priority' | 'TeamRouting'; confidence: number; modelVersion: string;
  inputRevision: number; predictedCategory: TicketCategory | null; predictedPriority: TicketPriority | null;
  recommendedTeamId: string | null; recommendedTeamName: string | null; createdAt: string;
  analysisId: string | null; needsHumanReview: boolean; reviewThreshold: number | null;
}
export interface Ticket extends Omit<TicketSummary, 'commentCount' | 'predictionCount'> {
  resolution: string | null; resolvedAt: string | null; closedAt: string | null;
  comments: TicketComment[]; predictions: TicketPrediction[];
}
export interface KnowledgeSummary {
  id: string; title: string; source: string; category: TicketCategory;
  kind: 'Document' | 'Article' | 'TroubleshootingGuide' | 'Faq' | 'Procedure';
  format: 'PlainText' | 'Markdown'; version: number; updatedAt: string; chunkCount: number;
}
export interface KnowledgeDocument extends Omit<KnowledgeSummary, 'chunkCount'> {
  createdById: string | null; createdAt: string;
  content: string; cleanedContent: string | null; processingVersion: string | null; processedAt: string | null;
  chunks: { id: string; content: string; metadata: {
    documentId: string; documentVersion: number; title: string; source: string; category: TicketCategory;
    kind: KnowledgeSummary['kind']; format: KnowledgeSummary['format']; position: number;
    startOffset: number; endOffset: number; contentHash: string;
  } }[];
}
export const ticketEvents = ['TicketCreated', 'TicketUpdated', 'TicketAssigned', 'TicketStatusChanged', 'PredictionCompleted', 'AIAnalysisCompleted', 'CommentAdded'] as const;
export interface TicketEvent {
  eventId: string; name: typeof ticketEvents[number]; ticketId: string; ticketRevision: number;
  occurredAt: string; relatedId: string | null;
}
export interface TeamOption { id: string; name: string; isActive: boolean }
export interface IntelligenceResult {
  category: TicketCategory; categoryConfidence: number; priority: TicketPriority; priorityConfidence: number;
  recommendedTeam: string | null; teamConfidence: number; needsHumanReview: boolean;
}
export interface AgentAnalysis {
  runId: string; ticketRevision: number;
  facts: { id: string; revision: number; title: string; description: string; status: TicketStatus; isArchived: boolean; assignedTeamId: string | null };
  predictions: IntelligenceResult; summary: string; diagnosis: string; possibleCauses: string[];
  recommendedActions: { description: string; requiresHumanApproval: boolean }[];
  sources: { documentId: string; chunkId: string; documentVersion: number; title: string; source: string; contentHash: string }[];
  confidence: number; requiresHumanReview: boolean;
}
export interface TicketWorkspace {
  assignedTeam: TeamOption | null; assignableTeams: TeamOption[]; teamsTruncated: boolean;
  analysis: { result: AgentAnalysis; createdAt: string } | null;
}
export interface AssistantMessage { id: string; role: 'User' | 'Assistant'; content: string; createdAt: string }
export interface AssistantActivity { callId: string; name: string; state: 'Started' | 'Succeeded' | 'Denied' | 'Failed'; occurredAt: string }
export interface AssistantTurn {
  id: string; runId: string; state: 'Pending' | 'Completed' | 'Failed' | 'Interrupted';
  userMessage: AssistantMessage; assistantMessage: AssistantMessage | null; analysis: AgentAnalysis | null;
  errorCode: string | null; activity: AssistantActivity[]; createdAt: string; updatedAt: string;
}
export interface AssistantConversation {
  id: string; ticketId: string; ticketReference: string; ticketRevision: number; title: string; createdAt: string; updatedAt: string; turns: AssistantTurn[];
}
export interface AssistantConversationPage {
  items: { id: string; title: string; ticketId: string; ticketReference: string; updatedAt: string }[]; page: number; hasMore: boolean;
}

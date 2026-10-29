import { Bot, LoaderCircle, UserRound } from 'lucide-react';
import type { AssistantTurn as Turn } from '../../api/contracts';
import { date } from '../../components/ui';
import { AiPanel } from '../ticket-details/IntelligencePanels';

export const failureText: Record<string, string> = {
  Interrupted: 'The response was interrupted. Please ask your question again.',
  Timeout: 'The analysis took too long. Please try again.',
  TicketChanged: 'The ticket changed or is no longer open for investigation. Review its current state before asking again.',
  TicketUnavailable: 'The ticket could not be retrieved for this investigation.',
  DependencyUnavailable: 'The assistant could not produce a validated response. Please try again. No completed response was saved.',
};
export function ConversationTurn({ turn, revision, latest }: { turn: Turn; revision: number; latest: boolean }) {
  const waiting = turn.state === 'Pending';
  return <article className="chat-turn" aria-label="Conversation turn" aria-busy={waiting}>
    <div className="chat-user-message"><span className="chat-avatar"><UserRound size={18} aria-hidden="true" /></span><div><div className="chat-message-meta"><strong>You</strong><time dateTime={turn.userMessage.createdAt}>{date(turn.userMessage.createdAt, true)}</time></div><p>{turn.userMessage.content}</p></div></div>
    <div className="chat-assistant-message"><span className="chat-avatar assistant-avatar"><Bot size={18} aria-hidden="true" /></span><div className="chat-answer-content"><div className="chat-message-meta"><strong>AI Assistant</strong><span>{waiting ? 'Investigating' : turn.state === 'Completed' ? 'Proposal saved' : 'Response unavailable'}</span></div>
      {waiting && <div className="chat-loading" role="status"><LoaderCircle size={18} className="spin" aria-hidden="true" /><p>Investigating the current ticket. A response can take up to three minutes.</p></div>}
      {(turn.state === 'Failed' || turn.state === 'Interrupted') && <div className="inline-error" role="alert"><p>{failureText[turn.errorCode ?? ''] ?? failureText.DependencyUnavailable}</p></div>}
      {turn.analysis && <details className="chat-answer" open={latest}><summary>Structured response · {turn.analysis.facts.title}</summary><div className="chat-ai-result"><AiPanel workspace={{ assignedTeam: null, assignableTeams: [], teamsTruncated: false, analysis: { result: turn.analysis, createdAt: turn.updatedAt } }} revision={revision} loading={false} retry={() => {}} /></div></details>}
    </div></div>
  </article>;
}

import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import type { AssistantTurn } from '../../api/contracts';
import { ConversationTurn } from './ConversationTurn';

describe('conversation presentation', () => {
  for (const state of ['Pending', 'Failed', 'Completed'] as const) {
    it(`keeps internal tool activity out of the ${state} view`, () => {
      const now = '2026-10-29T20:00:00Z';
      const turn: AssistantTurn = {
        id: 'turn', runId: 'run', state,
        userMessage: { id: 'message', role: 'User', content: 'Explain IT-1000', createdAt: now },
        assistantMessage: null, analysis: null, errorCode: state === 'Failed' ? 'DependencyUnavailable' : null,
        activity: [{ callId: 'call', name: 'get_ticket_history', state: 'Succeeded', occurredAt: now }],
        createdAt: now, updatedAt: now,
      };
      const html = renderToStaticMarkup(createElement(ConversationTurn, { turn, revision: 1, latest: true }));
      expect(html).toContain('Explain IT-1000');
      expect(html).not.toContain('get_ticket_history');
      expect(html).not.toContain('tool-activity');
      expect(html).not.toContain('Read-only tool activity');
      if (state === 'Pending') expect(html).toContain('role="status"');
      if (state === 'Failed') {
        expect(html).toContain('role="alert"');
        expect(html).toContain('could not produce a validated response');
        expect(html).not.toContain('required service was unavailable');
      }
    });
  }
});

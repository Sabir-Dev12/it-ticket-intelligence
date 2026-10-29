import { chromium, expect } from '@playwright/test';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
const out = resolve('../artifacts/frontend');
await mkdir(out, { recursive: true });
const fixture = JSON.parse(await readFile(`${out}/ticket-details-fixture.json`, 'utf8'));
// Synthetic local verification profile; never used by the product.
const token = 'ticket-details-local-verification-0123456789abcdef';
const base = process.env.FRONTEND_URL || 'http://127.0.0.1:5173';
const api = process.env.API_URL || 'http://127.0.0.1:5080';
const ticket = await (await fetch(`${api}/api/tickets/${fixture.ticketId}`)).json();
const browser = await chromium.launch({ channel: 'chrome' });
const page = await browser.newPage({ viewport: { width: 1280, height: 980 } });
async function connect() {
  await page.goto(`${base}/settings`);
  await page.getByLabel('Session access token').fill(token);
  await page.getByRole('button', { name: 'Connect live updates' }).click();
  await expect(page.getByRole('button', { name: 'Disconnect', exact: true })).toBeEnabled();
  await page.locator('nav a[href="/ai-assistant"]').click();
}
async function capture(name, theme, width) {
  await page.setViewportSize({ width, height: width === 390 ? 844 : 980 });
  await page.evaluate(value => { document.documentElement.dataset.theme = value; }, theme);
  await page.addStyleTag({ content: '*{transition:none!important;animation:none!important}' });
  await page.evaluate(async () => { document.activeElement?.blur?.(); window.scrollTo({ top: 0, behavior: 'instant' }); await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))); });
  await page.mouse.move(0, 0);
  await page.screenshot({ path: `${out}/assistant-${name}-${theme}-${width}.png`, fullPage: true });
  if (width === 1280) {
    const html = await page.evaluate(() => {
      const root = document.documentElement.cloneNode(true);
      root.querySelectorAll('script, link').forEach(n => n.remove());
      root.querySelectorAll('style').forEach(n => { n.textContent = n.textContent.replace(/@font-face\s*\{[^}]*\}/g, ''); });
      return '<!doctype html>' + root.outerHTML;
    });
    if (html.includes(token)) throw new Error('Credential leaked into DOM snapshot.');
    await writeFile(`${out}/assistant-${name}-${theme}.html`, html);
  }
}
try {
  await connect();
  await capture('empty', 'light', 1280);
  await page.getByLabel('Your question', { exact: true }).fill(`Why is ticket #${ticket.reference.replace('IT-', '')} failing?`);
  await page.getByRole('button', { name: 'Send question', exact: true }).click();
  await expect(page.locator('.analysis-sources')).toBeVisible({ timeout: 60000 });
  await expect(page.locator('.chat-user-message')).toHaveCount(1);
  await page.locator('.tool-activity > summary').click();
  await expect(page.locator('.tool-activity')).toContainText('get_ticket_history');
  await expect(page.locator('.tool-activity')).toContainText('Read completed');
  const conversationId = new URL(page.url()).searchParams.get('conversation');
  const stored = await (await fetch(`${api}/api/assistant/conversations/${conversationId}`, { headers: { Authorization: `Bearer ${token}` } })).json();
  if (stored.turns[0]?.state !== 'Completed' || !stored.turns[0]?.analysis?.sources.length) throw new Error('No durable sourced response.');
  for (const theme of ['light', 'dark']) for (const width of [1280, 390]) await capture('completed', theme, width);
  await page.setViewportSize({ width: 1280, height: 980 });
  await page.getByLabel('Ask a follow-up question', { exact: true }).fill('What should I check first?');
  await page.getByRole('button', { name: 'Send question', exact: true }).click();
  await expect(page.locator('.chat-user-message')).toHaveCount(2, { timeout: 60000 });
  await expect(page.locator('.chat-turn').last().getByText('Proposal saved', { exact: true })).toBeVisible({ timeout: 60000 });
  await page.reload();
  await expect(page.getByRole('heading', { name: 'Connect your private session' })).toBeVisible();
  await connect();
  await page.locator('.conversation-history button.selected, .conversation-history li button').first().click();
  await expect(page.locator('.chat-user-message')).toHaveCount(2);
  const after = await (await fetch(`${api}/api/tickets/${fixture.ticketId}`)).json();
  if (after.revision !== ticket.revision || after.status !== ticket.status || after.assignedTeamId !== ticket.assignedTeamId) throw new Error('Assistant mutated the ticket.');
  const stores = await page.evaluate(() => JSON.stringify({ ...localStorage, ...sessionStorage }));
  if (stores.includes(token) || stores.includes(conversationId)) throw new Error('Private data persisted in browser storage.');
  await writeFile(`${out}/assistant-live-result.json`, JSON.stringify({ conversationId, ticketId: fixture.ticketId, ticketReference: ticket.reference, turns: 2, sources: stored.turns[0].analysis.sources.length, activity: stored.turns[0].activity.map(a => ({ name: a.name, state: a.state })), unchangedTicketRevision: after.revision, providers: 'simulated LLM and embeddings; real API, ML.NET and PostgreSQL' }, null, 2));
  console.log('Real API conversation passed: 2 persisted turns, verified sources, actual audit reads, reconnect history, no ticket mutation or browser credential persistence.');
} finally { await browser.close(); }

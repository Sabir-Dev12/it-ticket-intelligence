import { chromium, expect } from '@playwright/test';
import { readFile } from 'node:fs/promises';
const fixture = JSON.parse(await readFile(new URL('../../artifacts/frontend/ticket-details-fixture.json', import.meta.url), 'utf8'));
const token = process.env.FRONTEND_SMOKE_TOKEN;
if (!token) throw new Error('Configure a temporary authorized SignalR read profile.');
const base = process.env.FRONTEND_URL || 'http://127.0.0.1:5173';
const browser = await chromium.launch({ channel: 'chrome' });
try {
  const observer = await browser.newPage();
  await observer.goto(`${base}/settings`);
  await observer.getByLabel('Session access token').fill(token);
  await observer.getByRole('button', { name: 'Connect live updates' }).click();
  await expect(observer.getByText('Connected to ticket events', { exact: true })).toBeVisible();
  const received = new Set();
  let ack = false;
  // The observer remains on the ticket throughout remote actions; it never calls Refresh.
  observer.on('websocket', wire => wire.on('framereceived', frame => {
    for (const value of String(frame.payload).split('\u001e').filter(Boolean)) {
      try { const message = JSON.parse(value); if (message.target) received.add(message.target); if (message.type === 3 && message.result?.id === fixture.ticketId) ack = true; } catch {}
    }
  }));
  // Capture the existing connection's messages as well via CDP.
  const cdp = await observer.context().newCDPSession(observer);
  await cdp.send('Network.enable');
  cdp.on('Network.webSocketFrameReceived', ({ response }) => {
    for (const value of response.payloadData.split('\u001e').filter(Boolean)) {
      try { const message = JSON.parse(value); if (message.target) received.add(message.target); if (message.type === 3 && message.result?.id === fixture.ticketId) ack = true; } catch {}
    }
  });
  await observer.getByRole('link', { name: 'Tickets', exact: true }).click();
  await observer.goto(`${base}/tickets/${fixture.ticketId}`);
  // Full navigation drops memory-only auth. Reconnect in the same SPA, then use a link.
  await observer.goto(`${base}/settings`);
  await observer.getByLabel('Session access token').fill(token);
  await observer.getByRole('button', { name: 'Connect live updates' }).click();
  await expect(observer.getByText('Connected to ticket events', { exact: true })).toBeVisible();
  await observer.getByRole('link', { name: 'Tickets', exact: true }).click();
  await observer.getByRole('textbox', { name: 'Search tickets', exact: true }).fill('Browser verification fixture');
  await observer.getByRole('button', { name: 'Apply ticket search' }).click();
  await observer.locator(`a[href='/tickets/${fixture.ticketId}']`).click();
  await expect.poll(() => ack, { timeout: 20000 }).toBe(true);
  await observer.getByRole('button', { name: 'Add Comment', exact: true }).click();
  await observer.getByLabel('Comment', { exact: true }).fill('Unsaved observer draft survives live updates.');
  const operator = await browser.newPage();
  await operator.goto(`${base}/tickets/${fixture.ticketId}`);
  await operator.getByRole('button', { name: 'Analyze Ticket', exact: true }).click();
  await expect.poll(() => received.has('PredictionCompleted'), { timeout: 20000 }).toBe(true);
  await expect(observer.locator('.intelligence-row')).toHaveCount(3);
  await expect(observer.locator('.intelligence-row').first()).toContainText('%');
  await operator.getByRole('button', { name: 'Ask AI', exact: true }).click();
  await expect.poll(() => received.has('AIAnalysisCompleted'), { timeout: 30000 }).toBe(true);
  await expect(observer.locator('.analysis-copy').first()).toContainText('SQL timeout');
  await expect(observer.locator('.analysis-sources li').first()).toBeVisible();
  await operator.getByRole('button', { name: 'Assign Team', exact: true }).click();
  await operator.getByLabel('Find a team', { exact: true }).fill(fixture.teamName);
  await operator.getByRole('radio', { name: fixture.teamName, exact: true }).check();
  await operator.getByRole('button', { name: 'Save assignment', exact: true }).click();
  await expect(observer.locator('.properties')).toContainText(fixture.teamName);
  await operator.getByRole('button', { name: 'Change Status', exact: true }).click();
  await operator.getByRole('button', { name: 'Save status', exact: true }).click();
  await expect(observer.locator('.properties')).toContainText('In progress');
  await operator.getByRole('button', { name: 'Add Comment', exact: true }).click();
  await operator.getByLabel('Your user ID', { exact: true }).fill(fixture.authorId);
  await operator.getByLabel('Comment', { exact: true }).fill('Remote operator collected SQL logs.');
  await operator.getByRole('button', { name: 'Save comment', exact: true }).click();
  await expect(observer.locator('.comment-list')).toContainText('Remote operator collected SQL logs.');
  await expect(observer.getByLabel('Comment', { exact: true })).toHaveValue('Unsaved observer draft survives live updates.');
  await expect(observer.getByRole('button', { name: /Use current revision/ })).toBeVisible();
  for (const name of ['PredictionCompleted', 'AIAnalysisCompleted', 'TicketAssigned', 'TicketStatusChanged', 'CommentAdded']) expect(received.has(name), `Missing real event ${name}`).toBe(true);
  console.log('LIVE PASS: five real SignalR events refresh a second browser session; saved ML, AI sources, assigned team, status and comments; unsaved draft preserved. Real API/ML/PostgreSQL; simulated embedding/LLM HTTP providers.');
} finally { await browser.close(); }

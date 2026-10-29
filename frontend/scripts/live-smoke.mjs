import { chromium, expect } from '@playwright/test';
const token = process.env.FRONTEND_SMOKE_TOKEN;
if (!token) throw new Error('Set FRONTEND_SMOKE_TOKEN to a temporary, authorized read-only profile.');
const browser = await chromium.launch({ channel: 'chrome' });
try {
  const page = await browser.newPage();
  await page.addInitScript(() => {
    const NativeSocket = window.WebSocket;
    window.__qaSockets = [];
    window.WebSocket = class extends NativeSocket { constructor(...args) { super(...args); window.__qaSockets.push(this); } };
  });
  let sockets = 0;
  let acknowledgedSocket = 0;
  page.on('websocket', socket => {
    const socketNumber = ++sockets;
    const pending = new Set();
    const messages = payload => String(payload).split('\u001e').filter(Boolean).flatMap(value => { try { return [JSON.parse(value)]; } catch { return []; } });
    socket.on('framesent', frame => { for (const message of messages(frame.payload)) if (message.type === 1 && message.target === 'JoinTicket') pending.add(message.invocationId); });
    socket.on('framereceived', frame => {
      for (const message of messages(frame.payload)) if (message.type === 3 && pending.has(message.invocationId) && message.result?.id && !message.error) acknowledgedSocket = socketNumber;
    });
  });
  const base = process.env.FRONTEND_URL || 'http://127.0.0.1:5173';
  await page.goto(`${base}/dashboard`);
  await expect(page.locator('.metric-value')).toBeVisible({ timeout: 20000 });
  const response = await page.request.get(`${base}/api/tickets?pageSize=1`);
  expect(response.ok()).toBe(true);
  const records = await response.json();
  await expect(page.locator('.secondary-metric').nth(1)).toContainText(records.totalCount.toLocaleString());
  await page.goto(`${base}/settings`);
  await page.getByLabel('Session access token').fill(token);
  await page.getByRole('button', { name: 'Connect live updates' }).click();
  await expect(page.getByText('Connected to ticket events', { exact: true })).toBeVisible();
  await expect(page.getByLabel('Session access token')).toHaveValue('');
  if (records.items.length) {
    await page.getByRole('link', { name: 'Tickets', exact: true }).click();
    await page.locator('.ticket-link').first().click();
    await expect.poll(() => acknowledgedSocket, { timeout: 20000 }).toBeGreaterThan(0);
    const firstSocket = acknowledgedSocket;
    await page.evaluate(() => window.__qaSockets.at(-1).close());
    await expect.poll(() => acknowledgedSocket, { timeout: 20000 }).toBeGreaterThan(firstSocket);
    await expect(page.getByRole('link', { name: 'Live updates on', exact: true })).toBeVisible();
  }
  await page.getByRole('link', { name: 'Settings', exact: true }).click();
  await page.getByRole('button', { name: 'Disconnect', exact: true }).click();
  await expect(page.getByText('Live updates are switched off')).toBeVisible();
  const stores = await page.evaluate(() => JSON.stringify({ local: { ...localStorage }, session: { ...sessionStorage } }));
  expect(stores).not.toContain(token);
  console.log(`LIVE PASS: real PostgreSQL-backed dashboard; authenticated SignalR; ${records.items.length ? 'ticket join and forced WebSocket reconnect/rejoin; ' : ''}disconnect; no persisted token. No business writes.`);
} finally { await browser.close(); }

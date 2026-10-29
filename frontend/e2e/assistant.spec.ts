import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
const simulation = 'http://127.0.0.1:4908/helpdesk';
const token = 'assistant-browser-session-token-0123456789abcdef';
async function connect(page: import('@playwright/test').Page, value = token) {
  await page.goto('/settings');
  await page.getByLabel('Session access token').fill(value);
  await page.getByRole('button', { name: 'Connect live updates' }).click();
  await expect(page.getByRole('button', { name: 'Connect live updates' })).toBeEnabled();
  await page.locator('nav a[href="/ai-assistant"]').click();
  await expect(page.getByRole('textbox', { name: 'Your question', exact: true })).toBeVisible();
}
test.beforeEach(async ({ request }) => { await request.post(`${simulation}/__test/reset`); });
test.afterEach(async ({ request }) => { await request.post(`${simulation}/__test/reset`); });

test('natural question shows an audited investigation, structured response, sources and proposals', async ({ page }) => {
  await connect(page);
  await page.getByRole('textbox', { name: 'Your question', exact: true }).fill('Why is ticket #1045 failing?');
  await page.getByRole('button', { name: 'Send question', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Send question', exact: true })).toBeDisabled();
  await expect(page.getByText('Preparing your response.', { exact: false })).toBeVisible();
  await expect(page.getByText('The reported SQL failure requires a connection-path investigation.', { exact: true })).toBeVisible({ timeout: 10000 });
  await expect(page.locator('.chat-user-message')).toContainText('Why is ticket #1045 failing?');
  await expect(page.getByText('Human approval required before execution', { exact: true })).toBeVisible();
  await expect(page.locator('.analysis-sources').getByRole('link', { name: 'SQL Server connection timeout' })).toBeVisible();
  await expect(page.locator('.tool-activity')).toHaveCount(0);
  await expect(page.getByText('get_ticket_history', { exact: true })).toHaveCount(0);
  await expect(page.getByText('Proposals only. No action has been executed.', { exact: true })).toBeVisible();
  const stores = await page.evaluate(() => JSON.stringify({ local: { ...localStorage }, session: { ...sessionStorage } }));
  expect(stores).not.toContain(token); expect(stores).not.toContain('1045');
  await page.getByRole('textbox', { name: 'Ask a follow-up question', exact: true }).fill('What should I check first?');
  await page.getByRole('button', { name: 'Send question', exact: true }).click();
  await expect(page.getByText('First, verify the configured SQL host and listener port.', { exact: true })).toBeVisible({ timeout: 10000 });
  await expect(page.locator('.chat-user-message')).toHaveCount(2);
  const audit = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze();
  expect(audit.violations, JSON.stringify(audit.violations)).toEqual([]);
});
test('history survives reload after reconnect and another session cannot see it', async ({ page }) => {
  await connect(page);
  await page.getByRole('textbox', { name: 'Your question', exact: true }).fill('Why is ticket #1045 failing?');
  await page.getByRole('button', { name: 'Send question', exact: true }).click();
  await expect(page.locator('.analysis-sources')).toBeVisible({ timeout: 10000 });
  await page.reload();
  await expect(page.getByRole('heading', { name: 'Connect your private session' })).toBeVisible();
  await connect(page);
  await page.locator('.conversation-history').getByRole('button', { name: /Why is ticket #1045 failing/ }).click();
  await expect(page.locator('.chat-user-message')).toContainText('Why is ticket #1045 failing?');
  await expect(page.locator('.analysis-sources')).toBeVisible();
  await connect(page, 'assistant-other-session-token-0123456789abcdef');
  await expect(page.getByText('No conversations yet.', { exact: false })).toBeVisible();
  await expect(page.locator('.chat-user-message')).toHaveCount(0);
});
test('failed model calls remain failed in history without a fabricated AI answer', async ({ page, request }) => {
  await request.post(`${simulation}/__test/mode`, { data: { mode: 'assistant-failure' } });
  await connect(page);
  await page.getByRole('textbox', { name: 'Your question', exact: true }).fill('Why is ticket #1045 failing?');
  await page.getByRole('button', { name: 'Send question', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('No completed response was saved', { timeout: 10000 });
  await expect(page.locator('.chat-user-message')).toHaveCount(1);
  await expect(page.locator('.analysis-sources')).toHaveCount(0);
  await expect(page.locator('.tool-activity')).toHaveCount(0);
});
test('questions need one ticket and transport errors preserve the draft', async ({ page, request }) => {
  await connect(page);
  await page.getByRole('textbox', { name: 'Your question', exact: true }).fill('Why does it fail?');
  await page.getByRole('button', { name: 'Send question', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('Mention one ticket');
  await expect(page.getByRole('textbox', { name: 'Your question', exact: true })).toHaveValue('Why does it fail?');
  await request.post(`${simulation}/__test/mode`, { data: { mode: 'assistant-transport-error' } });
  await page.getByRole('textbox', { name: 'Your question', exact: true }).fill('Why is ticket #1045 failing?');
  await page.getByRole('button', { name: 'Send question', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('API could not complete');
  await expect(page.getByRole('textbox', { name: 'Ask a follow-up question', exact: true })).toHaveValue('Why is ticket #1045 failing?');
});
test('authenticated conversation passes axe, keyboard and responsive checks in both themes', async ({ page }) => {
  await connect(page);
  await page.getByRole('textbox', { name: 'Your question', exact: true }).fill('Why is ticket #1045 failing?');
  await page.getByRole('button', { name: 'Send question', exact: true }).click();
  await expect(page.locator('.analysis-sources')).toBeVisible({ timeout: 10000 });
  const input = page.getByRole('textbox', { name: 'Ask a follow-up question', exact: true });
  await input.fill('What should I check first?'); await input.focus(); await page.keyboard.press('Tab');
  await expect(page.getByRole('button', { name: 'Send question', exact: true })).toBeFocused();
  for (const theme of ['light', 'dark']) {
    await page.evaluate(value => { document.documentElement.dataset.theme = value; localStorage.setItem('iti.theme', value); }, theme);
    await page.addStyleTag({ content: '*{transition:none!important;animation:none!important}' });
    const result = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze();
    expect(result.violations, JSON.stringify(result.violations)).toEqual([]);
    for (const width of [280, 320, 390, 768, 1280, 1600]) {
      await page.setViewportSize({ width, height: 900 }); await page.addStyleTag({ content: 'html{font-size:125%}' });
      expect(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), `${theme} ${width}`).toBe(false);
    }
  }
});

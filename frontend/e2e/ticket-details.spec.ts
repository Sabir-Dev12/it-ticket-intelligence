import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
const id = '10000000-0000-4000-8000-000000000001';
const author = '20000000-0000-4000-8000-000000000001';
const simulation = 'http://127.0.0.1:4908/helpdesk';
test.beforeEach(async ({ request }) => { expect((await request.post(`${simulation}/__test/reset`)).ok()).toBe(true); });
test.afterEach(async ({ request }) => { await request.post(`${simulation}/__test/reset`); });

test('ML and AI actions show persisted, distinct results without assigning the ticket', async ({ page }) => {
  await page.goto(`/tickets/${id}`);
  await expect(page.getByRole('button', { name: 'Analyze Ticket', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Analyze Ticket', exact: true }).click();
  await expect(page.locator('.intelligence-row').first()).toContainText('94%');
  await expect(page.locator('.intelligence-row').nth(1)).toContainText('91%');
  await expect(page.locator('.intelligence-row').nth(2)).toContainText('Database');
  await expect(page.locator('.properties')).toContainText('Not assigned');
  await page.getByRole('button', { name: 'Ask AI', exact: true }).click();
  await expect(page.getByText('The reported SQL timeout needs a connection-path investigation.', { exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: /Diagnosis/ })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Possible Causes', exact: true })).toBeVisible();
  await expect(page.getByText('Human approval required before execution', { exact: true })).toBeVisible();
  await expect(page.locator('.analysis-sources').getByRole('link', { name: 'SQL Server connection timeout' })).toHaveAttribute('href', /knowledge\/30000000/);
  await page.reload();
  await expect(page.getByText('The reported SQL timeout needs a connection-path investigation.', { exact: true })).toBeVisible();
  await expect(page.locator('.properties')).toContainText('Not assigned');
});
test('assignment, status and comment forms submit correct revisions and preserve an unsaved draft during refresh', async ({ page }) => {
  await page.goto(`/tickets/${id}`);
  await page.getByRole('button', { name: 'Assign Team', exact: true }).click();
  await page.getByRole('radio', { name: 'Database', exact: true }).check();
  await page.getByRole('button', { name: 'Save assignment', exact: true }).click();
  await expect(page.locator('.properties')).toContainText('Database');
  await page.getByRole('button', { name: 'Change Status', exact: true }).click();
  await page.getByRole('button', { name: 'Save status', exact: true }).click();
  await expect(page.locator('.properties')).toContainText('In progress');
  await page.getByRole('button', { name: 'Add Comment', exact: true }).click();
  await page.getByLabel('Your user ID', { exact: true }).fill(author);
  await page.getByLabel('Comment', { exact: true }).fill('Collected the connection logs.');
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  await expect(page.getByLabel('Comment', { exact: true })).toHaveValue('Collected the connection logs.');
  await page.getByRole('button', { name: 'Save comment', exact: true }).click();
  await expect(page.locator('.comment-list')).toContainText('Collected the connection logs.');
  await page.getByRole('button', { name: 'Change Status', exact: true }).click();
  await page.getByLabel('Resolution', { exact: true }).fill('The SQL listener configuration was corrected.');
  await page.getByRole('button', { name: 'Save status', exact: true }).click();
  await expect(page.locator('.properties')).toContainText('Resolved');
  await expect(page.getByRole('button', { name: 'Ask AI', exact: true })).toBeDisabled();
});
test('a revision conflict requires review and never retries the mutation automatically', async ({ page, request }) => {
  await page.goto(`/tickets/${id}`);
  await page.getByRole('button', { name: 'Assign Team', exact: true }).click();
  await page.getByRole('radio', { name: 'Database', exact: true }).check();
  await request.post(`${simulation}/__test/mode`, { data: { mode: 'conflict' } });
  let changes = 0; page.on('request', req => { if (req.method() === 'PATCH') changes++; });
  await page.getByRole('button', { name: 'Save assignment', exact: true }).click();
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Updated by another support agent');
  await expect(page.getByRole('button', { name: 'Use current revision 2' })).toBeVisible();
  await expect(page.getByRole('radio', { name: 'Database', exact: true })).toBeChecked();
  await expect(page.getByRole('button', { name: 'Save assignment', exact: true })).toBeDisabled();
  expect(changes).toBe(1);
  await page.getByRole('button', { name: 'Use current revision 2' }).click();
  await page.getByRole('button', { name: 'Save assignment', exact: true }).click();
  await expect(page.locator('.properties')).toContainText('Database'); expect(changes).toBe(2);
});
test('AI failure preserves saved ML results and never invents a successful analysis', async ({ page, request }) => {
  await request.post(`${simulation}/__test/mode`, { data: { mode: 'llm-unavailable' } });
  await page.goto(`/tickets/${id}`);
  await page.getByRole('button', { name: 'Ask AI', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('AI investigation could not be confirmed');
  await expect(page.locator('.intelligence-row').first()).toContainText('94%');
  await expect(page.getByText('No AI investigation saved', { exact: true })).toBeVisible();
  await expect(page.locator('.analysis-sources')).toHaveCount(0);
});
test('terminal tickets disable active operations', async ({ page, request }) => {
  await request.post(`${simulation}/__test/mode`, { data: { mode: 'closed' } });
  await page.goto(`/tickets/${id}`);
  for (const name of ['Analyze Ticket', 'Ask AI', 'Assign Team', 'Change Status', 'Add Comment']) await expect(page.getByRole('button', { name, exact: true })).toBeDisabled();
});
test('detail and action forms pass axe in both themes and fit narrow screens', async ({ page }) => {
  for (const theme of ['light', 'dark']) {
    await page.goto(`/tickets/${id}`);
    await page.evaluate(value => { document.documentElement.dataset.theme = value; localStorage.setItem('iti.theme', value); }, theme);
    await page.getByRole('button', { name: 'Ask AI', exact: true }).click();
    await expect(page.locator('.analysis-sources')).toBeVisible();
    for (const form of ['Assign Team', 'Change Status', 'Add Comment']) {
      await page.getByRole('button', { name: form, exact: true }).click();
      await page.addStyleTag({ content: '*{transition:none!important;animation:none!important}' });
      const result = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze();
      expect(result.violations, JSON.stringify(result.violations)).toEqual([]);
      for (const width of [280, 320, 390, 1280]) {
        await page.setViewportSize({ width, height: 900 });
        await page.addStyleTag({ content: 'html{font-size:125%}' });
        expect(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), `${form} ${theme} ${width}`).toBe(false);
      }
      await page.getByRole('button', { name: 'Cancel', exact: true }).click();
    }
  }
});

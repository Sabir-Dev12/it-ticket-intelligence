import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

// Run against the Postman read-api simulation, using the normal Vite proxy and real fetch.
test('overview uses contract totals and navigates to a ticket', async ({ page }) => {
  await page.goto('/dashboard');
  await expect(page.locator('.metric-value')).toContainText('27');
  await expect(page.locator('.secondary-metric').first()).toContainText('5');
  await page.getByRole('link', { name: /TKT-1045/ }).click();
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Production SQL connection pool exhausted');
  await expect(page.getByText('No model predictions have been saved')).toBeVisible();
  await expect(page.getByText('Initial investigation is in progress.')).toBeVisible();
});
test('ticket filters, pagination, empty state and recovery use real HTTP', async ({ page }) => {
  await page.goto('/tickets');
  await expect(page.getByText('36 results')).toBeVisible();
  await page.getByRole('button', { name: 'Next page' }).click();
  await expect(page.getByText('Page 2')).toBeVisible();
  await page.getByRole('button', { name: 'Resolved', exact: true }).click();
  await expect(page.getByText('9 results')).toBeVisible();
  await page.getByRole('button', { name: 'All tickets', exact: true }).click();
  await page.getByRole('textbox', { name: 'Search tickets', exact: true }).fill('no-matching-ticket');
  await page.getByRole('button', { name: 'Apply ticket search' }).click();
  await expect(page.getByRole('heading', { name: 'No tickets match this view' })).toBeVisible();
  await page.getByRole('textbox', { name: 'Search tickets', exact: true }).fill('simulate-unavailable');
  await page.getByRole('button', { name: 'Apply ticket search' }).click();
  await expect(page.getByRole('alert')).toContainText('API could not complete');
  await page.getByRole('button', { name: 'Retry request' }).click();
  await expect(page.getByRole('alert')).toBeVisible();
  await page.getByRole('textbox', { name: 'Search tickets', exact: true }).fill('VPN');
  await page.getByRole('button', { name: 'Apply ticket search' }).click();
  await expect(page.getByText('5 results')).toBeVisible();
  await page.goBack();
  await expect(page.getByRole('textbox', { name: 'Search tickets', exact: true })).toHaveValue('simulate-unavailable');
  await page.goForward();
  await expect(page.getByRole('textbox', { name: 'Search tickets', exact: true })).toHaveValue('VPN');
});
test('knowledge opens a document; an unauthenticated assistant does not start an investigation', async ({ page }) => {
  const writes: string[] = [];
  page.on('request', req => { if (req.method() !== 'GET' && req.url().includes('/api/')) writes.push(req.url()); });
  await page.goto('/knowledge');
  await page.getByRole('link', { name: /SQL Server connection timeout/ }).click();
  await expect(page.getByText('Check the connection path')).toBeVisible();
  await page.goto('/ai-assistant');
  await expect(page.getByRole('heading', { name: 'Connect your private session' })).toBeVisible();
  expect(writes).toEqual([]);
});
test('theme persists, invalid tokens are rejected and tokens are never persisted', async ({ page }) => {
  await page.goto('/settings');
  await page.getByRole('button', { name: 'Dark', exact: true }).click();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  await page.reload();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  await page.getByLabel('Session access token').fill('short');
  await page.getByRole('button', { name: 'Connect live updates' }).click();
  await expect(page.getByRole('alert')).toContainText('32 to 512');
  const storage = await page.evaluate(() => ({ ...localStorage, ...sessionStorage }));
  expect(storage).toEqual({ 'iti.theme': 'dark' });
});
test('unknown routes and invalid identifiers are useful', async ({ page }) => {
  await page.goto('/tickets/not-an-id');
  await expect(page.getByRole('alert')).toContainText('ticket link is invalid');
  await page.goto('/nothing-here');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('This path leads nowhere.');
  await page.getByRole('link', { name: 'Return to overview' }).click();
  await expect(page).toHaveURL(/dashboard/);
});
for (const theme of ['light', 'dark']) {
  test(`all main pages have no axe violations in ${theme} theme`, async ({ page }) => {
    for (const route of ['/dashboard', '/tickets', '/tickets/10000000-0000-4000-8000-000000000001', '/ai-assistant', '/knowledge', '/knowledge/30000000-0000-4000-8000-000000000001', '/analytics', '/settings']) {
      await page.goto(route);
      await page.evaluate(value => { document.documentElement.dataset.theme = value; localStorage.setItem('iti.theme', value); }, theme);
      await expect(page.getByRole('heading', { name: 'Loading your workspace' })).toHaveCount(0);
      await page.addStyleTag({ content: '* { transition: none !important; animation: none !important; }' });
      const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze();
      expect(results.violations, `${route} ${theme}: ${JSON.stringify(results.violations)}`).toEqual([]);
    }
  });
}
test('mobile navigation, narrow layouts and wider text avoid page overflow', async ({ page }) => {
  for (const width of [280, 320, 390, 768, 1280, 1600]) {
    await page.setViewportSize({ width, height: 900 });
    for (const route of ['/dashboard', '/tickets', '/ai-assistant', '/knowledge', '/analytics', '/settings']) {
      await page.goto(route);
      await expect(page.getByRole('heading', { name: 'Loading your workspace' })).toHaveCount(0);
      await page.addStyleTag({ content: 'html { font-size: 125%; }' });
      const overflow = await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1);
      expect(overflow, `${route} overflows at ${width}px and 125% text`).toBe(false);
    }
  }
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/dashboard');
  await page.getByRole('button', { name: 'Open navigation' }).click();
  await page.keyboard.press('Tab');
  expect(await page.locator('.sidebar').evaluate(sidebar => sidebar.contains(document.activeElement))).toBe(true);
  await page.keyboard.press('Escape');
  await expect(page.getByRole('button', { name: 'Open navigation' })).toBeFocused();
  await page.getByRole('button', { name: 'Open navigation' }).click();
  await page.getByRole('link', { name: 'Knowledge base', exact: true }).click();
  await expect(page).toHaveURL(/knowledge/);
  await expect(page.getByRole('button', { name: 'Open navigation' })).toHaveAttribute('aria-expanded', 'false');
});

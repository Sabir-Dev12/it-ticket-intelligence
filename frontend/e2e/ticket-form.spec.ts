import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
const simulation = 'http://127.0.0.1:4908/helpdesk';
const ticketId = '10000000-0000-4000-8000-000000000001';
const actor = '20000000-0000-4000-8000-000000000001';
const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aKxkAAAAASUVORK5CYII=', 'base64');
async function fill(page: import('@playwright/test').Page) {
  await page.getByLabel('Your user ID', { exact: false }).fill(actor);
  await page.getByLabel('Title', { exact: false }).fill('SQL Server connection timeout');
  await page.getByLabel('Description', { exact: false }).fill('Production users cannot connect after deployment. No changes have been applied.');
}
async function choose(page: import('@playwright/test').Page, label: string, value: string) {
  await page.getByRole('combobox', { name: new RegExp('^' + label) }).click();
  await page.getByRole('option', { name: value, exact: true }).click();
}
test.beforeEach(async ({ request }) => { await request.post(`${simulation}/__test/reset`); });
test.afterEach(async ({ request }) => { await request.post(`${simulation}/__test/reset`); });
test('create validates, reviews ML suggestions, previews evidence and prevents duplicate submissions', async ({ page }) => {
  let creates = 0; page.on('request', r => { if (r.method() === 'POST' && /\/api\/tickets$/.test(r.url())) creates++; });
  await page.goto('/tickets/new');
  await page.getByRole('button', { name: 'Create ticket', exact: true }).click();
  await expect(page.getByText('Enter a title.', { exact: true })).toBeVisible(); expect(creates).toBe(0);
  await fill(page);
  await page.getByRole('button', { name: 'Analyze with AI', exact: true }).focus(); await page.keyboard.press('Enter');
  await expect(page.getByText('94% model confidence')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Analyze with AI', exact: true })).toBeFocused();
  await expect(page.getByText('Suggestions ready. Review them before saving.', { exact: true })).toBeVisible();
  await expect(page.getByRole('combobox', { name: /^Category/ })).toContainText('Unclassified'); expect(creates).toBe(0);
  await page.getByRole('button', { name: 'Use these suggestions', exact: true }).click();
  await page.getByRole('button', { name: 'Review classification', exact: true }).click();
  await expect(page.getByRole('combobox', { name: /^Category/ })).toBeFocused();
  await expect(page.getByRole('combobox', { name: /^Assigned Team/ })).toContainText('Database');
  await choose(page, 'Priority', 'Medium');
  await page.locator('#ticket-files').setInputFiles({ name: 'screenshot.png', mimeType: 'image/png', buffer: png });
  await expect(page.getByAltText('Preview of screenshot.png')).toBeVisible();
  await expect(page.getByText(/Selected, upload not confirmed/)).toBeVisible();
  await page.locator('form.ticket-editor').evaluate(form => { (form as HTMLFormElement).requestSubmit(); (form as HTMLFormElement).requestSubmit(); });
  await expect(page).toHaveURL(/\/tickets\/[a-f0-9-]+$/); expect(creates).toBe(1);
  await expect(page.getByText(/created successfully. All selected attachments were saved/)).toBeVisible();
  await expect(page.locator('.properties')).toContainText('Medium');
  await expect(page.getByRole('link', { name: 'Download original' })).toBeVisible();
});
test('edit restores persisted values and saves one complete update', async ({ page }) => {
  let update: Record<string, unknown> | undefined;
  page.on('request', r => { if (r.method() === 'PUT') update = r.postDataJSON(); });
  await page.goto(`/tickets/${ticketId}/edit`);
  await expect(page.getByLabel('Title', { exact: false })).toHaveValue('Production SQL connection pool exhausted');
  await page.getByLabel('Title', { exact: false }).fill('Reviewed SQL connection issue');
  await choose(page, 'Assigned Team', 'Backend');
  await page.getByRole('button', { name: 'Save changes', exact: true }).click();
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Reviewed SQL connection issue');
  expect(update?.revision).toBe(1); expect(update?.assignment).toEqual({ teamId: '50000000-0000-4000-8000-000000000004' });
  await expect(page.locator('.properties')).toContainText('Backend');
});
test('a failed upload reports the saved ticket and retry never recreates it', async ({ page, request }) => {
  let creates = 0; page.on('request', r => { if (r.method() === 'POST' && /\/api\/tickets$/.test(r.url())) creates++; });
  await request.post(`${simulation}/__test/mode`, { data: { mode: 'upload-unavailable' } });
  await page.goto('/tickets/new'); await fill(page);
  await page.locator('#ticket-files').setInputFiles({ name: 'diagnostic.txt', mimeType: 'text/plain', buffer: Buffer.from('Observed SQL timeout. No commands executed.') });
  await page.getByRole('button', { name: 'Create ticket', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('was saved. Some attachment changes have not been confirmed.');
  await expect(page.getByLabel('Title', { exact: false })).toBeDisabled();
  await request.post(`${simulation}/__test/mode`, { data: { mode: '' } });
  await page.getByRole('button', { name: 'Retry remaining attachments', exact: true }).click();
  await expect(page).toHaveURL(/\/tickets\/[a-f0-9-]+$/); expect(creates).toBe(1);
  await expect(page.getByRole('link', { name: 'Download original' })).toBeVisible();
});
test('cancel, route navigation and reload protect an unsaved draft', async ({ page }) => {
  await page.goto('/tickets/new'); await fill(page);
  expect(await page.evaluate(() => { const event = new Event('beforeunload', { cancelable: true }); window.dispatchEvent(event); return event.defaultPrevented; })).toBe(true);
  await page.getByRole('button', { name: 'Cancel', exact: true }).click();
  const dialog = page.getByRole('dialog'); await expect(dialog).toBeVisible();
  await expect(dialog.getByRole('button', { name: 'Keep editing' })).toBeFocused();
  for (let i = 0; i < 4; i++) { await page.keyboard.press('Tab'); expect(await page.evaluate(() => document.querySelector('dialog')?.contains(document.activeElement))).toBe(true); }
  await page.keyboard.press('Escape'); await expect(dialog).not.toBeVisible();
  await expect(page.getByLabel('Title', { exact: false })).toHaveValue('SQL Server connection timeout');
  await page.getByRole('button', { name: 'Cancel', exact: true }).click();
  await dialog.getByRole('button', { name: 'Discard unsaved changes' }).click(); await expect(page).toHaveURL(/\/tickets$/);
});
test('bad files and server validation do not lose form values', async ({ page }) => {
  await page.goto('/tickets/new'); await fill(page);
  await page.locator('#ticket-files').setInputFiles({ name: 'script.svg', mimeType: 'image/svg+xml', buffer: Buffer.from('<svg/>') });
  await expect(page.locator('#attachments-error')).toContainText('Use PNG, JPEG');
  await expect(page.locator('.attachment-list li')).toHaveCount(0);
  await page.getByLabel('Your user ID', { exact: false }).fill('20000000-0000-4000-8000-000000000099');
  await page.getByRole('button', { name: 'Create ticket', exact: true }).click();
  await expect(page.getByText('This record could not be found.', { exact: true })).toBeVisible();
  await expect(page.getByLabel('Title', { exact: false })).toHaveValue('SQL Server connection timeout');
});
test('stale ML suggestions and revision conflicts require a new review', async ({ page, request }) => {
  await page.goto(`/tickets/${ticketId}/edit`);
  await page.getByRole('button', { name: 'Analyze with AI', exact: true }).click();
  await expect(page.getByText('94% model confidence')).toBeVisible();
  await page.getByLabel('Title', { exact: false }).fill('New observations');
  await expect(page.getByRole('button', { name: 'Use these suggestions', exact: true })).toBeDisabled();
  await request.post(`${simulation}/__test/mode`, { data: { mode: 'conflict' } });
  await page.getByRole('button', { name: 'Save changes', exact: true }).click();
  await expect(page.getByText(/Your draft is preserved/)).toBeVisible();
  await expect(page.getByLabel('Title', { exact: false })).toHaveValue('New observations');
  await expect(page.getByRole('button', { name: 'Save changes', exact: true })).toBeDisabled();
});
test('an uncertain creation outcome blocks duplicate creation and preserves the draft', async ({ page, request }) => {
  let creates = 0; page.on('request', r => { if (r.method() === 'POST' && /\/api\/tickets$/.test(r.url())) creates++; });
  await request.post(`${simulation}/__test/mode`, { data: { mode: 'create-unavailable' } });
  await page.goto('/tickets/new'); await fill(page);
  await page.getByRole('button', { name: 'Create ticket', exact: true }).click();
  await expect(page.getByText(/The save outcome is unknown/)).toBeVisible();
  await expect(page.getByRole('button', { name: 'Create ticket', exact: true })).toBeDisabled();
  expect(creates).toBe(1); await expect(page.getByLabel('Title', { exact: false })).toHaveValue('SQL Server connection timeout');
});
test('form, combobox and discard dialog pass axe and fit small screens in both themes', async ({ page }) => {
  await page.goto('/tickets/new'); await fill(page);
  const category = page.getByRole('combobox', { name: /^Category/ }); await category.focus(); await page.keyboard.press('ArrowDown'); await page.keyboard.press('d'); await page.keyboard.press('Enter');
  await expect(category).toContainText('Database');
  for (const theme of ['light', 'dark']) {
    await page.evaluate(theme => { document.documentElement.dataset.theme = theme; }, theme); await page.addStyleTag({ content: '*{transition:none!important;animation:none!important}' });
    const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze(); expect(results.violations, JSON.stringify(results.violations)).toEqual([]);
    for (const width of [280, 320, 390, 768, 1280, 1600]) { await page.setViewportSize({ width, height: 900 }); await page.addStyleTag({ content: 'html{font-size:125%}' }); expect(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), `${theme} ${width}`).toBe(false); }
  }
  await page.getByRole('button', { name: 'Cancel', exact: true }).click();
  const dialogResults = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze(); expect(dialogResults.violations, JSON.stringify(dialogResults.violations)).toEqual([]);
});

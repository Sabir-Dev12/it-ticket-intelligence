import { chromium, expect } from '@playwright/test';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { resolve } from 'node:path';
const base = process.env.FRONTEND_URL || 'http://127.0.0.1:5173';
const out = resolve('../artifacts/frontend'); await mkdir(out, { recursive: true });
const fixture = JSON.parse(await readFile(`${out}/ticket-form-fixture.json`, 'utf8'));
const browser = await chromium.launch({ channel: 'chrome' });
const page = await browser.newPage({ viewport: { width: 1280, height: 1000 } });
const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aKxkAAAAASUVORK5CYII=', 'base64');
async function capture(name) {
  for (const theme of ['light', 'dark']) for (const width of [1280, 390]) {
    await page.setViewportSize({ width, height: width === 390 ? 844 : 1000 });
    await page.evaluate(theme => { document.documentElement.dataset.theme = theme; }, theme);
    await page.addStyleTag({ content: '*{transition:none!important;animation:none!important}' });
    await page.evaluate(async () => { document.activeElement?.blur?.(); scrollTo({ top: 0, behavior: 'instant' }); await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))); });
    await page.mouse.move(0, 0); await page.screenshot({ path: `${out}/ticket-form-${name}-${theme}-${width}.png`, fullPage: true });
    if (width === 1280) await writeFile(`${out}/ticket-form-${name}-${theme}.html`, await page.evaluate(() => {
      const html = document.documentElement.cloneNode(true); html.querySelectorAll('script,link').forEach(node => node.remove());
      html.querySelectorAll('style').forEach(node => { node.textContent = node.textContent.replace(/@font-face\s*\{[^}]*\}/g, ''); });
      return '<!doctype html>' + html.outerHTML;
    }));
  }
}
try {
  await page.goto(`${base}/tickets/new`); await expect(page.locator('#ticket-title')).toBeVisible({ timeout: 20000 });
  await page.locator('#ticket-creator').fill(fixture.authorId); await page.locator('#ticket-title').fill('SQL Server connection timeout');
  await page.locator('#ticket-description').fill('Form verification fixture: SQL Server connection timeout after deployment. <script>literal evidence, not HTML</script>');
  await page.getByRole('button', { name: 'Analyze with AI', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Use these suggestions', exact: true })).toBeVisible({ timeout: 60000 });
  await expect(page.getByRole('combobox', { name: /^Category/ })).toContainText('Unclassified');
  await page.getByRole('button', { name: 'Use these suggestions', exact: true }).click();
  await page.getByRole('combobox', { name: /^Assigned Team/ }).click(); await page.getByRole('option', { name: fixture.teamName, exact: true }).click();
  await page.locator('#ticket-files').setInputFiles({ name: 'screen.png', mimeType: 'image/png', buffer: png });
  await expect(page.getByAltText('Preview of screen.png')).toBeVisible(); await capture('create');
  await page.getByRole('button', { name: 'Create ticket', exact: true }).click();
  await expect(page).toHaveURL(/\/tickets\/[a-f0-9-]+$/, { timeout: 30000 });
  fixture.ticketId = new URL(page.url()).pathname.split('/').at(-1); await writeFile(`${out}/ticket-form-fixture.json`, JSON.stringify(fixture, null, 2));
  await expect(page.getByText(/created successfully/)).toBeVisible(); await expect(page.locator('.document-content script')).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'Download original' })).toBeVisible();
  await page.getByRole('link', { name: 'Edit ticket', exact: true }).click();
  await expect(page.locator('#ticket-title')).toHaveValue('SQL Server connection timeout');
  await page.locator('#ticket-title').fill('SQL connection timeout reviewed');
  await page.getByRole('button', { name: 'Preview screen.png', exact: true }).click(); await expect(page.getByAltText('Preview of screen.png')).toBeVisible();
  await capture('edit');
  await page.getByRole('button', { name: 'Cancel', exact: true }).click();
  await expect(page.getByRole('dialog')).toBeVisible(); await capture('discard');
  await page.getByRole('button', { name: 'Keep editing', exact: true }).click();
  await page.getByRole('button', { name: 'Save changes', exact: true }).click(); await expect(page.getByRole('heading', { level: 1 })).toHaveText('SQL connection timeout reviewed');
  await page.getByRole('link', { name: 'Edit ticket', exact: true }).click();
  await page.getByRole('button', { name: 'Remove screen.png', exact: true }).click();
  await page.getByRole('button', { name: 'Save changes', exact: true }).click();
  await expect(page.getByRole('dialog')).toContainText('screen.png');
  await page.getByRole('button', { name: 'Save and remove attachments', exact: true }).click();
  await expect(page).toHaveURL(new RegExp(`/tickets/${fixture.ticketId}$`)); await expect(page.getByText('No attachments saved.', { exact: true })).toBeVisible();
  await writeFile(`${out}/ticket-form-live-result.json`, JSON.stringify({ ticketId: fixture.ticketId, create: true, realMlPredictionsReviewed: true, pngRoundTrip: true, literalHtml: true, edit: true, discardGuard: true, attachmentDeletionConfirmed: true, providers: 'real API, ML.NET and PostgreSQL; no LLM call' }, null, 2));
  console.log('Real browser/API/PostgreSQL/ML.NET passed: create, reviewed predictions, image upload/preview, edit, discard guard and confirmed attachment removal.');
} finally { await browser.close(); }

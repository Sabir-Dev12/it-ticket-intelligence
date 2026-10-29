import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

// Opt-in: this test uses the actual API, database and configured local LLM.
test('live ticket question is saved and internal tools stay hidden', async ({ page }) => {
  test.skip(!process.env.ASSISTANT_LIVE_EMAIL || !process.env.ASSISTANT_LIVE_PASSWORD, 'Provide local test credentials to run against real services.');
  test.setTimeout(240000);
  await page.setViewportSize({ width: 1280, height: 900 });
  await page.goto('/login');
  await page.getByLabel('Email', { exact: true }).fill(process.env.ASSISTANT_LIVE_EMAIL!);
  await page.getByLabel('Password', { exact: true }).fill(process.env.ASSISTANT_LIVE_PASSWORD!);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Sign out', exact: true })).toBeVisible();
  await page.locator('.sidebar nav a[href="/ai-assistant"]').click();
  const reference = process.env.ASSISTANT_LIVE_TICKET ?? 'IT-1000';
  await page.getByLabel('Your question', { exact: true }).fill(`Donne-moi les détails du ticket ${reference}.`);
  const answer = page.waitForResponse(r => r.url().includes('/assistant/conversations/') && r.url().endsWith('/messages') && r.request().method() === 'POST', { timeout: 200000 });
  await page.getByRole('button', { name: 'Send question', exact: true }).click();
  const response = await answer;
  expect(response.status()).toBe(200);
  const conversation = await response.json();
  const turn = conversation.turns.at(-1);
  expect(turn.state).toBe('Completed');
  expect(turn.errorCode).toBeNull();
  expect(turn.analysis.summary.length).toBeGreaterThan(0);
  await expect(page.getByText(turn.analysis.summary, { exact: true })).toBeVisible();
  await expect(page.locator('.tool-activity')).toHaveCount(0);
  await expect(page.getByRole('alert')).toHaveCount(0);
  await expect(page.getByText('Proposals only. No action has been executed.', { exact: true })).toBeVisible();
  // Measure settled theme colors rather than the intermediate animated palette.
  await page.addStyleTag({ content: '*{transition:none!important;animation:none!important}' });
  for (const theme of ['light', 'dark']) {
    await page.evaluate(value => document.documentElement.dataset.theme = value, theme);
    expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    for (const width of [280, 320, 414, 1280]) {
      await page.setViewportSize({ width, height: 900 });
      expect(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1)).toBe(false);
    }
    await page.locator('.chat-turn').last().screenshot({ path: `../artifacts/frontend/assistant-fixed-${theme}.png` });
  }
});

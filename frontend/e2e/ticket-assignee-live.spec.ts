import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

test('create with a named person, enforce eligibility, reassign and revoke direct access', async ({ page, request }) => {
  test.skip(!process.env.ASSIGNEE_TEST_USER_PASSWORD || !process.env.ASSIGNEE_TEST_AGENT_PASSWORD || !process.env.ASSIGNEE_TEST_SUPPORT_PASSWORD, 'Requires local test accounts and the real database.');
  test.setTimeout(90000);
  await page.setViewportSize({ width: 1280, height: 900 });
  await page.goto('/login');
  await page.getByLabel('Email', { exact: true }).fill('user.test@itticket.local');
  await page.getByLabel('Password', { exact: true }).fill(process.env.ASSIGNEE_TEST_USER_PASSWORD!);
  const loginResponse = page.waitForResponse(r => r.url().endsWith('/api/auth/login') && r.request().method() === 'POST');
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  const user = await (await loginResponse).json();
  const userHeaders = { Authorization: `Bearer ${user.accessToken}` };
  await page.locator('.sidebar nav a[href="/tickets"]').click();
  await page.getByRole('link', { name: 'Create ticket', exact: true }).click();
  await page.getByLabel('Title Required').fill('[Local test] Individual assignee');
  await page.getByLabel('Description Required').fill('VPN connection times out. Test assignment to a named person rather than a team.');
  await page.getByRole('combobox', { name: /^Assigned to/ }).click();
  await page.getByRole('option', { name: 'Local Test Agent · Agent', exact: true }).click();
  await page.addStyleTag({ content: '*{transition:none!important;animation:none!important}' });
  for (const theme of ['light', 'dark']) {
    await page.evaluate(t => document.documentElement.dataset.theme = t, theme);
    expect((await new AxeBuilder({ page }).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze()).violations).toEqual([]);
    for (const width of [280,320,414,1280]) {
      await page.setViewportSize({ width, height: 900 });
      expect(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1)).toBe(false);
    }
    await page.locator('.ticket-editor').screenshot({ path: `../artifacts/frontend/ticket-assignee-${theme}.png` });
  }
  await page.getByRole('button', { name: 'Create ticket', exact: true }).click();
  await expect(page).toHaveURL(/\/tickets\/[a-f0-9-]+$/, { timeout: 20000 });
  await expect(page.locator('.properties')).toContainText('Local Test Agent');
  const id = page.url().split('/').at(-1)!;
  const api = '/api/tickets';
  const ticket = await (await request.get(`${api}/${id}`, { headers: userHeaders })).json();
  expect(ticket.assignedUserName).toBe('Local Test Agent');
  expect(ticket.assignedTeamId).toBeNull();
  async function login(role: string, password: string) {
    const response = await request.post('/api/auth/login', { data: { email: `${role}.test@itticket.local`, password } });
    expect(response.status()).toBe(200);
    const session = await response.json();
    return { session, headers: { Authorization: `Bearer ${session.accessToken}` } };
  }
  const agent = await login('agent', process.env.ASSIGNEE_TEST_AGENT_PASSWORD!);
  expect((await request.get(`${api}/${id}`, { headers: agent.headers })).status()).toBe(200);
  for (const [assignedUserId, status] of [[user.user.id, 409], ['00000000-0000-0000-0000-000000000000', 400], [crypto.randomUUID(), 404]] as const) {
    expect((await request.post(api, { headers: userHeaders, data: { title: 'Invalid assignee', description: 'Should not be saved.', createdById: user.user.id, assignedUserId } })).status()).toBe(status);
  }
  const support = await login('support', process.env.ASSIGNEE_TEST_SUPPORT_PASSWORD!);
  expect((await request.put(`${api}/${id}`, { headers: support.headers, data: { title: ticket.title, description: ticket.description, category: ticket.category, priority: ticket.priority, revision: ticket.revision, assignee: { userId: support.session.user.id } } })).status()).toBe(200);
  const changed = await (await request.get(`${api}/${id}`, { headers: support.headers })).json();
  expect(changed.assignedUserName).toBe('Local Test Support');
  expect((await request.get(`${api}/${id}`, { headers: agent.headers })).status()).toBe(404);
  expect((await request.put(`${api}/${id}`, { headers: support.headers, data: { title: changed.title, description: changed.description, category: changed.category, priority: changed.priority, revision: changed.revision, assignee: { userId: null } } })).status()).toBe(200);
  expect((await (await request.get(`${api}/${id}`, { headers: support.headers })).json()).assignedUserId).toBeNull();
});

import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
const article='30000000-0000-4000-8000-000000000001';
test.beforeEach(async({page,request})=>{
  expect((await request.post('http://127.0.0.1:4918/helpdesk/__test/reset')).ok()).toBe(true);
  await page.goto('/login');await page.getByLabel('Email',{exact:true}).fill('admin@example.test');await page.getByLabel('Password',{exact:true}).fill('synthetic-password-for-ui-only');await page.getByRole('button',{name:'Sign in',exact:true}).click();await expect(page).toHaveURL(/dashboard/);
});
test('workflow 1: create ticket, see initial ML, ask AI and open cited source',async({page})=>{
  await page.locator('.sidebar a[href="/tickets"]').click();await page.locator('a[href="/tickets/new"]').click();await page.locator('#ticket-title').fill('SQL Server application timeout');await page.locator('#ticket-description').fill('Cannot connect to SQL Server from the production application.');
  await page.getByRole('button',{name:'Create ticket',exact:true}).click();await expect(page).toHaveURL(/\/tickets\/[a-f0-9-]+$/);
  await expect(page.locator('.intelligence-row')).toHaveCount(3);await expect(page.locator('.intelligence-row').first()).toContainText('94%');
  await page.getByRole('button',{name:'Ask AI',exact:true}).click();await expect(page.getByText('The reported SQL timeout needs a connection-path investigation.',{exact:true})).toBeVisible();
  await expect(page.locator('.properties')).toContainText('Not assigned');
  await page.locator('.analysis-sources').getByRole('link',{name:'SQL Server connection timeout'}).click();await expect(page).toHaveURL(new RegExp('/knowledge/'+article));await expect(page.getByRole('link',{name:'Ask AI',exact:true})).toBeVisible();
});
test('workflow 2: article to assistant passes only article id and shows server citations',async({page})=>{
  await page.locator('.sidebar a[href="/knowledge"]').click();await page.locator('a[href="/knowledge/'+article+'"]').click();await page.getByRole('link',{name:'Ask AI',exact:true}).click();await expect(page).toHaveURL(new RegExp('documentId='+article));
  await page.getByLabel('Question about this article').fill('Why does SQL Server time out?');
  const request=page.waitForRequest(r=>r.url().includes('/assistant/knowledge/') && r.method()==='POST');
  await page.getByRole('button',{name:'Ask about article',exact:true}).click();expect((await request).postDataJSON()).toEqual({question:'Why does SQL Server time out?'});
  await expect(page.getByText('Article evidence: check SQL host and TCP port 1433.',{exact:true})).toBeVisible();await expect(page.getByRole('link',{name:'SQL Server connection timeout',exact:true})).toHaveAttribute('href','/knowledge/'+article);
  await expect(page.getByText('Recommendations require review. No action has been executed.',{exact:true})).toBeVisible();
  await page.addStyleTag({content:'*{transition:none!important;animation:none!important}'});
  for(const theme of ['light','dark']){await page.evaluate(t=>document.documentElement.dataset.theme=t,theme);expect((await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze()).violations).toEqual([]);for(const width of [280,390,1280]){await page.setViewportSize({width,height:900});expect(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth+1)).toBe(false);}await page.screenshot({path:`../artifacts/frontend/article-workflow-${theme}.png`,fullPage:true});}
});
test('workflow 3: ask by ticket reference, inspect structured response and saved history',async({page,request})=>{
  const data=await (await request.get('http://127.0.0.1:4918/helpdesk/api/tickets?page=1&pageSize=1')).json();const number=data.items[0].reference.replace(/\D/g,'');
  await page.locator('.sidebar a[href="/ai-assistant"]').first().click();await page.getByLabel('Your question',{exact:true}).fill(`Why is ticket #${number} failing?`);await page.getByRole('button',{name:'Send question',exact:true}).click();
  await expect(page.getByText('The reported SQL failure requires a connection-path investigation.',{exact:true})).toBeVisible();await expect(page.locator('.analysis-sources').getByRole('link',{name:'SQL Server connection timeout'})).toBeVisible();
  await expect(page.locator('.tool-activity')).toHaveCount(0);await expect(page.getByText('get_ticket_history',{exact:true})).toHaveCount(0);
  await page.locator('.sidebar a[href="/tickets"]').click();await page.locator('.sidebar a[href="/ai-assistant"]').first().click();await page.locator('.conversation-history li button').first().click();await expect(page.getByText('The reported SQL failure requires a connection-path investigation.',{exact:true})).toBeVisible();await expect(page.getByText('No changes have been applied.',{exact:false})).toHaveCount(0);await expect(page.locator('.tool-activity')).toHaveCount(0);
});
test('article provider failure shows an error without fabricated answer or sources',async({page,request})=>{
  await request.post('http://127.0.0.1:4918/helpdesk/__test/mode',{data:{mode:'llm-unavailable'}});
  await page.locator('.sidebar a[href="/knowledge"]').click();await page.locator('a[href="/knowledge/'+article+'"]').click();await page.getByRole('link',{name:'Ask AI',exact:true}).click();
  await page.getByLabel('Question about this article').fill('Explain the timeout');await page.getByRole('button',{name:'Ask about article',exact:true}).click();
  await expect(page.getByRole('alert')).toBeVisible();await expect(page.getByRole('heading',{name:'AI response',exact:true})).toHaveCount(0);await expect(page.getByRole('heading',{name:'Sources',exact:true})).toHaveCount(0);await expect(page.getByRole('button',{name:'Ask about article',exact:true})).toBeEnabled();
});

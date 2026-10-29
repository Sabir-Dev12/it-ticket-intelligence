import {test,expect} from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { writeFile } from 'node:fs/promises';
async function login(page:import('@playwright/test').Page,email='admin@example.test'){
  await page.goto('/login');await page.getByLabel('Email',{exact:true}).fill(email);await page.getByLabel('Password',{exact:true}).fill('synthetic-password-for-ui-only');await page.getByRole('button',{name:'Sign in',exact:true}).click();await expect(page).toHaveURL(email==='admin@example.test'?/dashboard/:/tickets/);
}
test.beforeEach(async({request})=>{await request.post('http://127.0.0.1:4918/helpdesk/__test/reset');});
for(const role of ['User','Agent','Support','Admin'])test(`administrator creates ${role} account with exact server contract`,async({page})=>{
  await login(page);await page.locator('.sidebar a[href="/admin/users"]').click();await expect(page.getByLabel('Role',{exact:true})).toHaveValue('User');
  await page.getByLabel('Full name',{exact:true}).fill('New '+role);await page.getByLabel('Email address',{exact:true}).fill(role.toLowerCase()+'-new@example.test');await page.getByLabel('Password',{exact:true}).fill('sample-password-for-ui-test');await page.getByLabel('Confirm password',{exact:true}).fill('sample-password-for-ui-test');await page.getByLabel('Role',{exact:true}).selectOption(role);
  const response=page.waitForResponse(r=>r.url().endsWith('/api/admin/users')&&r.request().method()==='POST');await page.getByRole('button',{name:'Create account',exact:true}).click();const posted=(await response).request().postDataJSON();expect(posted).toEqual({name:'New '+role,email:role.toLowerCase()+'-new@example.test',password:'sample-password-for-ui-test',role});
  await expect(page.locator('.action-feedback.success')).toContainText('Account created for');await expect(page.getByLabel('Password',{exact:true})).toHaveValue('');await expect(page.getByLabel('Confirm password',{exact:true})).toHaveValue('');await expect(page.locator('.account-list')).toContainText('New '+role);
  expect(await page.evaluate(()=>JSON.stringify({...localStorage,...sessionStorage}))).not.toContain('sample-password-for-ui-test');
});
test('validation, duplicate email and accessible themes',async({page})=>{
  await login(page);await page.locator('.sidebar a[href="/admin/users"]').click();await page.getByRole('button',{name:'Create account',exact:true}).click();await expect(page.locator('#account-name-error')).toContainText('Enter a name');
  await page.getByLabel('Full name',{exact:true}).fill('Existing');await page.getByLabel('Email address',{exact:true}).fill('admin@example.test');await page.getByLabel('Password',{exact:true}).fill('sample-password-for-ui-test');await page.getByLabel('Confirm password',{exact:true}).fill('sample-password-for-ui-test');await page.getByRole('button',{name:'Create account',exact:true}).click();await expect(page.getByRole('alert')).toContainText('already exists');await expect(page.getByLabel('Password',{exact:true})).toHaveValue('');
  await page.addStyleTag({content:'*{transition:none!important;animation:none!important}'});
  for(const theme of ['light','dark']){await page.evaluate(t=>document.documentElement.dataset.theme=t,theme);expect((await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze()).violations).toEqual([]);for(const width of [280,390,1280]){await page.setViewportSize({width,height:900});expect(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth+1)).toBe(false);}await page.getByLabel('Filter loaded accounts').focus();await page.evaluate(()=>window.scrollTo(0,0));await page.screenshot({path:`../artifacts/frontend/users-${theme}.png`,fullPage:true});await writeFile(`../artifacts/frontend/users-${theme}.html`,await page.evaluate(()=>{const html=document.documentElement.cloneNode(true) as HTMLElement;html.querySelectorAll('script,link').forEach(n=>n.remove());return '<!doctype html>'+html.outerHTML;}));}
});
for(const role of ['user','agent','support'])test(`${role} cannot open account administration`,async({page})=>{
  await login(page,role+'@example.test');await expect(page.locator('.sidebar a[href="/admin/users"]')).toHaveCount(0);
  await page.evaluate(()=>{history.pushState(null,'','/admin/users');dispatchEvent(new PopStateEvent('popstate'));});await expect(page).toHaveURL(/\/tickets$/);await expect(page.getByRole('heading',{name:'Create an account'})).toHaveCount(0);
});

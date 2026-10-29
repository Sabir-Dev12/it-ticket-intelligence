import { expect,test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
test('login rejects invalid credentials, signs in, never persists token and signs out',async({page})=>{
  await page.goto('/tickets');await expect(page).toHaveURL(/\/login$/);
  await page.getByLabel('Email',{exact:true}).fill('admin@example.test');await page.getByLabel('Password',{exact:true}).fill('incorrect');
  await page.getByRole('button',{name:'Sign in',exact:true}).click();await expect(page.getByRole('alert')).toContainText('Unable to sign in');
  await expect(page.getByLabel('Password',{exact:true})).toHaveValue('');
  await page.getByLabel('Password',{exact:true}).fill('synthetic-password-for-ui-only');await page.getByRole('button',{name:'Sign in',exact:true}).click();
  await expect(page).toHaveURL(/\/dashboard$/);
  const stored=await page.evaluate(()=>JSON.stringify({...localStorage,...sessionStorage}));expect(stored).not.toContain('synthetic-jwt');expect(stored).not.toContain('password');
  await page.getByRole('button',{name:'Sign out',exact:true}).click();await expect(page).toHaveURL(/\/login$/);
});
test('login matches the shared themes and passes axe and narrow layouts',async({page})=>{
  await page.goto('/login');
  await page.addStyleTag({content:'*{transition:none!important;animation:none!important}'});
  for(const theme of ['light','dark']){
    await page.evaluate(value=>{document.documentElement.dataset.theme=value;},theme);
    const result=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();expect(result.violations).toEqual([]);
    for(const width of [280,320,390,1280]){await page.setViewportSize({width,height:900});expect(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth+1)).toBe(false);}
    await page.screenshot({path:`../artifacts/frontend/login-${theme}.png`,fullPage:true});
  }
});

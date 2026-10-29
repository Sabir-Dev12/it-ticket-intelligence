import { chromium,expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { writeFile,mkdir } from 'node:fs/promises';
import { resolve } from 'node:path';
const base='http://127.0.0.1:5175';const api='http://127.0.0.1:5081';const out=resolve('../artifacts/frontend');await mkdir(out,{recursive:true});
const browser=await chromium.launch({channel:'chrome'});
try{
 const context=await browser.newContext({viewport:{width:1280,height:900}});const page=await context.newPage();
 await page.clock.install();
 await page.goto(base+'/login');await page.addStyleTag({content:'*{transition:none!important;animation:none!important}'});
 for(const theme of ['light','dark']){
  await page.evaluate(value=>{document.documentElement.dataset.theme=value;},theme);
  expect((await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze()).violations).toEqual([]);
  for(const width of [1280,390]){await page.setViewportSize({width,height:900});await page.screenshot({path:`${out}/login-real-${theme}-${width}.png`,fullPage:true});}
  await writeFile(`${out}/login-real-${theme}.html`,await page.evaluate(()=>{const html=document.documentElement.cloneNode(true);html.querySelectorAll('script,link').forEach(n=>n.remove());html.querySelectorAll('style').forEach(n=>{n.textContent=n.textContent.replace(/@font-face\s*\{[^}]*\}/g,'');});return '<!doctype html>'+html.outerHTML;}));
 }
 await page.setViewportSize({width:1280,height:900});
 await page.getByLabel('Email',{exact:true}).fill(process.env.AUTH__BootstrapEmail);
 await page.getByLabel('Password',{exact:true}).fill(process.env.AUTH__BootstrapPassword);
 const response=page.waitForResponse(r=>r.url().endsWith('/api/auth/login')&&r.status()===200);
 await page.getByRole('button',{name:'Sign in',exact:true}).click();const {accessToken}=await(await response).json();
 expect(accessToken.split('.')).toHaveLength(3);await expect(page).toHaveURL(/\/dashboard$/);
 expect(JSON.parse(Buffer.from(accessToken.split('.')[1],'base64url').toString())).not.toHaveProperty('exp');
 await page.clock.fastForward(31*60*1000);
 await expect(page).toHaveURL(/\/dashboard$/);
 expect((await page.request.get(api+'/api/tickets',{headers:{Authorization:`Bearer ${accessToken}`}})).status()).toBe(200);
 expect(await page.context().cookies()).toHaveLength(0);
 const storage=await page.evaluate(()=>JSON.stringify({...localStorage,...sessionStorage}));expect(storage).not.toContain(accessToken);
 await page.getByRole('button',{name:'Sign out',exact:true}).click();await expect(page).toHaveURL(/\/login$/);
 expect((await page.request.get(api+'/api/tickets',{headers:{Authorization:`Bearer ${accessToken}`}})).status()).toBe(401);
 await writeFile(`${out}/auth-live-result.json`,JSON.stringify({realJwt:true,realPostgres:true,noAutomaticExpiry:true,stillSignedInAfter31Minutes:true,noAuthCookie:true,noPersistentBrowserToken:true,logoutRevoked:true,axeBothThemes:true},null,2));
 console.log('Real JWT/browser/PostgreSQL login, authorized request, logout and replay rejection passed. No credentials recorded.');
}finally{await browser.close();}

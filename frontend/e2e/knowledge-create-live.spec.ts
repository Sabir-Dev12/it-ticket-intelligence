import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
test('admin imports and publishes a document visible in the library and indexed for RAG',async({page,request})=>{
  test.skip(!process.env.KNOWLEDGE_TEST_PASSWORD,'Requires a real local administrator account.');test.setTimeout(120000);
  await page.setViewportSize({width:1280,height:900});await page.goto('/login');
  await page.getByLabel('Email',{exact:true}).fill('admin.test@itticket.local');await page.getByLabel('Password',{exact:true}).fill(process.env.KNOWLEDGE_TEST_PASSWORD!);
  const login=page.waitForResponse(r=>r.url().endsWith('/api/auth/login'));await page.getByRole('button',{name:'Sign in',exact:true}).click();const session=await(await login).json();const headers={Authorization:`Bearer ${session.accessToken}`};
  await page.locator('.sidebar nav a[href="/knowledge"]').click();await page.getByRole('link',{name:'Add document',exact:true}).click();
  await page.getByRole('button',{name:'Save document',exact:true}).click();await expect(page.getByRole('alert')).toContainText('highlighted');
  const content='# SQL Server connection timeout\n\nWhen an application cannot connect to SQL Server, confirm the configured server name and TCP port with the database owner. Check DNS resolution, network reachability and approved firewall rules. Confirm SQL Server is running and listening on the expected port. Check authentication settings and connection pool saturation. Collect the exact error and timestamps before changing configuration. Production changes require operator approval.';
  await page.locator('#knowledge-file').setInputFiles({name:'sql-server-timeout.md',mimeType:'text/markdown',buffer:Buffer.from(content)});
  await expect(page.getByLabel('Content',{exact:true})).toHaveValue(content);
  await page.getByLabel('Title',{exact:true}).fill('[Demo] SQL Server connection timeout');
  await page.getByLabel('Source',{exact:true}).fill('demo/'+crypto.randomUUID()+'/sql-server-timeout.md');
  await page.addStyleTag({content:'*{transition:none!important;animation:none!important}'});
  for(const theme of ['light','dark']){await page.evaluate(t=>document.documentElement.dataset.theme=t,theme);expect((await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze()).violations).toEqual([]);for(const width of [280,320,414,1280]){await page.setViewportSize({width,height:900});expect(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth+1)).toBe(false);}await page.screenshot({path:`../artifacts/frontend/knowledge-create-${theme}.png`,fullPage:true});}
  const indexing=page.waitForResponse(r=>/\/api\/rag\/documents\/.+\/index$/.test(r.url())&&r.request().method()==='POST');
  await page.getByRole('button',{name:'Save document',exact:true}).click();const queued=await indexing;expect(queued.status()).toBe(202);const job=await queued.json();
  await expect(page).toHaveURL(/\/knowledge\/[a-f0-9-]+$/);await expect(page.locator('.document-content')).toContainText('SQL Server connection timeout');await expect(page.getByRole('status')).toContainText('Document saved');
  const documentId=page.url().split('/').at(-1)!;
  await page.getByRole('link',{name:'Back to knowledge',exact:true}).click();await page.getByRole('textbox',{name:'Search knowledge base'}).fill('[Demo] SQL Server connection timeout');await page.getByRole('button',{name:'Search',exact:true}).click();await expect(page.locator(`a[href="/knowledge/${documentId}"]`)).toBeVisible();
  await expect.poll(async()=>{const r=await request.get(`/api/rag/index-jobs/${job.id}`,{headers});expect(r.status()).toBe(200);return(await r.json()).state;},{timeout:60000,intervals:[1000,2000]}).toBe('Succeeded');
  const retrieved=await request.post('/api/rag/retrieve',{headers,data:{question:'SQL Server connection timeout',documentId,minimumScore:0,limit:5}});expect(retrieved.status()).toBe(200);const context=await retrieved.json();expect(context.results.length).toBeGreaterThan(0);expect(context.results[0].documentId).toBe(documentId);
});
test('support sees the library without document publishing controls',async({page,request})=>{
  test.skip(!process.env.KNOWLEDGE_TEST_SUPPORT_PASSWORD,'Requires a local Support test account.');
  await page.setViewportSize({width:1280,height:900});await page.goto('/login');
  await page.getByLabel('Email',{exact:true}).fill('support.test@itticket.local');await page.getByLabel('Password',{exact:true}).fill(process.env.KNOWLEDGE_TEST_SUPPORT_PASSWORD!);
  const login=page.waitForResponse(r=>r.url().endsWith('/api/auth/login'));await page.getByRole('button',{name:'Sign in',exact:true}).click();const session=await(await login).json();
  await page.locator('.sidebar nav a[href="/knowledge"]').click();await expect(page.getByRole('heading',{name:'Document library'})).toBeVisible();await expect(page.getByRole('link',{name:'Add document',exact:true})).toHaveCount(0);
  const denied=await request.post('/api/knowledge',{headers:{Authorization:`Bearer ${session.accessToken}`},data:{title:'Forbidden',content:'Support cannot publish.',source:'test.txt'}});expect(denied.status()).toBe(403);
});


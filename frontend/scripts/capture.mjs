import { chromium } from '@playwright/test';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
const out = resolve('../artifacts/frontend');
await mkdir(out, { recursive: true });
const browser = await chromium.launch({ channel: 'chrome' });
const page = await browser.newPage();
const routes = process.env.CAPTURE_ROUTES?.split(',') || ['dashboard', 'tickets', 'ai-assistant', 'knowledge', 'analytics', 'settings'];
for (const theme of ['light', 'dark']) {
  for (const width of [1280, 390]) {
    await page.setViewportSize({ width, height: width === 390 ? 844 : 980 });
    for (const route of routes) {
      await page.goto(`${process.env.FRONTEND_URL || 'http://127.0.0.1:5173'}/${route}`);
      await page.waitForFunction(() => !document.querySelector('[role="status"] h2'));
      if (process.env.CAPTURE_ACTION) await page.getByRole('button', { name: process.env.CAPTURE_ACTION, exact: true }).click();
      await page.evaluate(value => { document.documentElement.dataset.theme = value; localStorage.setItem('iti.theme', value); }, theme);
      await page.addStyleTag({ content: '*{transition:none!important;animation:none!important}' });
      await page.mouse.move(0, 0);
      const name = route.replaceAll('/', '-') + (process.env.CAPTURE_ACTION ? '-' + process.env.CAPTURE_ACTION.toLowerCase().replaceAll(' ', '-') : '');
      await page.screenshot({ path: `${out}/${name}-${theme}-${width}.png`, fullPage: true });
      if (width === 1280) {
        // Self-contained real DOM snapshot for the kit's file:// render gates.
        const html = await page.evaluate(() => {
          const root = document.documentElement.cloneNode(true);
          root.querySelectorAll('script, link').forEach(node => node.remove());
          root.querySelectorAll('style').forEach(node => { node.textContent = node.textContent.replace(/@font-face\s*\{[^}]*\}/g, ''); });
          return '<!doctype html>' + root.outerHTML;
        });
        await writeFile(`${out}/${name}-${theme}.html`, html);
      }
    }
  }
}
await browser.close();
console.log(`Captured ${routes.length * 4} screenshots and ${routes.length * 2} DOM snapshots from the real React app.`);

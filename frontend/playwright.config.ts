import { defineConfig } from '@playwright/test';
export default defineConfig({ testDir: './e2e', workers: 1, reporter: 'list',
  outputDir: '../artifacts/frontend/test-results',
  use: { baseURL: process.env.FRONTEND_URL || 'http://127.0.0.1:5173', channel: 'chrome', headless: true, trace: 'retain-on-failure' },
});

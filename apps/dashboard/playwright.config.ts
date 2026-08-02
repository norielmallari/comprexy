import { defineConfig, devices } from '@playwright/test';

/**
 * Dashboard Playwright smoke config.
 * Mocks control-api by default (page.route); live :8130 is not required for CI/merge.
 */
export default defineConfig({
  testDir: './e2e',
  // Evidence capture needs a live control-api; it runs from playwright.evidence.config.ts instead.
  testIgnore: ['**/evidence/**'],
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: process.env.CI ? 1 : undefined,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: 'http://127.0.0.1:3000',
    trace: 'on-first-retry',
    ...devices['Desktop Chrome'],
  },
  webServer: {
    command: 'npm run dev',
    url: 'http://127.0.0.1:3000',
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
  },
});

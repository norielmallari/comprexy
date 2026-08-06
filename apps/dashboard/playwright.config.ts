import { defineConfig, devices } from '@playwright/test';

/**
 * Dashboard Playwright smoke config.
 * Mocks control-api by default (page.route); live :8130 is not required for CI/merge.
 *
 * Always headless for default runs: no visible Chromium/Chrome window or Dock app on macOS.
 * Opt into a window only via `npm run test:e2e:headed` / `:ui` (sets PW_HEADED=1).
 */
const headed = process.env.PW_HEADED === '1';

if (!headed) {
  // Force chrome-headless-shell (no GUI Chromium binary) for every invoke of this config.
  process.env.PLAYWRIGHT_CHROMIUM_USE_HEADLESS_SHELL = '1';
}

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
    // Use localhost (not 127.0.0.1): Next.js 16 blocks cross-origin /_next
    // assets from 127.0.0.1 when the dev host is localhost, which breaks hydration.
    baseURL: 'http://localhost:3000',
    trace: 'on-first-retry',
    ...devices['Desktop Chrome'],
    headless: !headed,
  },
  webServer: {
    command: 'npm run dev',
    url: 'http://localhost:3000',
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
  },
});

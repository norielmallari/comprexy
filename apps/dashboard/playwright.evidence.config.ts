import { defineConfig, devices } from '@playwright/test';

/**
 * Opt-in evidence capture. Unlike the smoke config, this drives the real dashboard against a live
 * control-api (the bench harness points NEXT_PUBLIC_API_BASE_URL at its bench host) and only takes
 * screenshots. It is never part of the merge-default suite.
 *
 * Headless by default (same policy as playwright.config.ts). Set PW_HEADED=1 only for local debug.
 */
const headed = process.env.PW_HEADED === '1';

if (!headed) {
  process.env.PLAYWRIGHT_CHROMIUM_USE_HEADLESS_SHELL = '1';
}

export default defineConfig({
  testDir: './e2e/evidence',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: [['list']],
  use: {
    baseURL: 'http://localhost:3000',
    ...devices['Desktop Chrome'],
    viewport: { width: 1600, height: 1000 },
    headless: !headed,
  },
  webServer: {
    command: 'npm run dev',
    url: 'http://localhost:3000',
    reuseExistingServer: true,
    timeout: 120_000,
  },
});

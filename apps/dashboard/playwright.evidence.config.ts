import { defineConfig, devices } from '@playwright/test';

/**
 * Opt-in evidence capture. Unlike the smoke config, this drives the real dashboard against a live
 * control-api (the bench harness points NEXT_PUBLIC_API_BASE_URL at its bench host) and only takes
 * screenshots. It is never part of the merge-default suite.
 */
export default defineConfig({
  testDir: './e2e/evidence',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: [['list']],
  use: {
    baseURL: 'http://127.0.0.1:3000',
    ...devices['Desktop Chrome'],
    viewport: { width: 1600, height: 1000 },
  },
  webServer: {
    command: 'npm run dev',
    url: 'http://127.0.0.1:3000',
    reuseExistingServer: true,
    timeout: 120_000,
  },
});

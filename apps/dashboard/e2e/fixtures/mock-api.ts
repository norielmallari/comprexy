import type { Page } from '@playwright/test';

import conversations from './data/conversations.json';

/** Default control-api origin used by the dashboard when env is unset. */
export const CONTROL_API_ORIGIN = 'http://localhost:8130';

/**
 * Register mocked control-api routes for smoke tests.
 * Keeps merge-default Playwright green without a live :8130 process.
 */
export async function mockControlApi(page: Page): Promise<void> {
  await page.route(`${CONTROL_API_ORIGIN}/health`, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ status: 'Healthy' }),
    });
  });

  await page.route(
    (url) =>
      url.origin === CONTROL_API_ORIGIN &&
      url.pathname === '/v1/comprexy/conversations',
    async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: conversations, total: conversations.length }),
      });
    },
  );
}

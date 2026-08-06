import { expect, test } from '@playwright/test';

import {
  MOCK_CONVERSATION_ID,
  MOCK_DASHBOARD_API_KEY,
  mockControlApi,
} from '../fixtures/mock-api';

test.describe('auth + cost picker smoke', () => {
  test('login stores key and subsequent /v1 calls send auth headers', async ({
    page,
  }) => {
    const authHeaders: string[] = [];

    await mockControlApi(page, {
      requireAuth: true,
      onV1Request: (request) => {
        const authorization = request.headers()['authorization'];
        if (authorization) {
          authHeaders.push(authorization);
        }
      },
    });

    await page.goto(`/?conv=${MOCK_CONVERSATION_ID}`);

    await expect(
      page.getByRole('dialog', { name: 'Dashboard API key' }),
    ).toBeVisible();

    await page
      .getByTestId('login-gate')
      .getByRole('textbox', { name: 'API key' })
      .fill(MOCK_DASHBOARD_API_KEY);
    await page.getByRole('button', { name: 'Save key' }).click();

    await expect(
      page.getByRole('dialog', { name: 'Dashboard API key' }),
    ).not.toBeVisible();

    await expect(
      page.getByRole('heading', { name: 'Comprexy Metrics' }),
    ).toBeVisible();
    await expect(
      page.getByRole('region', { name: 'Input tokens', exact: true }),
    ).toBeVisible();

    expect(authHeaders.some((h) => h === `Bearer ${MOCK_DASHBOARD_API_KEY}`)).toBe(
      true,
    );
  });

  test('Local hides `$`; Sonnet shows `$` on metrics I/O cards', async ({
    page,
  }) => {
    await mockControlApi(page);
    await page.goto(`/?conv=${MOCK_CONVERSATION_ID}`);

    const costSelect = page.getByRole('combobox', { name: 'Cost model' });
    await expect(costSelect).toBeVisible();

    // Default Local — tokens only
    await costSelect.selectOption('local');
    const ioStrip = page.getByTestId('conversation-io-cards');
    await expect(ioStrip).toBeVisible();
    await expect(ioStrip).not.toContainText('$');

    await costSelect.selectOption('claude-sonnet-5');
    // 8000 * $3/1M = $0.0240; 600 * $15/1M = $0.0090
    await expect(ioStrip).toContainText('$0.0240');
    await expect(ioStrip).toContainText('$0.0090');
  });

  test('Escape closes the login dialog', async ({ page }) => {
    // Open manually (no requireAuth) so late 401 notifies cannot reopen after Escape.
    await mockControlApi(page);
    await page.goto(`/?conv=${MOCK_CONVERSATION_ID}`);

    await page.getByRole('button', { name: 'Enter dashboard API key' }).click();

    const dialog = page.getByRole('dialog', { name: 'Dashboard API key' });
    await expect(dialog).toBeVisible();

    await page.keyboard.press('Escape');

    await expect(dialog).not.toBeVisible();
  });
});

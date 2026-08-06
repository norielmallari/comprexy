import { expect, test } from '@playwright/test';

import {
  MOCK_CONVERSATION_ID,
  MOCK_DASHBOARD_API_KEY,
  mockControlApi,
} from '../fixtures/mock-api';

test.describe('settings page smoke', () => {
  test('shows PassThrough wins banner from mocked settings', async ({ page }) => {
    await mockControlApi(page);
    await page.goto('/settings');

    await expect(
      page.getByRole('heading', { name: 'Comprexy Settings' }),
    ).toBeVisible();
    await expect(page.getByTestId('settings-page')).toBeVisible();
    await expect(page.getByTestId('passthrough-wins-banner')).toBeVisible();
    await expect(page.getByRole('status')).toContainText(/PassThrough wins/i);
  });

  test('401 then Save key shows settings form without Retry', async ({ page }) => {
    await mockControlApi(page, { requireAuth: true });
    await page.goto('/settings');

    await expect(
      page.getByRole('dialog', { name: 'Dashboard API key' }),
    ).toBeVisible();

    await page
      .getByTestId('login-gate')
      .getByLabel('API key')
      .fill(MOCK_DASHBOARD_API_KEY);
    await page.getByRole('button', { name: 'Save key' }).click();

    await expect(
      page.getByRole('dialog', { name: 'Dashboard API key' }),
    ).not.toBeVisible();
    await expect(page.getByTestId('settings-form')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Retry' })).toHaveCount(0);
  });

  test('Escape closes login dialog on settings', async ({ page }) => {
    await mockControlApi(page, { requireAuth: true });
    await page.goto('/settings');

    const dialog = page.getByRole('dialog', { name: 'Dashboard API key' });
    await expect(dialog).toBeVisible();

    await page.keyboard.press('Escape');

    await expect(dialog).not.toBeVisible();
  });

  test('Tab from Dismiss stays inside login dialog', async ({ page }) => {
    await mockControlApi(page, { requireAuth: true });
    await page.goto('/settings');

    const dialog = page.getByRole('dialog', { name: 'Dashboard API key' });
    await expect(dialog).toBeVisible();

    const keyInput = page.getByTestId('login-gate').getByRole('textbox', { name: 'API key' });
    await page.getByRole('button', { name: 'Dismiss' }).focus();
    await page.keyboard.press('Tab');

    await expect(keyInput).toBeFocused();
  });

  test('metrics page shows accessible N/A for null effective settings', async ({
    page,
  }) => {
    await mockControlApi(page);
    await page.goto(`/?conv=${MOCK_CONVERSATION_ID}`);

    await expect(
      page.getByRole('region', { name: 'Conversation effective settings' }),
    ).toBeVisible();
    await expect(
      page.getByLabel('Effective settings not available'),
    ).toHaveText('N/A');
  });
});

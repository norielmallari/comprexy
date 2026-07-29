import { expect, test } from '@playwright/test';

import { mockControlApi } from '../fixtures/mock-api';

test.describe('dashboard load', () => {
  test('shell renders with mocked control-api', async ({ page }) => {
    await mockControlApi(page);

    await page.goto('/');

    await expect(
      page.getByRole('heading', { name: 'Comprexy Metrics' }),
    ).toBeVisible();
    await expect(page.getByRole('main')).toBeVisible();
  });
});

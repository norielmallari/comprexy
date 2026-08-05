import { expect, test } from '@playwright/test';

import { MOCK_CONVERSATION_ID, mockControlApi } from '../fixtures/mock-api';

test.describe('token counts chart', () => {
  test.beforeEach(async ({ page }) => {
    await mockControlApi(page);
    await page.goto(`/?conv=${MOCK_CONVERSATION_ID}`);
  });

  test('renders the prepared-prompt stack with a SoftBudget IR-full ghost behind it', async ({ page }) => {
    const chart = page.getByTestId('token-counts-by-turn-chart');
    await expect(chart).toBeVisible();
    await expect(chart).toHaveAttribute('aria-label', /3 turns/);

    // One dashed ghost rectangle per turn.
    const ghostBars = chart.locator('path[stroke-dasharray="3 2"]');
    await expect(ghostBars).toHaveCount(3);

    // Recharts paints series in declaration order, so the ghost layer must come first.
    const barLayers = chart.locator('g.recharts-bar');
    await expect(barLayers).toHaveCount(4);
    await expect(barLayers.first().locator('path[stroke-dasharray="3 2"]')).toHaveCount(3);
  });

  test('legend names the segments and drops the unsourced overhead entry', async ({ page }) => {
    const legend = page.getByTestId('chart-legend');

    await expect(legend.getByText('System', { exact: true })).toBeVisible();
    await expect(legend.getByText('History + tools', { exact: true })).toBeVisible();
    await expect(legend.getByText('Compressed WM', { exact: true })).toBeVisible();
    await expect(legend.getByText('SoftBudget (IR full)', { exact: true })).toBeVisible();

    // Per-turn overhead has no source in the turns DTO; the entry was removed rather than zeroed.
    await expect(legend.getByText('Overhead', { exact: true })).toHaveCount(0);
    await expect(legend.getByText('Prompt', { exact: true })).toHaveCount(0);
  });

  test('tooltip reports no working memory on turns before the first version', async ({ page }) => {
    const chart = page.getByTestId('token-counts-by-turn-chart');
    await expect(chart).toBeVisible();

    const box = await chart.boundingBox();
    expect(box).not.toBeNull();

    // Hover the first of three turn bands.
    await page.mouse.move(box!.x + box!.width * 0.2, box!.y + box!.height * 0.7);

    const tooltip = page.getByTestId('chart-tooltip');
    await expect(tooltip).toBeVisible();
    await expect(tooltip).toContainText('Turn 1');
    await expect(tooltip).toContainText('No working memory yet');
  });
});

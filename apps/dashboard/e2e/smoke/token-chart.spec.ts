import { expect, test } from '@playwright/test';

import { MOCK_CONVERSATION_ID, mockControlApi } from '../fixtures/mock-api';

test.describe('token counts chart', () => {
  test.beforeEach(async ({ page }) => {
    await mockControlApi(page);
    await page.goto(`/?conv=${MOCK_CONVERSATION_ID}`);
  });

  test('renders the prepared-prompt stack with a Full History Est. ghost behind it', async ({ page }) => {
    const chart = page.getByTestId('token-counts-by-turn-chart');
    await expect(chart).toBeVisible();
    await expect(chart).toHaveAttribute('aria-label', /3 turns/);

    // One dashed ghost rectangle per turn.
    const ghostBars = chart.locator('path[stroke-dasharray="3 2"]');
    await expect(ghostBars).toHaveCount(3);

    // Ghost + System + Virtual tools + Client tools + History + Compressed WM (Rules omitted at 0).
    const barLayers = chart.locator('g.recharts-bar');
    await expect(barLayers).toHaveCount(6);
    await expect(barLayers.first().locator('path[stroke-dasharray="3 2"]')).toHaveCount(3);
  });

  test('legend names catalog and history segments without History + tools', async ({ page }) => {
    const legend = page.getByTestId('chart-legend');

    await expect(legend.getByText('System', { exact: true })).toBeVisible();
    await expect(legend.getByText('Virtual tools', { exact: true })).toBeVisible();
    await expect(legend.getByText('Client tools', { exact: true })).toBeVisible();
    await expect(legend.getByText('History', { exact: true })).toBeVisible();
    await expect(legend.getByText('Compressed WM', { exact: true })).toBeVisible();
    await expect(legend.getByText('Full History Est.', { exact: true })).toBeVisible();

    await expect(legend.getByText('History + tools', { exact: true })).toHaveCount(0);
    await expect(legend.getByText('Rules', { exact: true })).toHaveCount(0);
    await expect(legend.getByText('VT / native-wire', { exact: true })).toHaveCount(0);
    await expect(legend.getByText('Overhead', { exact: true })).toHaveCount(0);
    await expect(legend.getByText('Prompt', { exact: true })).toHaveCount(0);
  });

  test('tooltip reports history, catalog rows, and a separate VT channel', async ({ page }) => {
    const chart = page.getByTestId('token-counts-by-turn-chart');
    await expect(chart).toBeVisible();

    // Wait for Recharts bars, then hover the first turn's solid segment.
    const solidBars = chart.locator('g.recharts-bar path:not([stroke-dasharray])');
    await expect(solidBars.first()).toBeVisible();
    await solidBars.first().hover({ force: true });

    const tooltip = page.getByTestId('chart-tooltip');
    await expect(tooltip).toBeVisible();
    await expect(tooltip).toContainText('Turn 1');
    await expect(tooltip).toContainText('No working memory yet');
    await expect(tooltip).toContainText('History');
    await expect(tooltip).toContainText('Virtual tools (catalog)');
    await expect(tooltip).toContainText('Client tools (catalog)');
    await expect(tooltip).toContainText('VT / native-wire');
    await expect(tooltip).not.toContainText('History + tools');
    await expect(tooltip).not.toContainText('Rules');
  });
});

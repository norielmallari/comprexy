import { expect, test } from '@playwright/test';

import metricsSummary from '../fixtures/data/metrics-summary.json';
import turns from '../fixtures/data/turns.json';
import { MOCK_CONVERSATION_ID, mockControlApi } from '../fixtures/mock-api';

/**
 * Fixture fields must stay distinct so source-field regressions are visible:
 * averageTokenSavingsRatio ≠ best turn ratio, baseline ≠ actual, overhead ≠ either.
 */
test.describe('dashboard metric cards', () => {
  test.beforeEach(async ({ page }) => {
    await mockControlApi(page);
    await page.goto(`/?conv=${MOCK_CONVERSATION_ID}`);
  });

  test('renders twelve named metric regions with fixture values', async ({
    page,
  }) => {
    const bestRatio = Math.max(
      ...turns.map((turn) => turn.netTokenSavingsRatio),
    );
    const averageRatio =
      turns.reduce((sum, turn) => sum + turn.netTokenSavingsRatio, 0) /
      turns.length;
    const maxWm = Math.max(
      ...turns.map((turn) => turn.workingMemoryVersionUsed ?? 0),
    );
    const overheadPct = (
      (metricsSummary.totalCompressionOverheadTokens /
        metricsSummary.totalBaselineTokensEstimated) *
      100
    ).toFixed(1);

    // Guard: fixture fields remain distinct for source-field regression detection.
    expect(metricsSummary.averageTokenSavingsRatio).not.toBe(bestRatio);
    expect(metricsSummary.averageTokenSavingsRatio).not.toBe(averageRatio);
    expect(metricsSummary.totalBaselineTokensEstimated).not.toBe(
      metricsSummary.totalActualTokensEstimated,
    );
    expect(metricsSummary.totalCompressionOverheadTokens).not.toBe(
      metricsSummary.totalBaselineTokensEstimated,
    );
    expect(metricsSummary.totalCompressionOverheadTokens).not.toBe(
      metricsSummary.totalActualTokensEstimated,
    );

    await expect(
      page.getByRole('region', { name: 'Tokens Saved' }),
    ).toBeVisible();
    await expect(
      page.getByRole('region', { name: 'Tokens Saved' }),
    ).toContainText('4,000');

    await expect(
      page.getByRole('region', { name: 'Weighted Compression' }),
    ).toBeVisible();
    await expect(
      page.getByRole('region', { name: 'Weighted Compression' }),
    ).toContainText('33.0');

    await expect(
      page.getByRole('region', { name: 'Average Compression' }),
    ).toContainText((averageRatio * 100).toFixed(1));

    await expect(
      page.getByRole('region', { name: 'Baseline (combined)' }),
    ).toBeVisible();
    await expect(
      page.getByRole('region', { name: 'Baseline (combined)' }),
    ).toContainText('12,600');

    await expect(
      page.getByRole('region', { name: 'Actual (combined)' }),
    ).toBeVisible();
    await expect(
      page.getByRole('region', { name: 'Actual (combined)' }),
    ).toContainText('8,600');

    await expect(
      page.getByRole('region', { name: 'Best Compression' }),
    ).toBeVisible();
    await expect(
      page.getByRole('region', { name: 'Best Compression' }),
    ).toContainText((bestRatio * 100).toFixed(1));

    await expect(page.getByRole('region', { name: 'Overhead' })).toBeVisible();
    await expect(page.getByRole('region', { name: 'Overhead' })).toContainText(
      overheadPct,
    );

    await expect(
      page.getByRole('region', { name: 'Working Memory' }),
    ).toBeVisible();
    await expect(
      page.getByRole('region', { name: 'Working Memory' }),
    ).toContainText(`v${maxWm}`);

    await expect(
      page.getByTestId('effective-settings-snapshot'),
    ).toBeVisible();
    await expect(page.getByTestId('effective-settings-na')).toHaveText('N/A');

    await expect(
      page.getByRole('region', { name: 'Raw input tokens', exact: true }),
    ).toBeVisible();
    await expect(
      page.getByRole('region', { name: 'Raw input tokens', exact: true }),
    ).toContainText('12,000');

    // exact: true — "Input tokens" is a substring of "Raw input tokens"
    await expect(
      page.getByRole('region', { name: 'Input tokens', exact: true }),
    ).toBeVisible();
    await expect(
      page.getByRole('region', { name: 'Input tokens', exact: true }),
    ).toContainText('8,000');

    await expect(
      page.getByRole('region', { name: 'Output tokens', exact: true }),
    ).toBeVisible();
    await expect(
      page.getByRole('region', { name: 'Output tokens', exact: true }),
    ).toContainText('600');

    await expect(
      page.getByRole('region', { name: 'Virtual Tools channel', exact: true }),
    ).toBeVisible();
    await expect(
      page.getByRole('region', { name: 'Virtual Tools channel', exact: true }),
    ).toContainText('1,000');

    await expect(
      page.getByRole('region', { name: 'Baseline Tokens' }),
    ).toHaveCount(0);
    await expect(
      page.getByRole('region', { name: 'Actual Tokens' }),
    ).toHaveCount(0);
    await expect(
      page.getByRole('region', { name: 'Compression Ratios' }),
    ).toHaveCount(0);
    await expect(
      page.getByRole('region', { name: 'Baseline vs Actual Tokens' }),
    ).toHaveCount(0);
    await expect(
      page.getByRole('region', { name: 'Compression Health' }),
    ).toHaveCount(0);
    await expect(
      page.getByRole('region', { name: 'Budget Triggers' }),
    ).toHaveCount(0);
  });

  test('keeps the token chart after selecting the mocked conversation', async ({
    page,
  }) => {
    const chart = page.getByTestId('token-counts-by-turn-chart');
    await expect(chart).toBeVisible();
    await expect(chart).toHaveAttribute('aria-label', /3 turns/);
  });

  test('lays out token I/O and state as the two halves of row three', async ({
    page,
  }) => {
    const topLeft = await page.getByTestId('metrics-top-left').boundingBox();
    const topRight = await page.getByTestId('metrics-top-right').boundingBox();
    const bottomLeft = await page.getByTestId('metrics-bottom-left').boundingBox();
    const bottomRight = await page.getByTestId('metrics-bottom-right').boundingBox();
    const thirdLeft = await page.getByTestId('metrics-third-left').boundingBox();
    const thirdRight = await page.getByTestId('metrics-third-right').boundingBox();

    expect(topLeft).not.toBeNull();
    expect(topRight).not.toBeNull();
    expect(bottomLeft).not.toBeNull();
    expect(bottomRight).not.toBeNull();
    expect(thirdLeft).not.toBeNull();
    expect(thirdRight).not.toBeNull();

    expect(topLeft!.x).toBeLessThan(topRight!.x);
    expect(bottomLeft!.x).toBeLessThan(bottomRight!.x);
    expect(bottomLeft!.y).toBeGreaterThan(topLeft!.y);
    expect(bottomRight!.y).toBeGreaterThan(topRight!.y);
    expect(thirdLeft!.x).toBe(topLeft!.x);
    expect(thirdLeft!.width).toBe(topLeft!.width);
    expect(thirdRight!.x).toBe(topRight!.x);
    expect(thirdRight!.width).toBe(topRight!.width);
    expect(thirdLeft!.y).toBeGreaterThan(bottomLeft!.y);
    expect(thirdRight!.y).toBeGreaterThan(bottomRight!.y);
  });

  test('places the I/O strip inside the third-left half', async ({ page }) => {
    const thirdLeft = await page.getByTestId('metrics-third-left').boundingBox();
    const ioStrip = await page.getByTestId('conversation-io-cards').boundingBox();

    expect(thirdLeft).not.toBeNull();
    expect(ioStrip).not.toBeNull();
    expect(ioStrip!.x).toBe(thirdLeft!.x);
    expect(ioStrip!.y).toBe(thirdLeft!.y);
    expect(ioStrip!.width).toBe(thirdLeft!.width);
  });
});

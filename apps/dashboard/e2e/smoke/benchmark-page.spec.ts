import { expect, test } from '@playwright/test';

import benchmarkRun from '../fixtures/data/benchmark-run.json';
import { mockBenchmarkApi } from '../fixtures/mock-api';

test.describe('benchmark page', () => {
  test('loads with telemetry panel and mode toggle', async ({ page }) => {
    await mockBenchmarkApi(page);

    await page.goto('/benchmark');

    await expect(
      page.getByRole('heading', { name: 'Comprexy Benchmark' }),
    ).toBeVisible();
    await expect(page.getByTestId('benchmark-mode-toggle')).toBeVisible();
    await expect(page.getByTestId('telemetry-panel')).toBeVisible();
    await expect(page.getByTestId('comparison-panel')).toHaveCount(0);
    await expect(page.getByTestId('cost-model-disclaimer')).toBeVisible();
  });

  test('switches to comparison mode', async ({ page }) => {
    await mockBenchmarkApi(page);
    await page.goto('/benchmark');

    await page.getByTestId('benchmark-mode-comparison').click();

    await expect(page.getByTestId('comparison-panel')).toBeVisible();
    await expect(page.getByTestId('telemetry-panel')).toHaveCount(0);
  });

  test('starts a benchmark run and shows status from mock', async ({ page }) => {
    await mockBenchmarkApi(page);
    await page.goto('/benchmark');

    await page.getByLabel('fixture-scenario-a (5 prompts)').check();
    await page.getByTestId('benchmark-ack-checkbox').check();
    await page.getByTestId('start-benchmark-button').click();

    await expect(page.getByTestId('run-status-panel')).toBeVisible();
    await expect(page.getByText(`Run ${benchmarkRun.runId}`)).toBeVisible();
    await expect(page.getByTestId('run-status-badge')).toContainText(
      'Run completed successfully',
    );
  });

  test('auto-fills comparison mode with distinct chart testids after completed run', async ({
    page,
  }) => {
    await mockBenchmarkApi(page);
    await page.goto('/benchmark');

    await page.getByLabel('fixture-scenario-a (5 prompts)').check();
    await page.getByTestId('benchmark-ack-checkbox').check();
    await page.getByTestId('start-benchmark-button').click();

    await expect(page.getByTestId('comparison-panel')).toBeVisible();
    await expect(page.getByTestId('baseline-token-chart')).toBeVisible();
    await expect(page.getByTestId('compare-token-chart')).toBeVisible();
    await expect(page.getByTestId('telemetry-panel')).toHaveCount(0);
  });

  test('shows report-failed badge when mock run phase is completed_with_report_error', async ({
    page,
  }) => {
    await mockBenchmarkApi(page);

    await page.route(
      (url) =>
        url.pathname.includes('/v1/comprexy/benchmarks/runs/') &&
        !url.pathname.endsWith('/runs'),
      async (route) => {
        if (route.request().method() !== 'GET') {
          await route.continue();
          return;
        }
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            ...benchmarkRun,
            phase: 'completed_with_report_error',
            lastError: 'Report agent fixture failure',
          }),
        });
      },
    );

    await page.goto('/benchmark');

    await page.getByLabel('fixture-scenario-a (5 prompts)').check();
    await page.getByTestId('benchmark-ack-checkbox').check();
    await page.getByTestId('start-benchmark-button').click();

    await expect(page.getByTestId('run-status-badge')).toContainText(
      'Run finished; report failed',
    );
  });
});

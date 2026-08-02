import type { Page } from '@playwright/test';

import benchmarkPresentation from './data/benchmark-presentation.json';
import benchmarkRun from './data/benchmark-run.json';
import benchmarkScenarios from './data/benchmark-scenarios.json';
import benchmarkTelemetry from './data/benchmark-telemetry.json';
import conversations from './data/conversations.json';
import metricsSummary from './data/metrics-summary.json';
import turns from './data/turns.json';

/** Default control-api origin used by the dashboard when env is unset. */
export const CONTROL_API_ORIGIN = 'http://localhost:8130';

/** Conversation the mocked metrics and turns belong to. */
export const MOCK_CONVERSATION_ID = conversations[0].conversationId;

export const MOCK_BASELINE_ID = benchmarkPresentation.baselineConversationId;
export const MOCK_COMPARE_ID = benchmarkPresentation.compareConversationId;

export { turns as mockTurns };

const BENCHMARK_PREFIX = `${CONTROL_API_ORIGIN}/v1/comprexy/benchmarks`;

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

  await page.route(
    (url) =>
      url.origin === CONTROL_API_ORIGIN &&
      url.pathname.endsWith('/metrics/turns'),
    async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(turns),
      });
    },
  );

  await page.route(
    (url) =>
      url.origin === CONTROL_API_ORIGIN &&
      url.pathname.endsWith('/metrics'),
    async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(metricsSummary),
      });
    },
  );
}

/**
 * Register benchmark control-api routes for /benchmark smoke tests.
 */
export async function mockBenchmarkApi(page: Page): Promise<void> {
  await mockControlApi(page);

  await page.route(`${BENCHMARK_PREFIX}/scenarios`, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(benchmarkScenarios),
    });
  });

  await page.route(`${BENCHMARK_PREFIX}/runs`, async (route) => {
    if (route.request().method() === 'GET') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([benchmarkRun]),
      });
      return;
    }
    if (route.request().method() === 'POST') {
      await route.fulfill({
        status: 202,
        contentType: 'application/json',
        body: JSON.stringify({ runId: benchmarkRun.runId }),
      });
      return;
    }
    await route.continue();
  });

  await page.route(
    (url) =>
      url.origin === CONTROL_API_ORIGIN &&
      url.pathname.match(/\/v1\/comprexy\/benchmarks\/runs\/[^/]+$/) !== null,
    async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(benchmarkRun),
      });
    },
  );

  await page.route(
    (url) =>
      url.origin === CONTROL_API_ORIGIN &&
      url.pathname.endsWith('/presentation'),
    async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(benchmarkPresentation),
      });
    },
  );

  await page.route(`${BENCHMARK_PREFIX}/presentation/telemetry`, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(benchmarkTelemetry),
    });
  });

  await page.route(`${BENCHMARK_PREFIX}/presentation/compare`, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(benchmarkPresentation),
    });
  });

  await page.route(
    (url) =>
      url.origin === CONTROL_API_ORIGIN &&
      url.pathname.includes('/cancel'),
    async (route) => {
      await route.fulfill({ status: 204, body: '' });
    },
  );

  await page.route(
    (url) =>
      url.origin === CONTROL_API_ORIGIN &&
      url.pathname.includes('/report'),
    async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ runId: benchmarkRun.runId, exitCode: 0 }),
      });
    },
  );
}

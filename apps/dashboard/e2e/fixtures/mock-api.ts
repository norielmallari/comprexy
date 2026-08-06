import type { Page, Request } from '@playwright/test';

import benchmarkPresentation from './data/benchmark-presentation.json';
import benchmarkRun from './data/benchmark-run.json';
import benchmarkScenarios from './data/benchmark-scenarios.json';
import benchmarkTelemetry from './data/benchmark-telemetry.json';
import conversations from './data/conversations.json';
import costModels from './data/cost-models.json';
import metricsSummary from './data/metrics-summary.json';
import settings from './data/settings.json';
import turns from './data/turns.json';

/** Default control-api origin used by the dashboard when env is unset. */
export const CONTROL_API_ORIGIN = 'http://localhost:8130';

/** Conversation the mocked metrics and turns belong to. */
export const MOCK_CONVERSATION_ID = conversations[0].conversationId;

export const MOCK_BASELINE_ID = benchmarkPresentation.baselineConversationId;
export const MOCK_COMPARE_ID = benchmarkPresentation.compareConversationId;

/** Synthetic dashboard API key used by auth smoke tests. */
export const MOCK_DASHBOARD_API_KEY = 'synthetic-dashboard-key';

export { turns as mockTurns, costModels as mockCostModels, settings as mockSettings };

const BENCHMARK_PREFIX = `${CONTROL_API_ORIGIN}/v1/comprexy/benchmarks`;

export type MockControlApiOptions = {
  /** When true, /v1 routes require Authorization / X-Api-Key matching MOCK_DASHBOARD_API_KEY. */
  requireAuth?: boolean;
  /** Captures Authorization header values seen on /v1 requests (excluding /health). */
  onV1Request?: (request: Request) => void;
};

function hasValidAuth(request: Request): boolean {
  const headers = request.headers();
  const bearer = headers['authorization'];
  const apiKey = headers['x-api-key'];
  return (
    bearer === `Bearer ${MOCK_DASHBOARD_API_KEY}` || apiKey === MOCK_DASHBOARD_API_KEY
  );
}

async function fulfillJson(
  route: Parameters<Parameters<Page['route']>[1]>[0],
  body: unknown,
  status = 200,
): Promise<void> {
  await route.fulfill({
    status,
    contentType: 'application/json',
    body: JSON.stringify(body),
  });
}

/**
 * Register mocked control-api routes for smoke tests.
 * Keeps merge-default Playwright green without a live :8130 process.
 */
export async function mockControlApi(
  page: Page,
  options: MockControlApiOptions = {},
): Promise<void> {
  const { requireAuth = false, onV1Request } = options;

  await page.route(`${CONTROL_API_ORIGIN}/health`, async (route) => {
    await fulfillJson(route, { status: 'Healthy' });
  });

  const guard = async (
    route: Parameters<Parameters<Page['route']>[1]>[0],
  ): Promise<boolean> => {
    const request = route.request();
    onV1Request?.(request);
    if (requireAuth && !hasValidAuth(request)) {
      await fulfillJson(route, { message: 'Unauthorized' }, 401);
      return false;
    }
    return true;
  };

  await page.route(
    (url) =>
      url.origin === CONTROL_API_ORIGIN &&
      url.pathname === '/v1/comprexy/cost-models',
    async (route) => {
      if (!(await guard(route))) return;
      await fulfillJson(route, costModels);
    },
  );

  await page.route(
    (url) =>
      url.origin === CONTROL_API_ORIGIN &&
      url.pathname === '/v1/comprexy/settings',
    async (route) => {
      if (!(await guard(route))) return;
      if (route.request().method() === 'PUT') {
        const body = route.request().postDataJSON() as {
          revision?: number;
          settings?: unknown;
        };
        await fulfillJson(route, {
          revision: (body.revision ?? settings.revision) + 1,
          settings: body.settings ?? settings.settings,
          updatedAt: '2026-01-15T13:00:00.000Z',
        });
        return;
      }
      await fulfillJson(route, settings);
    },
  );

  await page.route(
    (url) =>
      url.origin === CONTROL_API_ORIGIN &&
      url.pathname === '/v1/comprexy/conversations',
    async (route) => {
      if (!(await guard(route))) return;
      await fulfillJson(route, {
        data: conversations,
        total: conversations.length,
      });
    },
  );

  await page.route(
    (url) =>
      url.origin === CONTROL_API_ORIGIN &&
      url.pathname.endsWith('/metrics/turns'),
    async (route) => {
      if (!(await guard(route))) return;
      await fulfillJson(route, turns);
    },
  );

  await page.route(
    (url) =>
      url.origin === CONTROL_API_ORIGIN &&
      url.pathname.endsWith('/metrics'),
    async (route) => {
      if (!(await guard(route))) return;
      await fulfillJson(route, metricsSummary);
    },
  );
}

/**
 * Register benchmark control-api routes for /benchmark smoke tests.
 */
export async function mockBenchmarkApi(page: Page): Promise<void> {
  await mockControlApi(page);

  await page.route(`${BENCHMARK_PREFIX}/scenarios`, async (route) => {
    await fulfillJson(route, benchmarkScenarios);
  });

  await page.route(`${BENCHMARK_PREFIX}/runs`, async (route) => {
    if (route.request().method() === 'GET') {
      await fulfillJson(route, [benchmarkRun]);
      return;
    }
    if (route.request().method() === 'POST') {
      await fulfillJson(route, { runId: benchmarkRun.runId }, 202);
      return;
    }
    await route.continue();
  });

  await page.route(
    (url) =>
      url.origin === CONTROL_API_ORIGIN &&
      url.pathname.match(/\/v1\/comprexy\/benchmarks\/runs\/[^/]+$/) !== null,
    async (route) => {
      await fulfillJson(route, benchmarkRun);
    },
  );

  await page.route(
    (url) =>
      url.origin === CONTROL_API_ORIGIN &&
      url.pathname.endsWith('/presentation'),
    async (route) => {
      await fulfillJson(route, benchmarkPresentation);
    },
  );

  await page.route(`${BENCHMARK_PREFIX}/presentation/telemetry`, async (route) => {
    await fulfillJson(route, benchmarkTelemetry);
  });

  await page.route(`${BENCHMARK_PREFIX}/presentation/compare`, async (route) => {
    await fulfillJson(route, benchmarkPresentation);
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
      await fulfillJson(route, { runId: benchmarkRun.runId, exitCode: 0 });
    },
  );
}

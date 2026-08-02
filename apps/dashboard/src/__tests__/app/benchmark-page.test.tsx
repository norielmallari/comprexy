import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import type { MockedFunction } from 'vitest';

import BenchmarkPage from '@/app/benchmark/page';
import {
  getRunPresentation,
  useBenchmarkRun,
  useBenchmarkScenarios,
  useCancelBenchmarkRun,
  useComparisonPresentation,
  useReportBenchmarkRun,
  useStartBenchmarkRun,
  useTelemetryPresentation,
} from '@/lib/api/benchmarks';
import { useConversations } from '@/lib/queries/use-conversations';
import { useTurnMetrics } from '@/lib/queries/use-turns';
import type {
  BenchmarkComparisonPresentationResponse,
  BenchmarkScenarioDto,
  ConversationMetricsListItemDto,
  ConversationTurnMetricDto,
} from '@/types/api';

vi.mock('@/components/layout', () => ({
  DashboardShell: ({ children }: { children: React.ReactNode }) => (
    <main>{children}</main>
  ),
  DashboardSkeleton: () => <div>Loading skeleton</div>,
}));

vi.mock('@/hooks/use-theme', () => ({
  useTheme: () => ({ theme: 'light', toggleTheme: vi.fn() }),
}));

vi.mock('@/components/charts', () => ({
  BarChart: ({ testId }: { testId?: string }) => (
    <div data-testid={testId ?? 'token-counts-by-turn-chart'} role="img" />
  ),
}));

vi.mock('@/lib/queries/use-conversations', () => ({
  useConversations: vi.fn(),
}));

vi.mock('@/lib/queries/use-turns', () => ({
  useTurnMetrics: vi.fn(),
}));

vi.mock('@/lib/api/benchmarks', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/lib/api/benchmarks')>();
  return {
    ...actual,
    useBenchmarkScenarios: vi.fn(),
    useStartBenchmarkRun: vi.fn(),
    useCancelBenchmarkRun: vi.fn(),
    useReportBenchmarkRun: vi.fn(),
    useBenchmarkRun: vi.fn(),
    useTelemetryPresentation: vi.fn(),
    useComparisonPresentation: vi.fn(),
    getRunPresentation: vi.fn(),
  };
});

const mockUseBenchmarkScenarios = useBenchmarkScenarios as MockedFunction<
  typeof useBenchmarkScenarios
>;
const mockUseStartBenchmarkRun = useStartBenchmarkRun as MockedFunction<
  typeof useStartBenchmarkRun
>;
const mockUseCancelBenchmarkRun = useCancelBenchmarkRun as MockedFunction<
  typeof useCancelBenchmarkRun
>;
const mockUseReportBenchmarkRun = useReportBenchmarkRun as MockedFunction<
  typeof useReportBenchmarkRun
>;
const mockUseBenchmarkRun = useBenchmarkRun as MockedFunction<typeof useBenchmarkRun>;
const mockUseTelemetryPresentation = useTelemetryPresentation as MockedFunction<
  typeof useTelemetryPresentation
>;
const mockUseComparisonPresentation = useComparisonPresentation as MockedFunction<
  typeof useComparisonPresentation
>;
const mockUseConversations = useConversations as MockedFunction<
  typeof useConversations
>;
const mockUseTurnMetrics = useTurnMetrics as MockedFunction<typeof useTurnMetrics>;
const mockGetRunPresentation = getRunPresentation as MockedFunction<
  typeof getRunPresentation
>;

const BASELINE_ID = '00000000-0000-4000-8000-000000000001';
const COMPARE_ID = '00000000-0000-4000-8000-000000000002';

const scenarios: BenchmarkScenarioDto[] = [
  { name: 'fixture-scenario-a', promptCount: 5 },
];

const conversations: ConversationMetricsListItemDto[] = [
  {
    conversationId: BASELINE_ID,
    totalTurns: 3,
    totalRawInputTokensEstimated: 12_000,
    totalActualTokensEstimated: 8_000,
    totalNetTokensSaved: 4_000,
    averageTokenSavingsRatio: 0.33,
    totalCompressionOverheadTokens: 200,
    updatedAt: '2026-01-15T12:00:00.000Z',
  },
  {
    conversationId: COMPARE_ID,
    totalTurns: 4,
    totalRawInputTokensEstimated: 11_000,
    totalActualTokensEstimated: 7_500,
    totalNetTokensSaved: 3_500,
    averageTokenSavingsRatio: 0.3,
    totalCompressionOverheadTokens: 180,
    updatedAt: '2026-01-15T13:00:00.000Z',
  },
];

const turnFixture: ConversationTurnMetricDto = {
  id: '00000000-0000-4000-8000-00000000a001',
  turnIndex: 1,
  requestStartedAt: '2026-01-15T11:00:00.000Z',
  model: 'test-model',
  rawInputTokensEstimated: 3000,
  compressedInputTokensEstimated: 2500,
  systemPromptTokensEstimated: 300,
  workingMemoryTokensEstimated: 0,
  historyAndToolsTokensEstimated: 2200,
  actualPromptTokens: 2480,
  actualCompletionTokens: 200,
  baselineTotalTokensEstimated: 3200,
  compressedTotalTokensEstimated: 2700,
  netTokensSaved: 500,
  netTokenSavingsRatio: 0.15625,
  softBudgetExceeded: false,
  hardBudgetExceeded: false,
  trimTriggered: false,
  workingMemoryVersionUsed: null,
  rawMessageCount: 3,
  sentMessageCount: 3,
  durationMs: 1200,
  upstreamDurationMs: 900,
  prepareDurationMs: 300,
  createdAt: '2026-01-15T11:00:10.000Z',
};

const presentationFixture: BenchmarkComparisonPresentationResponse = {
  totals: {
    baseline: {
      conversationId: BASELINE_ID,
      turnCount: 3,
      inputTokens: 12_000,
      outputTokens: 3_000,
      overheadTokens: 400,
      totalSentTokens: 15_400,
      wallClockMs: 120_000,
      totalProxyDurationMs: 95_000,
      totalUpstreamDurationMs: 80_000,
      totalPrepareDurationMs: 15_000,
    },
    compare: {
      conversationId: COMPARE_ID,
      turnCount: 4,
      inputTokens: 11_000,
      outputTokens: 3_200,
      overheadTokens: 350,
      totalSentTokens: 14_550,
      wallClockMs: 135_000,
      totalProxyDurationMs: 100_000,
      totalUpstreamDurationMs: 85_000,
      totalPrepareDurationMs: 15_000,
    },
    input: { baseline: 12_000, compare: 11_000, delta: -1000, deltaPercent: -8.33 },
    output: { baseline: 3000, compare: 3200, delta: 200, deltaPercent: 6.67 },
    overhead: { baseline: 400, compare: 350, delta: -50, deltaPercent: -12.5 },
    turnCount: { baseline: 3, compare: 4, delta: 1, deltaPercent: 33.33 },
    wallClockMs: { baseline: 120_000, compare: 135_000, delta: 15_000, deltaPercent: 12.5 },
    proxyDurationMs: { baseline: 95_000, compare: 100_000, delta: 5000, deltaPercent: 5.26 },
    caveats: [],
  },
  cost: null,
  baselineConversationId: BASELINE_ID,
  compareConversationId: COMPARE_ID,
  runId: 'fixture-run-001',
  turnSeriesPaths: ['/tmp/fixture-bench/fixture-run-001/turns-baseline.json'],
};

function queryIdle() {
  return {
    data: undefined,
    isLoading: false,
    isSuccess: false,
    isPending: true,
  };
}

function querySuccess<T>(data: T) {
  return {
    data,
    isLoading: false,
    isSuccess: true,
    isPending: false,
  };
}

beforeEach(() => {
  vi.clearAllMocks();

  mockUseBenchmarkScenarios.mockReturnValue({
    data: scenarios,
    isLoading: false,
  } as unknown as ReturnType<typeof useBenchmarkScenarios>);

  mockUseStartBenchmarkRun.mockReturnValue({
    mutateAsync: vi.fn().mockResolvedValue({ runId: 'fixture-run-001' }),
    isPending: false,
  } as unknown as ReturnType<typeof useStartBenchmarkRun>);

  mockUseCancelBenchmarkRun.mockReturnValue({
    mutateAsync: vi.fn(),
    isPending: false,
  } as unknown as ReturnType<typeof useCancelBenchmarkRun>);

  mockUseReportBenchmarkRun.mockReturnValue({
    mutateAsync: vi.fn(),
    isPending: false,
  } as unknown as ReturnType<typeof useReportBenchmarkRun>);

  mockUseTelemetryPresentation.mockReturnValue(
    queryIdle() as unknown as ReturnType<typeof useTelemetryPresentation>,
  );
  mockUseComparisonPresentation.mockReturnValue(
    querySuccess(presentationFixture) as unknown as ReturnType<
      typeof useComparisonPresentation
    >,
  );

  mockUseConversations.mockReturnValue({
    data: conversations,
    isLoading: false,
    isSuccess: true,
  } as unknown as ReturnType<typeof useConversations>);

  mockUseTurnMetrics.mockImplementation((conversationId: string | null) => {
    if (!conversationId) {
      return querySuccess(null) as unknown as ReturnType<typeof useTurnMetrics>;
    }
    return querySuccess([turnFixture]) as unknown as ReturnType<typeof useTurnMetrics>;
  });

  mockGetRunPresentation.mockResolvedValue(presentationFixture);
  mockUseBenchmarkRun.mockImplementation((runId: string | null) => {
    if (runId === 'fixture-run-001') {
      return {
        data: {
          runId: 'fixture-run-001',
          phase: 'completed',
          runPhase: 'run_finished',
          startedAt: null,
          updatedAt: null,
          lastError: null,
          arm: null,
          conversationName: null,
          promptsCompleted: null,
          promptCount: null,
          conversationNames: [],
          costRates: null,
        },
        isLoading: false,
      } as unknown as ReturnType<typeof useBenchmarkRun>;
    }
    return queryIdle() as unknown as ReturnType<typeof useBenchmarkRun>;
  });
});

describe('BenchmarkPage auto-fill integration', () => {
  it('switches to comparison mode when terminal run auto-fills IDs', async () => {
    render(<BenchmarkPage />);

    expect(screen.getByTestId('telemetry-panel')).toBeInTheDocument();

    fireEvent.click(screen.getByTestId('benchmark-ack-checkbox'));
    fireEvent.click(screen.getByLabelText(/fixture-scenario-a/));
    fireEvent.click(screen.getByTestId('start-benchmark-button'));

    await waitFor(() => {
      expect(mockGetRunPresentation).toHaveBeenCalledWith('fixture-run-001');
      expect(screen.getByTestId('comparison-panel')).toBeInTheDocument();
      expect(screen.queryByTestId('telemetry-panel')).not.toBeInTheDocument();
      expect(screen.getByTestId('baseline-token-chart')).toBeInTheDocument();
      expect(screen.getByTestId('compare-token-chart')).toBeInTheDocument();
    });
  });
});

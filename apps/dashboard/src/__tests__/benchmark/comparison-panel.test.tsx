import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import type { MockedFunction } from 'vitest';

import { ComparisonPanel } from '@/components/benchmark/comparison-panel';
import { useComparisonPresentation } from '@/lib/api/benchmarks';
import { DEFAULT_COST_RATES } from '@/lib/benchmark-cost';
import { useConversations } from '@/lib/queries/use-conversations';
import { useTurnMetrics } from '@/lib/queries/use-turns';
import type {
  BenchmarkComparisonPresentationResponse,
  ConversationMetricsListItemDto,
  ConversationTurnMetricDto,
} from '@/types/api';

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
    useComparisonPresentation: vi.fn(),
  };
});

vi.mock('@/components/charts', () => ({
  BarChart: ({ testId, title }: { testId?: string; title?: string }) => (
    <div data-testid={testId} role="img" aria-label={title} />
  ),
}));

const mockUseConversations = useConversations as MockedFunction<
  typeof useConversations
>;
const mockUseTurnMetrics = useTurnMetrics as MockedFunction<typeof useTurnMetrics>;
const mockUseComparisonPresentation = useComparisonPresentation as MockedFunction<
  typeof useComparisonPresentation
>;

const BASELINE_ID = '00000000-0000-4000-8000-000000000001';
const COMPARE_ID = '00000000-0000-4000-8000-000000000002';

const conversations: ConversationMetricsListItemDto[] = [
  {
    conversationId: BASELINE_ID,
    totalTurns: 3,
    totalRawInputTokensEstimated: 12_000,
    totalActualTokensEstimated: 8_000,
    totalNetTokensSaved: 4_000,
    totalVirtualToolsTokensSaved: 1_000,
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
    totalVirtualToolsTokensSaved: 800,
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
  irFullInputTokensEstimated: 2500,
  compressedInputTokensEstimated: 2500,
  systemPromptTokensEstimated: 300,
  workingMemoryTokensEstimated: 0,
  historyAndToolsTokensEstimated: 2200,
  actualPromptTokens: 2480,
  actualCompletionTokens: 200,
  baselineTotalTokensEstimated: 2700,
  compressedTotalTokensEstimated: 2700,
  netTokensSaved: 0,
  netTokenSavingsRatio: 0.15625,
  virtualToolsTokensSaved: 500,
  isLegacyMixedAxis: false,
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
  mockUseComparisonPresentation.mockReturnValue(
    querySuccess(presentationFixture) as unknown as ReturnType<
      typeof useComparisonPresentation
    >,
  );
});

describe('ComparisonPanel', () => {
  it('renders baseline and compare pickers with label text', () => {
    render(
      <ComparisonPanel
        baselineId={null}
        compareId={null}
        onBaselineChange={vi.fn()}
        onCompareChange={vi.fn()}
        rates={DEFAULT_COST_RATES}
        modelKind="local"
      />,
    );

    expect(screen.getByText('Baseline (top chart)')).toBeInTheDocument();
    expect(screen.getByText('Compare (bottom chart)')).toBeInTheDocument();
    expect(screen.getAllByRole('combobox')).toHaveLength(2);
  });

  it('renders distinct chart testids when both conversations are selected', () => {
    render(
      <ComparisonPanel
        baselineId={BASELINE_ID}
        compareId={COMPARE_ID}
        onBaselineChange={vi.fn()}
        onCompareChange={vi.fn()}
        rates={DEFAULT_COST_RATES}
        modelKind="local"
      />,
    );

    expect(screen.getByTestId('baseline-token-chart')).toBeInTheDocument();
    expect(screen.getByTestId('compare-token-chart')).toBeInTheDocument();
    expect(screen.getByTestId('baseline-token-chart')).not.toBe(
      screen.getByTestId('compare-token-chart'),
    );
  });
});

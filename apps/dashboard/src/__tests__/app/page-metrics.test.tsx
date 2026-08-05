import { render, screen } from '@testing-library/react';
import type { QueryObserverSuccessResult } from '@tanstack/react-query';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import type { MockedFunction } from 'vitest';

import Home from '@/app/page';
import { useConversationUrl } from '@/hooks/use-conversation-url';
import { useConversations } from '@/lib/queries/use-conversations';
import { useMetricsSummary } from '@/lib/queries/use-metrics';
import { useTurnMetrics } from '@/lib/queries/use-turns';
import type {
  ConversationMetricsListItemDto,
  ConversationMetricsSummaryDto,
  ConversationTurnMetricDto,
} from '@/types/api';

function querySuccess<T>(
  data: T,
): QueryObserverSuccessResult<T, Error> {
  return {
    data,
    dataUpdatedAt: Date.now(),
    error: null,
    errorUpdatedAt: 0,
    failureCount: 0,
    failureReason: null,
    errorUpdateCount: 0,
    isError: false,
    isFetched: true,
    isFetchedAfterMount: true,
    isFetching: false,
    isLoading: false,
    isPending: false,
    isLoadingError: false,
    isInitialLoading: false,
    isPaused: false,
    isPlaceholderData: false,
    isRefetchError: false,
    isRefetching: false,
    isStale: false,
    isSuccess: true,
    isEnabled: true,
    status: 'success',
    fetchStatus: 'idle',
    refetch: vi.fn(),
    promise: Promise.resolve(data),
  };
}

vi.mock('@/hooks/use-conversation-url', () => ({
  useConversationUrl: vi.fn(),
}));

vi.mock('@/lib/queries/use-conversations', () => ({
  useConversations: vi.fn(),
}));

vi.mock('@/lib/queries/use-metrics', () => ({
  useMetricsSummary: vi.fn(),
}));

vi.mock('@/lib/queries/use-turns', () => ({
  useTurnMetrics: vi.fn(),
}));

vi.mock('@/components/layout', () => ({
  DashboardShell: ({ children }: { children: React.ReactNode }) => (
    <main>{children}</main>
  ),
  DashboardSkeleton: () => <div>Loading skeleton</div>,
}));

vi.mock('@/components/charts', () => ({
  BarChart: () => (
    <div
      role="img"
      aria-label="Prepared prompt tokens chart"
      data-testid="token-counts-by-turn-chart"
    />
  ),
}));

vi.mock('@/hooks/use-theme', () => ({
  useTheme: () => ({ theme: 'light', toggleTheme: vi.fn() }),
}));

const mockUseConversationUrl = useConversationUrl as MockedFunction<
  typeof useConversationUrl
>;
const mockUseConversations = useConversations as MockedFunction<
  typeof useConversations
>;
const mockUseMetricsSummary = useMetricsSummary as MockedFunction<
  typeof useMetricsSummary
>;
const mockUseTurnMetrics = useTurnMetrics as MockedFunction<typeof useTurnMetrics>;

const CONVERSATION_ID = '00000000-0000-4000-8000-000000000001';

const metricsSummary: ConversationMetricsSummaryDto = {
  conversationId: CONVERSATION_ID,
  totalTurns: 3,
  totalRawInputTokensEstimated: 12000,
  totalCompressedPromptTokens: 8000,
  totalCompletionTokens: 600,
  totalCompressionOverheadTokens: 200,
  totalBaselineTokensEstimated: 12600,
  totalActualTokensEstimated: 8600,
  totalNetTokensSaved: 4000,
  totalVirtualToolsTokensSaved: 1000,
  averageTokenSavingsRatio: 0.33,
  compressionEventCount: 1,
  createdAt: '2026-01-15T11:00:00.000Z',
  updatedAt: '2026-01-15T12:00:00.000Z',
};

const turns: ConversationTurnMetricDto[] = [
  {
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
  },
  {
    id: '00000000-0000-4000-8000-00000000a003',
    turnIndex: 3,
    requestStartedAt: '2026-01-15T12:00:00.000Z',
    model: 'test-model',
    rawInputTokensEstimated: 4500,
    irFullInputTokensEstimated: 4500,
    compressedInputTokensEstimated: 1500,
    systemPromptTokensEstimated: 300,
    workingMemoryTokensEstimated: 800,
    historyAndToolsTokensEstimated: 400,
    actualPromptTokens: 1520,
    actualCompletionTokens: 200,
    baselineTotalTokensEstimated: 4700,
    compressedTotalTokensEstimated: 1700,
    netTokensSaved: 3000,
    netTokenSavingsRatio: 0.638298,
    virtualToolsTokensSaved: 0,
    isLegacyMixedAxis: false,
    softBudgetExceeded: false,
    hardBudgetExceeded: false,
    trimTriggered: false,
    workingMemoryVersionUsed: 1,
    rawMessageCount: 7,
    sentMessageCount: 3,
    durationMs: 1500,
    upstreamDurationMs: 1100,
    prepareDurationMs: 400,
    createdAt: '2026-01-15T12:00:10.000Z',
  },
];

beforeEach(() => {
  vi.clearAllMocks();
  mockUseConversationUrl.mockReturnValue({
    conversationId: CONVERSATION_ID,
    effectiveConversationId: CONVERSATION_ID,
    isRestoringConversation: false,
    navigateToConversation: vi.fn(),
  });
  mockUseConversations.mockReturnValue(
    querySuccess<ConversationMetricsListItemDto[]>([]),
  );
  mockUseMetricsSummary.mockReturnValue(querySuccess(metricsSummary));
  mockUseTurnMetrics.mockReturnValue(querySuccess(turns));
});

describe('Dashboard page metric composition', () => {
  it('shows empty state when no conversation is selected', () => {
    mockUseConversationUrl.mockReturnValue({
      conversationId: null,
      effectiveConversationId: null,
      isRestoringConversation: false,
      navigateToConversation: vi.fn(),
    });

    render(<Home />);

    expect(screen.getByTestId('metrics-empty-state')).toBeInTheDocument();
    expect(screen.queryByTestId('metrics-grid')).not.toBeInTheDocument();
  });

  it('uses a full-width 2x2 metric grid with the requested grouping', () => {
    render(<Home />);

    const grid = screen.getByTestId('metrics-grid');
    const topLeft = screen.getByTestId('metrics-top-left');
    const topRight = screen.getByTestId('metrics-top-right');
    const bottomLeft = screen.getByTestId('metrics-bottom-left');
    const bottomRight = screen.getByTestId('metrics-bottom-right');

    expect(grid).toHaveClass('w-full', 'lg:grid-cols-2');
    expect(Array.from(grid.children)).toEqual([
      topLeft,
      topRight,
      bottomLeft,
      bottomRight,
    ]);
    expect(topLeft).toContainElement(
      screen.getByRole('region', { name: 'Tokens Saved' }),
    );
    expect(topRight).toContainElement(
      screen.getByRole('region', { name: 'Weighted Compression' }),
    );
    expect(topRight).toContainElement(
      screen.getByRole('region', { name: 'Average Compression' }),
    );
    expect(bottomLeft).toContainElement(
      screen.getByRole('region', { name: 'Baseline (combined)' }),
    );
    expect(bottomLeft).toContainElement(
      screen.getByRole('region', { name: 'Actual (combined)' }),
    );
    expect(bottomRight).toContainElement(
      screen.getByRole('region', { name: 'Best Compression' }),
    );
    expect(bottomRight).toContainElement(
      screen.getByRole('region', { name: 'Overhead' }),
    );
    expect(bottomRight).toContainElement(
      screen.getByRole('region', { name: 'Working Memory' }),
    );
  });

  it('places the I/O strip as a sibling after the metrics grid', () => {
    render(<Home />);

    const grid = screen.getByTestId('metrics-grid');
    const ioStrip = screen.getByTestId('conversation-io-cards');
    const bottomLeft = screen.getByTestId('metrics-bottom-left');

    expect(ioStrip).toBeInTheDocument();
    expect(bottomLeft).not.toContainElement(ioStrip);
    expect(grid).not.toContainElement(ioStrip);
    expect(grid.nextElementSibling).toBe(ioStrip);
  });

  it('renders exactly one region for each honest metric', () => {
    render(<Home />);

    const expectedRegions = [
      'Tokens Saved',
      'Weighted Compression',
      'Average Compression',
      'Baseline (combined)',
      'Actual (combined)',
      'Best Compression',
      'Overhead',
      'Working Memory',
      'Raw input tokens',
      'Input tokens',
      'Output tokens',
      'Virtual Tools channel',
    ];

    for (const name of expectedRegions) {
      expect(screen.getAllByRole('region', { name })).toHaveLength(1);
    }

    expect(
      screen.queryByRole('region', { name: 'Baseline Tokens' }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('region', { name: 'Actual Tokens' }),
    ).not.toBeInTheDocument();
  });

  it('uses the Best Compression value size for every non-hero metric', () => {
    render(<Home />);

    const standardMetrics = [
      'Weighted Compression',
      'Average Compression',
      'Baseline (combined)',
      'Actual (combined)',
      'Best Compression',
      'Overhead',
      'Raw input tokens',
      'Input tokens',
      'Output tokens',
      'Virtual Tools channel',
    ];

    for (const name of standardMetrics) {
      expect(
        screen.getByRole('region', { name }).querySelector('.text-3xl'),
      ).toBeInTheDocument();
    }

    expect(
      screen
        .getByRole('region', { name: 'Working Memory' })
        .querySelector('.text-2xl'),
    ).toBeInTheDocument();
  });

  it('does not render legacy grouped metric regions', () => {
    render(<Home />);

    const forbidden = [
      'Compression Ratios',
      'Baseline vs Actual Tokens',
      'Compression Health',
      'Budget Triggers',
    ];

    for (const name of forbidden) {
      expect(screen.queryByRole('region', { name })).not.toBeInTheDocument();
      expect(screen.queryByText(name)).not.toBeInTheDocument();
    }
  });

  it('binds distinct summary and turn-derived values to the metric regions', () => {
    render(<Home />);

    expect(
      screen.getByRole('region', { name: 'Tokens Saved' }),
    ).toHaveTextContent('4,000');
    expect(
      screen.getByRole('region', { name: 'Weighted Compression' }),
    ).toHaveTextContent('33.0');
    expect(
      screen.getByRole('region', { name: 'Average Compression' }),
    ).toHaveTextContent('39.7');
    expect(
      screen.getByRole('region', { name: 'Baseline (combined)' }),
    ).toHaveTextContent('12,600');
    expect(
      screen.getByRole('region', { name: 'Actual (combined)' }),
    ).toHaveTextContent('8,600');
    expect(
      screen.getByRole('region', { name: 'Best Compression' }),
    ).toHaveTextContent('63.8');
    expect(screen.getByRole('region', { name: 'Overhead' })).toHaveTextContent(
      '1.6',
    );
    expect(
      screen.getByRole('region', { name: 'Working Memory' }),
    ).toHaveTextContent('v1');
    expect(
      screen.getByRole('region', { name: 'Raw input tokens' }),
    ).toHaveTextContent('12,000');
    expect(
      screen.getByRole('region', { name: 'Input tokens' }),
    ).toHaveTextContent('8,000');
    expect(
      screen.getByRole('region', { name: 'Output tokens' }),
    ).toHaveTextContent('600');
    expect(
      screen.getByRole('region', { name: 'Virtual Tools channel' }),
    ).toHaveTextContent('1,000');
  });

  it('keeps Tokens Saved region name with SoftBudget hero subtitle', () => {
    render(<Home />);

    const hero = screen.getByRole('region', { name: 'Tokens Saved' });
    expect(hero).toHaveTextContent('SoftBudget net (IR full − prepared)');
    expect(
      screen.queryByRole('region', { name: 'SoftBudget tokens saved' }),
    ).not.toBeInTheDocument();
  });
});

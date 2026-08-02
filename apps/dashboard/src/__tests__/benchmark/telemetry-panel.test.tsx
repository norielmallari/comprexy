import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import type { MockedFunction } from 'vitest';

import { TelemetryPanel } from '@/components/benchmark/telemetry-panel';
import { useTelemetryPresentation } from '@/lib/api/benchmarks';
import { DEFAULT_COST_RATES } from '@/lib/benchmark-cost';
import { useConversations } from '@/lib/queries/use-conversations';
import { useTurnMetrics } from '@/lib/queries/use-turns';
import type { ConversationMetricsListItemDto } from '@/types/api';

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
    useTelemetryPresentation: vi.fn(),
  };
});

vi.mock('@/components/charts', () => ({
  BarChart: () => <div data-testid="token-counts-by-turn-chart" />,
}));

const mockUseConversations = useConversations as MockedFunction<
  typeof useConversations
>;
const mockUseTurnMetrics = useTurnMetrics as MockedFunction<typeof useTurnMetrics>;
const mockUseTelemetryPresentation = useTelemetryPresentation as MockedFunction<
  typeof useTelemetryPresentation
>;

const CONVERSATION_ID = '00000000-0000-4000-8000-000000000001';

const conversations: ConversationMetricsListItemDto[] = [
  {
    conversationId: CONVERSATION_ID,
    totalTurns: 3,
    totalRawInputTokensEstimated: 12_000,
    totalActualTokensEstimated: 8_000,
    totalNetTokensSaved: 4_000,
    averageTokenSavingsRatio: 0.33,
    totalCompressionOverheadTokens: 200,
    updatedAt: '2026-01-15T12:00:00.000Z',
  },
];

function queryIdle() {
  return {
    data: undefined,
    isLoading: false,
    isSuccess: false,
    isPending: true,
  };
}

beforeEach(() => {
  vi.clearAllMocks();
  mockUseConversations.mockReturnValue({
    data: conversations,
    isLoading: false,
    isSuccess: true,
  } as unknown as ReturnType<typeof useConversations>);
  mockUseTurnMetrics.mockReturnValue(
    queryIdle() as unknown as ReturnType<typeof useTurnMetrics>,
  );
  mockUseTelemetryPresentation.mockReturnValue(
    queryIdle() as unknown as ReturnType<typeof useTelemetryPresentation>,
  );
});

describe('TelemetryPanel', () => {
  it('renders conversation picker with label prop text', () => {
    render(
      <TelemetryPanel
        conversationId={null}
        onConversationChange={vi.fn()}
        rates={DEFAULT_COST_RATES}
        modelKind="local"
      />,
    );

    expect(screen.getByText('Operator DB conversation')).toBeInTheDocument();
    expect(screen.getByRole('combobox')).toBeInTheDocument();
  });
});

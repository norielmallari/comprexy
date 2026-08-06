import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { IoTotalsCards } from '@/components/benchmark/io-totals-cards';
import {
  CATALOG_MODELS,
  storeSelectorStub,
} from '@/__tests__/helpers/cost-model-mocks';
import type { BenchmarkChannelDelta, ConversationTokenTotals } from '@/types/api';

vi.mock('@/lib/queries/use-cost-models', () => ({
  useCostModels: vi.fn(),
}));

vi.mock('@/lib/store/dashboard-store', () => ({
  useDashboardStore: vi.fn(),
}));

import { useCostModels } from '@/lib/queries/use-cost-models';
import { useDashboardStore } from '@/lib/store/dashboard-store';

const mockUseCostModels = useCostModels as unknown as ReturnType<typeof vi.fn>;
const mockUseDashboardStore = useDashboardStore as unknown as ReturnType<typeof vi.fn>;

const telemetryTotals: ConversationTokenTotals = {
  conversationId: '00000000-0000-4000-8000-000000000001',
  turnCount: 4,
  inputTokens: 12_000,
  outputTokens: 3_500,
  overheadTokens: 800,
  totalSentTokens: 16_300,
  wallClockMs: 45_000,
  totalProxyDurationMs: 42_000,
  totalUpstreamDurationMs: 40_000,
  totalPrepareDurationMs: 2_000,
};

function makeDelta(
  baseline: number,
  compare: number,
): BenchmarkChannelDelta {
  const delta = compare - baseline;
  const deltaPercent = baseline === 0 ? null : (delta / baseline) * 100;
  return { baseline, compare, delta, deltaPercent };
}

describe('IoTotalsCards', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseCostModels.mockReturnValue({
      data: CATALOG_MODELS,
      isLoading: false,
      isError: false,
    });
    mockUseDashboardStore.mockImplementation(storeSelectorStub('local'));
  });

  it('renders separated input, output, overhead, and turn count cards', () => {
    render(<IoTotalsCards totals={telemetryTotals} />);

    expect(screen.getByTestId('benchmark-io-cards')).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Input tokens' })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Output tokens' })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Overhead tokens' })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Turn count' })).toBeInTheDocument();
    expect(screen.getByText('12K')).toBeInTheDocument();
    expect(screen.getByText('4K')).toBeInTheDocument();
    expect(screen.getByText('800')).toBeInTheDocument();
  });

  it('does not show a blended savings headline', () => {
    render(<IoTotalsCards totals={telemetryTotals} />);

    expect(screen.queryByText(/savings/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/blended/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/total tokens saved/i)).not.toBeInTheDocument();
  });

  it('renders comparison deltas with negative values styled in red', () => {
    const deltas = {
      input: makeDelta(10_000, 8_000),
      output: makeDelta(5_000, 6_000),
      overhead: makeDelta(500, 300),
      turnCount: makeDelta(10, 12),
    };

    render(<IoTotalsCards showComparison deltas={deltas} />);

    const inputDelta = screen.getByRole('region', { name: 'Input tokens comparison' });
    const negativeValue = inputDelta.querySelector('.text-red-700');
    expect(negativeValue).not.toBeNull();
    expect(negativeValue?.textContent).toMatch(/-2,000/);

    const outputDelta = screen.getByRole('region', { name: 'Output tokens comparison' });
    const positiveValue = outputDelta.querySelector('.text-green-700');
    expect(positiveValue).not.toBeNull();
    expect(positiveValue?.textContent).toMatch(/\+1,000/);
  });

  it('returns null when no totals and not in comparison mode', () => {
    const { container } = render(<IoTotalsCards />);
    expect(container).toBeEmptyDOMElement();
  });

  it('hides `$` when Local is selected', () => {
    render(<IoTotalsCards totals={telemetryTotals} />);

    expect(screen.getByTestId('benchmark-io-cards').textContent).not.toMatch(/\$/);
  });

  it('shows `$` when Sonnet is selected', () => {
    mockUseDashboardStore.mockImplementation(storeSelectorStub('claude-sonnet-5'));

    render(<IoTotalsCards totals={telemetryTotals} />);

    // 12000 * $3/1M = $0.0360; 3500 * $15/1M = $0.0525
    expect(screen.getByRole('region', { name: 'Input tokens' })).toHaveTextContent(
      '$0.0360',
    );
    expect(screen.getByRole('region', { name: 'Output tokens' })).toHaveTextContent(
      '$0.0525',
    );
  });

  it('exposes Estimated cost aria-labels on `$` overlays when a priced model is selected', () => {
    mockUseDashboardStore.mockImplementation(storeSelectorStub('claude-sonnet-5'));

    render(<IoTotalsCards totals={telemetryTotals} />);

    expect(screen.getByLabelText('Estimated cost $0.0360')).toBeInTheDocument();
    expect(screen.getByLabelText('Estimated cost $0.0525')).toBeInTheDocument();
    expect(screen.getByLabelText('Estimated cost $0.0024')).toBeInTheDocument();
    expect(screen.getAllByLabelText(/Estimated cost/).length).toBe(3);
  });

  it('exposes Estimated cost aria-labels on comparison `$` overlays', () => {
    mockUseDashboardStore.mockImplementation(storeSelectorStub('claude-sonnet-5'));
    const deltas = {
      input: makeDelta(10_000, 8_000),
      output: makeDelta(5_000, 6_000),
      overhead: makeDelta(500, 300),
      turnCount: makeDelta(10, 12),
    };

    render(<IoTotalsCards showComparison deltas={deltas} />);

    // |Δ input| 2000 * $3/1M = $0.0060; |Δ output| 1000 * $15/1M = $0.0150
    expect(screen.getByLabelText('Estimated cost $0.0060')).toBeInTheDocument();
    expect(screen.getByLabelText('Estimated cost $0.0150')).toBeInTheDocument();
  });
});

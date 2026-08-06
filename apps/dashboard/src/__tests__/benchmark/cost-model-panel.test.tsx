import { render, screen, fireEvent } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactElement } from 'react';
import { describe, expect, it, vi, beforeEach } from 'vitest';

import { CostModelPanel } from '@/components/benchmark/cost-model-panel';
import {
  COST_DISCLAIMER,
  DEFAULT_COST_RATES,
  LOCAL_COST_DISCLAIMER,
} from '@/lib/benchmark-cost';
import type { BenchmarkCostRates, CostModelDto } from '@/types/api';

const baseRates: BenchmarkCostRates = { ...DEFAULT_COST_RATES };

const localModel: CostModelDto = {
  modelKey: 'local',
  displayLabel: 'Local',
  currencyCode: 'USD',
  inputUsdPer1M: 0,
  outputUsdPer1M: 0,
  sortOrder: 0,
};

const sonnetModel: CostModelDto = {
  modelKey: 'claude-sonnet-5',
  displayLabel: 'Claude Sonnet 5',
  currencyCode: 'USD',
  inputUsdPer1M: 3,
  outputUsdPer1M: 15,
  sortOrder: 2,
};

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

function wrap(ui: ReactElement) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

describe('CostModelPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseDashboardStore.mockImplementation(
      (selector: (s: { selectedCostModelKey: string }) => unknown) =>
        selector({ selectedCostModelKey: 'local' }),
    );
    mockUseCostModels.mockReturnValue({
      data: [localModel, sonnetModel],
      isLoading: false,
      isError: false,
    });
  });

  it('shows local disclaimer when rates are local', () => {
    wrap(
      <CostModelPanel rates={baseRates} onRatesChange={vi.fn()} />,
    );

    const disclaimer = screen.getByTestId('cost-model-disclaimer');
    expect(disclaimer).toHaveTextContent(LOCAL_COST_DISCLAIMER);
    expect(disclaimer).not.toHaveTextContent(COST_DISCLAIMER);
  });

  it('shows USD disclaimer and catalog rates when model is priced', () => {
    mockUseDashboardStore.mockImplementation(
      (selector: (s: { selectedCostModelKey: string }) => unknown) =>
        selector({ selectedCostModelKey: 'claude-sonnet-5' }),
    );

    wrap(
      <CostModelPanel
        rates={{ ...baseRates, modelKind: 'usd', inputUsdPer1M: 3, outputUsdPer1M: 15 }}
        onRatesChange={vi.fn()}
      />,
    );

    expect(screen.getByTestId('cost-model-disclaimer')).toHaveTextContent(COST_DISCLAIMER);
    expect(screen.getByText('3')).toBeInTheDocument();
    expect(screen.getByText('15')).toBeInTheDocument();
  });

  it('resets time-value defaults when reset is clicked', () => {
    const onRatesChange = vi.fn();

    wrap(
      <CostModelPanel
        rates={{ ...baseRates, developerUsdPerHour: 10, machineUsdPerHour: 1 }}
        onRatesChange={onRatesChange}
      />,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Reset time-value defaults' }));
    expect(onRatesChange).toHaveBeenCalled();
  });
});

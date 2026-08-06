import { render, screen, fireEvent } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { CostModelPicker } from '@/components/cost/cost-model-picker';
import { COST_DISCLAIMER } from '@/components/cost/format-token-cost';
import { CATALOG_MODELS, storeSelectorStub } from '@/__tests__/helpers/cost-model-mocks';

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

describe('CostModelPicker', () => {
  const setSelectedCostModelKey = vi.fn();

  beforeEach(() => {
    vi.clearAllMocks();
    mockUseCostModels.mockReturnValue({
      data: CATALOG_MODELS,
      isLoading: false,
      isError: false,
    });
    mockUseDashboardStore.mockImplementation(
      (selector: (s: {
        selectedCostModelKey: string;
        setSelectedCostModelKey: (k: string) => void;
      }) => unknown) =>
        selector({
          selectedCostModelKey: 'local',
          setSelectedCostModelKey,
        }),
    );
  });

  it('renders cost model combobox with catalog options', () => {
    render(<CostModelPicker />);

    const select = screen.getByRole('combobox', { name: 'Cost model' });
    expect(select).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Local' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Claude Sonnet 5' })).toBeInTheDocument();
  });

  it('shows USD disclaimer when a priced model is selected', () => {
    mockUseDashboardStore.mockImplementation(
      (selector: (s: {
        selectedCostModelKey: string;
        setSelectedCostModelKey: (k: string) => void;
      }) => unknown) =>
        selector({
          selectedCostModelKey: 'claude-sonnet-5',
          setSelectedCostModelKey,
        }),
    );

    render(<CostModelPicker />);

    expect(screen.getByText(COST_DISCLAIMER)).toBeInTheDocument();
  });

  it('hides USD disclaimer for Local', () => {
    render(<CostModelPicker />);
    expect(screen.queryByText(COST_DISCLAIMER)).not.toBeInTheDocument();
  });

  it('updates store when selection changes', () => {
    render(<CostModelPicker />);

    fireEvent.change(screen.getByRole('combobox', { name: 'Cost model' }), {
      target: { value: 'claude-sonnet-5' },
    });

    expect(setSelectedCostModelKey).toHaveBeenCalledWith('claude-sonnet-5');
  });
});

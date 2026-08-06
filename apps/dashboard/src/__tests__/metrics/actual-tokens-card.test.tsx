import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { ActualTokensCard } from '@/components/metrics/actual-tokens-card';
import { CATALOG_MODELS, storeSelectorStub } from '@/__tests__/helpers/cost-model-mocks';

vi.mock('@/lib/utils', () => ({
  formatNumber: (n: number) => n.toLocaleString('en-US'),
}));

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

describe('ActualTokensCard', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseCostModels.mockReturnValue({
      data: CATALOG_MODELS,
      isLoading: false,
      isError: false,
    });
    mockUseDashboardStore.mockImplementation(storeSelectorStub('local'));
  });

  it('renders formatted actual token value', () => {
    render(<ActualTokensCard totalActualTokensEstimated={8600} />);
    expect(screen.getByText('8,600')).toBeInTheDocument();
    expect(screen.getByText('tokens')).toBeInTheDocument();
  });

  it('shows an em dash when actual is null', () => {
    render(<ActualTokensCard totalActualTokensEstimated={null} />);
    expect(screen.getByText('—')).toBeInTheDocument();
  });

  it('exposes a named Actual (combined) region', () => {
    render(<ActualTokensCard totalActualTokensEstimated={1000} />);
    expect(
      screen.getByRole('region', { name: 'Actual (combined)' }),
    ).toBeInTheDocument();
  });

  it('describes SoftBudget prepared path without renaming the region', () => {
    render(<ActualTokensCard totalActualTokensEstimated={1000} />);

    const region = screen.getByRole('region', { name: 'Actual (combined)' });
    expect(region).toHaveTextContent(
      'SoftBudget prepared + completion + overhead',
    );
    expect(
      screen.queryByRole('region', { name: 'Actual Tokens' }),
    ).not.toBeInTheDocument();
  });

  it('formats zero actual tokens', () => {
    render(<ActualTokensCard totalActualTokensEstimated={0} />);
    expect(screen.getByText('0')).toBeInTheDocument();
  });
});

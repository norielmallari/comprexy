import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { BaselineTokensCard } from '@/components/metrics/baseline-tokens-card';
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

describe('BaselineTokensCard', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseCostModels.mockReturnValue({
      data: CATALOG_MODELS,
      isLoading: false,
      isError: false,
    });
    mockUseDashboardStore.mockImplementation(storeSelectorStub('local'));
  });

  it('renders formatted baseline token value', () => {
    render(<BaselineTokensCard totalBaselineTokensEstimated={12600} />);
    expect(screen.getByText('12,600')).toBeInTheDocument();
    expect(screen.getByText('tokens')).toBeInTheDocument();
  });

  it('shows an em dash when baseline is null', () => {
    render(<BaselineTokensCard totalBaselineTokensEstimated={null} />);
    expect(screen.getByText('—')).toBeInTheDocument();
  });

  it('exposes a named Baseline (combined) region', () => {
    render(<BaselineTokensCard totalBaselineTokensEstimated={1000} />);
    expect(
      screen.getByRole('region', { name: 'Baseline (combined)' }),
    ).toBeInTheDocument();
  });

  it('describes SoftBudget IR full + completion without renaming the region', () => {
    render(<BaselineTokensCard totalBaselineTokensEstimated={1000} />);

    const region = screen.getByRole('region', { name: 'Baseline (combined)' });
    expect(region).toHaveTextContent('SoftBudget IR full + completion');
    expect(
      screen.queryByRole('region', { name: 'Baseline Tokens' }),
    ).not.toBeInTheDocument();
  });

  it('formats zero baseline tokens', () => {
    render(<BaselineTokensCard totalBaselineTokensEstimated={0} />);
    expect(screen.getByText('0')).toBeInTheDocument();
  });
});

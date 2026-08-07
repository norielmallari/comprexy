import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { HeroCard } from '@/components/metrics/hero-card';
import {
  CATALOG_MODELS,
  storeSelectorStub,
} from '@/__tests__/helpers/cost-model-mocks';

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

describe('HeroCard', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseCostModels.mockReturnValue({
      data: CATALOG_MODELS,
      isLoading: false,
      isError: false,
    });
    mockUseDashboardStore.mockImplementation(storeSelectorStub('local'));
  });

  it('renders tokens saved value when provided', () => {
    render(<HeroCard tokensSaved={150000} />);
    expect(screen.getByText('150,000')).toBeInTheDocument();
  });

  it('omits `$` overlay for Local', () => {
    render(<HeroCard tokensSaved={4000} />);
    expect(screen.queryByLabelText(/Estimated cost/)).not.toBeInTheDocument();
  });

  it('shows `$` overlay for Sonnet', () => {
    mockUseDashboardStore.mockImplementation(storeSelectorStub('claude-sonnet-5'));
    render(<HeroCard tokensSaved={4000} />);
    // 4000 * $3/1M = $0.0120
    expect(screen.getByLabelText('Estimated cost $0.0120')).toBeInTheDocument();
  });

  it('shows placeholder when tokensSaved is null', () => {
    render(<HeroCard tokensSaved={null} />);
    expect(screen.getByText('—')).toBeInTheDocument();
  });

  it('renders tokens saved label', () => {
    render(<HeroCard tokensSaved={1000} />);
    expect(screen.getByText('Tokens Saved')).toBeInTheDocument();
  });

  it('formats small token values correctly', () => {
    render(<HeroCard tokensSaved={42} />);
    expect(screen.getByText('42')).toBeInTheDocument();
  });

  it('formats zero tokens saved', () => {
    render(<HeroCard tokensSaved={0} />);
    expect(screen.getByText('0')).toBeInTheDocument();
  });

  it('exposes a Tokens Saved region', () => {
    render(<HeroCard tokensSaved={1000} />);
    expect(
      screen.getByRole('region', { name: 'Tokens Saved' }),
    ).toBeInTheDocument();
  });

  it('keeps Tokens Saved accessible name and SoftBudget subtitle', () => {
    render(<HeroCard tokensSaved={1000} />);

    const region = screen.getByRole('region', { name: 'Tokens Saved' });
    expect(region).toHaveTextContent('Tokens Saved');
    expect(region).toHaveTextContent('Saved vs full history (full history − prepared)');
  });
});

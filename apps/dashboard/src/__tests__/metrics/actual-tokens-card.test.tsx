import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { ActualTokensCard } from '@/components/metrics/actual-tokens-card';

vi.mock('@/lib/utils', () => ({
  formatNumber: (n: number) => n.toLocaleString('en-US'),
}));

describe('ActualTokensCard', () => {
  it('renders formatted actual token value', () => {
    render(<ActualTokensCard totalActualTokensEstimated={8600} />);
    expect(screen.getByText('8,600')).toBeInTheDocument();
    expect(screen.getByText('tokens')).toBeInTheDocument();
  });

  it('shows an em dash when actual is null', () => {
    render(<ActualTokensCard totalActualTokensEstimated={null} />);
    expect(screen.getByText('—')).toBeInTheDocument();
  });

  it('exposes a named Actual Tokens region', () => {
    render(<ActualTokensCard totalActualTokensEstimated={1000} />);
    expect(
      screen.getByRole('region', { name: 'Actual Tokens' }),
    ).toBeInTheDocument();
  });

  it('formats zero actual tokens', () => {
    render(<ActualTokensCard totalActualTokensEstimated={0} />);
    expect(screen.getByText('0')).toBeInTheDocument();
  });
});

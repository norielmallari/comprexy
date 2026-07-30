import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { BaselineTokensCard } from '@/components/metrics/baseline-tokens-card';

vi.mock('@/lib/utils', () => ({
  formatNumber: (n: number) => n.toLocaleString('en-US'),
}));

describe('BaselineTokensCard', () => {
  it('renders formatted baseline token value', () => {
    render(<BaselineTokensCard totalBaselineTokensEstimated={12600} />);
    expect(screen.getByText('12,600')).toBeInTheDocument();
    expect(screen.getByText('tokens')).toBeInTheDocument();
  });

  it('shows an em dash when baseline is null', () => {
    render(<BaselineTokensCard totalBaselineTokensEstimated={null} />);
    expect(screen.getByText('—')).toBeInTheDocument();
  });

  it('exposes a named Baseline Tokens region', () => {
    render(<BaselineTokensCard totalBaselineTokensEstimated={1000} />);
    expect(
      screen.getByRole('region', { name: 'Baseline Tokens' }),
    ).toBeInTheDocument();
  });

  it('formats zero baseline tokens', () => {
    render(<BaselineTokensCard totalBaselineTokensEstimated={0} />);
    expect(screen.getByText('0')).toBeInTheDocument();
  });
});

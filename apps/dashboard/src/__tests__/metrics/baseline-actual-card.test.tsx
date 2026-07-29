import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { BaselineActualCard } from '@/components/metrics/baseline-actual-card';

vi.mock('@/lib/utils', () => ({
  formatNumber: (n: number) => n.toLocaleString('en-US'),
}));

describe('BaselineActualCard', () => {
  it('renders baseline and actual values', () => {
    render(
      <BaselineActualCard
        totalBaselineTokensEstimated={100000}
        totalActualTokensEstimated={35000}
      />,
    );
    expect(screen.getByText('100,000')).toBeInTheDocument();
    expect(screen.getByText('35,000')).toBeInTheDocument();
  });

  it('shows delta and savings percentage', () => {
    render(
      <BaselineActualCard
        totalBaselineTokensEstimated={100000}
        totalActualTokensEstimated={35000}
      />,
    );
    expect(screen.getByText(/Delta: 65,000 saved \(65\.0%\)/)).toBeInTheDocument();
  });

  it('shows placeholder when baseline is null', () => {
    render(
      <BaselineActualCard
        totalBaselineTokensEstimated={null}
        totalActualTokensEstimated={35000}
      />,
    );
    const dashes = screen.getAllByText('—');
    expect(dashes.length).toBeGreaterThanOrEqual(1);
  });

  it('shows placeholder when actual is null', () => {
    render(
      <BaselineActualCard
        totalBaselineTokensEstimated={100000}
        totalActualTokensEstimated={null}
      />,
    );
    const dashes = screen.getAllByText('—');
    expect(dashes.length).toBeGreaterThanOrEqual(1);
  });

  it('shows placeholder for both when both are null', () => {
    render(
      <BaselineActualCard
        totalBaselineTokensEstimated={null}
        totalActualTokensEstimated={null}
      />,
    );
    const dashes = screen.getAllByText('—');
    expect(dashes.length).toBeGreaterThanOrEqual(1);
  });

  it('renders labels', () => {
    render(
      <BaselineActualCard
        totalBaselineTokensEstimated={100000}
        totalActualTokensEstimated={35000}
      />,
    );
    expect(screen.getByText('Baseline vs Actual Tokens')).toBeInTheDocument();
    expect(screen.getByText('Baseline')).toBeInTheDocument();
    expect(screen.getByText('Actual')).toBeInTheDocument();
  });

  it('shows "over" when actual exceeds baseline', () => {
    render(
      <BaselineActualCard
        totalBaselineTokensEstimated={100000}
        totalActualTokensEstimated={120000}
      />,
    );
    expect(screen.getByText(/Delta: 20,000 over/)).toBeInTheDocument();
  });

  it('has role="region" on root', () => {
    const { container } = render(
      <BaselineActualCard
        totalBaselineTokensEstimated={100000}
        totalActualTokensEstimated={35000}
      />,
    );
    const region = container.querySelector('[role="region"]');
    expect(region).toBeInTheDocument();
  });
});

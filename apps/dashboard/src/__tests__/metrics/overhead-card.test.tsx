import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { OverheadCard } from '@/components/metrics/overhead-card';

vi.mock('@/components/ui/badge', () => ({
  Badge: ({ children, ...props }: any) => <span {...props}>{children}</span>,
}));

describe('OverheadCard', () => {
  it('renders overhead percentage', () => {
    render(
      <OverheadCard
        totalCompressionOverheadTokens={500}
        totalBaselineTokensEstimated={10000}
      />,
    );
    expect(screen.getByText('5.0')).toBeInTheDocument();
    expect(screen.getByText('%')).toBeInTheDocument();
  });

  it('calculates overhead percentage correctly', () => {
    render(
      <OverheadCard
        totalCompressionOverheadTokens={100}
        totalBaselineTokensEstimated={5000}
      />,
    );
    // 100 / 5000 * 100 = 2.0%
    expect(screen.getByText('2.0')).toBeInTheDocument();
    expect(screen.getByText('%')).toBeInTheDocument();
  });

  it('shows placeholder when no data', () => {
    render(
      <OverheadCard
        totalCompressionOverheadTokens={null}
        totalBaselineTokensEstimated={null}
      />,
    );
    expect(screen.getByText('—')).toBeInTheDocument();
  });

  it('shows placeholder when baseline is null', () => {
    render(
      <OverheadCard
        totalCompressionOverheadTokens={500}
        totalBaselineTokensEstimated={null}
      />,
    );
    expect(screen.getByText('—')).toBeInTheDocument();
  });

  it('shows placeholder when baseline is zero', () => {
    render(
      <OverheadCard
        totalCompressionOverheadTokens={500}
        totalBaselineTokensEstimated={0}
      />,
    );
    expect(screen.getByText('—')).toBeInTheDocument();
  });

  it('handles zero overhead tokens', () => {
    render(
      <OverheadCard
        totalCompressionOverheadTokens={0}
        totalBaselineTokensEstimated={10000}
      />,
    );
    expect(screen.getByText('0.0')).toBeInTheDocument();
    expect(screen.getByText('%')).toBeInTheDocument();
  });

  it('renders title "Overhead"', () => {
    render(
      <OverheadCard
        totalCompressionOverheadTokens={500}
        totalBaselineTokensEstimated={10000}
      />,
    );
    expect(screen.getByText('Overhead')).toBeInTheDocument();
  });

  it('handles large overhead values', () => {
    render(
      <OverheadCard
        totalCompressionOverheadTokens={50000}
        totalBaselineTokensEstimated={100000}
      />,
    );
    expect(screen.getByText('50.0')).toBeInTheDocument();
    expect(screen.getByText('%')).toBeInTheDocument();
  });

  it('handles small overhead percentage with decimals', () => {
    render(
      <OverheadCard
        totalCompressionOverheadTokens={7}
        totalBaselineTokensEstimated={1000}
      />,
    );
    // 7 / 1000 * 100 = 0.7%
    expect(screen.getByText('0.7')).toBeInTheDocument();
    expect(screen.getByText('%')).toBeInTheDocument();
  });

  it('handles null overhead with valid baseline', () => {
    render(
      <OverheadCard
        totalCompressionOverheadTokens={null}
        totalBaselineTokensEstimated={10000}
      />,
    );
    // null / 10000 * 100 = 0.0%
    expect(screen.getByText('0.0')).toBeInTheDocument();
    expect(screen.getByText('%')).toBeInTheDocument();
  });
});

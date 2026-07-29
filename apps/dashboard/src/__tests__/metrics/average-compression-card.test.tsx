import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { AverageCompressionCard } from '@/components/metrics/average-compression-card';

vi.mock('@/components/ui/badge', () => ({
  Badge: ({ children, ...props }: any) => <span {...props}>{children}</span>,
}));

describe('AverageCompressionCard', () => {
  it('renders percentage value', () => {
    render(<AverageCompressionCard averageTokenSavingsRatio={0.673} />);
    expect(screen.getByText('67.3')).toBeInTheDocument();
    expect(screen.getByText('%')).toBeInTheDocument();
  });

  it('formats percentage correctly', () => {
    render(<AverageCompressionCard averageTokenSavingsRatio={0.42} />);
    expect(screen.getByText('42.0')).toBeInTheDocument();
    expect(screen.getByText('%')).toBeInTheDocument();
  });

  it('shows placeholder when no data', () => {
    render(<AverageCompressionCard averageTokenSavingsRatio={null} />);
    expect(screen.getByText('—')).toBeInTheDocument();
  });

  it('renders title "Average Compression"', () => {
    render(<AverageCompressionCard averageTokenSavingsRatio={0.5} />);
    expect(screen.getByText('Average Compression')).toBeInTheDocument();
  });

  it('formats value with one decimal place', () => {
    render(<AverageCompressionCard averageTokenSavingsRatio={0.3333} />);
    expect(screen.getByText('33.3')).toBeInTheDocument();
    expect(screen.getByText('%')).toBeInTheDocument();
  });

  it('formats 100% compression correctly', () => {
    render(<AverageCompressionCard averageTokenSavingsRatio={1.0} />);
    expect(screen.getByText('100.0')).toBeInTheDocument();
    expect(screen.getByText('%')).toBeInTheDocument();
  });

  it('formats 0% compression correctly', () => {
    render(<AverageCompressionCard averageTokenSavingsRatio={0.0} />);
    expect(screen.getByText('0.0')).toBeInTheDocument();
    expect(screen.getByText('%')).toBeInTheDocument();
  });

  it('renders with a high compression ratio', () => {
    render(<AverageCompressionCard averageTokenSavingsRatio={0.95} />);
    expect(screen.getByText('95.0')).toBeInTheDocument();
    expect(screen.getByText('%')).toBeInTheDocument();
  });

  it('renders a low compression ratio', () => {
    render(<AverageCompressionCard averageTokenSavingsRatio={0.05} />);
    expect(screen.getByText('5.0')).toBeInTheDocument();
    expect(screen.getByText('%')).toBeInTheDocument();
  });
});

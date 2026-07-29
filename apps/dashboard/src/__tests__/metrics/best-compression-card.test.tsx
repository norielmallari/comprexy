import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { BestCompressionCard } from '@/components/metrics/best-compression-card';

vi.mock('@/components/ui/badge', () => ({
  Badge: ({ children, ...props }: { children?: React.ReactNode }) => (
    <span {...props}>{children}</span>
  ),
}));

describe('BestCompressionCard', () => {
  it('renders percentage value', () => {
    render(<BestCompressionCard bestCompressionRatio={0.673} />);
    expect(screen.getByText('67.3')).toBeInTheDocument();
    expect(screen.getByText('%')).toBeInTheDocument();
  });

  it('shows placeholder when no data', () => {
    render(<BestCompressionCard bestCompressionRatio={null} />);
    expect(screen.getByText('—')).toBeInTheDocument();
  });

  it('renders title "Best Compression"', () => {
    render(<BestCompressionCard bestCompressionRatio={0.5} />);
    expect(screen.getByText('Best Compression')).toBeInTheDocument();
  });

  it('formats 100% correctly', () => {
    render(<BestCompressionCard bestCompressionRatio={1.0} />);
    expect(screen.getByText('100.0')).toBeInTheDocument();
  });
});

import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { HeroCard } from '@/components/metrics/hero-card';

vi.mock('@/lib/utils', () => ({
  formatNumber: (n: number) => n.toLocaleString('en-US'),
}));

describe('HeroCard', () => {
  it('renders tokens saved value when provided', () => {
    render(<HeroCard tokensSaved={150000} weightedCompressionRatio={0.65} />);
    expect(screen.getByText('150,000')).toBeInTheDocument();
  });

  it('renders weighted compression value when provided', () => {
    render(<HeroCard tokensSaved={150000} weightedCompressionRatio={0.65} />);
    expect(screen.getByText('65.0%')).toBeInTheDocument();
  });

  it('shows placeholder when tokensSaved is null', () => {
    render(<HeroCard tokensSaved={null} weightedCompressionRatio={0.65} />);
    expect(screen.getByText('—')).toBeInTheDocument();
  });

  it('shows placeholder when weightedCompression is null', () => {
    render(<HeroCard tokensSaved={150000} weightedCompressionRatio={null} />);
    const dashes = screen.getAllByText('—');
    expect(dashes.length).toBeGreaterThanOrEqual(1);
  });

  it('shows placeholder for both when both are null', () => {
    render(<HeroCard tokensSaved={null} weightedCompressionRatio={null} />);
    const dashes = screen.getAllByText('—');
    expect(dashes.length).toBe(2);
  });

  it('renders with both values provided', () => {
    render(<HeroCard tokensSaved={50000} weightedCompressionRatio={0.8} />);
    expect(screen.getByText('50,000')).toBeInTheDocument();
    expect(screen.getByText('80.0%')).toBeInTheDocument();
  });

  it('renders tokens saved label', () => {
    render(<HeroCard tokensSaved={1000} weightedCompressionRatio={0.5} />);
    expect(screen.getByText('Tokens Saved')).toBeInTheDocument();
  });

  it('renders weighted compression label', () => {
    render(<HeroCard tokensSaved={1000} weightedCompressionRatio={0.5} />);
    expect(screen.getByText('Weighted Compression')).toBeInTheDocument();
  });

  it('formats small token values correctly', () => {
    render(<HeroCard tokensSaved={42} weightedCompressionRatio={0.1} />);
    expect(screen.getByText('42')).toBeInTheDocument();
  });

  it('formats zero tokens saved', () => {
    render(<HeroCard tokensSaved={0} weightedCompressionRatio={0.5} />);
    expect(screen.getByText('0')).toBeInTheDocument();
  });

  it('has role="region" on root', () => {
    const { container } = render(
      <HeroCard tokensSaved={1000} weightedCompressionRatio={0.5} />,
    );
    const region = container.querySelector('[role="region"]');
    expect(region).toBeInTheDocument();
  });

  it('displays correct decimal precision for compression', () => {
    render(<HeroCard tokensSaved={null} weightedCompressionRatio={0.333} />);
    expect(screen.getByText('33.3%')).toBeInTheDocument();
  });
});

import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { CompressionRatioCard } from '@/components/metrics/compression-ratio-card';

describe('CompressionRatioCard', () => {
  it('renders compression ratio values', () => {
    render(<CompressionRatioCard averageTokenSavingsRatio={0.65} />);
    const values = screen.getAllByText('65.0%');
    expect(values.length).toBe(2);
  });

  it('displays same value for Weighted and Average', () => {
    render(<CompressionRatioCard averageTokenSavingsRatio={0.8} />);
    const values = screen.getAllByText('80.0%');
    expect(values.length).toBe(2);
  });

  it('shows placeholder when ratio is null', () => {
    render(<CompressionRatioCard averageTokenSavingsRatio={null} />);
    const dashes = screen.getAllByText(/—/);
    expect(dashes.length).toBe(2);
  });

  it('renders labels', () => {
    render(<CompressionRatioCard averageTokenSavingsRatio={0.5} />);
    expect(screen.getByText('Compression Ratios')).toBeInTheDocument();
    expect(screen.getByText('Weighted Compression')).toBeInTheDocument();
    expect(screen.getByText('Average Compression')).toBeInTheDocument();
  });

  it('shows correct decimal precision', () => {
    render(<CompressionRatioCard averageTokenSavingsRatio={0.333} />);
    const values = screen.getAllByText('33.3%');
    expect(values.length).toBe(2);
  });

  it('has role="region" on root', () => {
    const { container } = render(<CompressionRatioCard averageTokenSavingsRatio={0.5} />);
    const region = container.querySelector('[role="region"]');
    expect(region).toBeInTheDocument();
  });
});

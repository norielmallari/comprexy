import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { CompressionHealthCard } from '@/components/metrics/compression-health-card';

vi.mock('@/lib/utils', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/lib/utils')>();
  return {
    ...actual,
    getWmColor: (v: number, _isDark: boolean) => {
      if (v === 0) return '#10b981';
      if (v === 1) return '#3b82f6';
      if (v === 2) return '#f59e0b';
      return '#ef4444';
    },
  };
});

vi.mock('@/hooks/use-theme', () => ({
  useTheme: () => ({ theme: 'light' }),
}));

describe('CompressionHealthCard', () => {
  it('renders best compression and overhead values', () => {
    render(
      <CompressionHealthCard
        bestCompressionRatio={0.95}
        totalCompressionOverheadTokens={5000}
        totalBaselineTokensEstimated={100000}
        maxWorkingMemoryVersion={2}
      />,
    );
    expect(screen.getByText('95.0%')).toBeInTheDocument();
    expect(screen.getByText('5.0%')).toBeInTheDocument();
  });

  it('renders working memory badge', () => {
    render(
      <CompressionHealthCard
        bestCompressionRatio={0.95}
        totalCompressionOverheadTokens={5000}
        totalBaselineTokensEstimated={100000}
        maxWorkingMemoryVersion={2}
      />,
    );
    expect(screen.getByText('v2')).toBeInTheDocument();
  });

  it('shows placeholder when best compression is null', () => {
    render(
      <CompressionHealthCard
        bestCompressionRatio={null}
        totalCompressionOverheadTokens={5000}
        totalBaselineTokensEstimated={100000}
        maxWorkingMemoryVersion={2}
      />,
    );
    expect(screen.getByText('—')).toBeInTheDocument();
  });

  it('shows placeholder when overhead values are null', () => {
    render(
      <CompressionHealthCard
        bestCompressionRatio={0.95}
        totalCompressionOverheadTokens={null}
        totalBaselineTokensEstimated={null}
        maxWorkingMemoryVersion={2}
      />,
    );
    expect(screen.getByText('—')).toBeInTheDocument();
  });

  it('shows placeholder when working memory is null', () => {
    render(
      <CompressionHealthCard
        bestCompressionRatio={0.95}
        totalCompressionOverheadTokens={5000}
        totalBaselineTokensEstimated={100000}
        maxWorkingMemoryVersion={null}
      />,
    );
    expect(screen.getByText('—')).toBeInTheDocument();
  });

  it('renders all labels', () => {
    render(
      <CompressionHealthCard
        bestCompressionRatio={0.95}
        totalCompressionOverheadTokens={5000}
        totalBaselineTokensEstimated={100000}
        maxWorkingMemoryVersion={2}
      />,
    );
    expect(screen.getByText('Compression Health')).toBeInTheDocument();
    expect(screen.getByText('Best Compression')).toBeInTheDocument();
    expect(screen.getByText('Overhead')).toBeInTheDocument();
    expect(screen.getByText('Working Memory')).toBeInTheDocument();
  });

  it('has role="region" on root', () => {
    const { container } = render(
      <CompressionHealthCard
        bestCompressionRatio={0.95}
        totalCompressionOverheadTokens={5000}
        totalBaselineTokensEstimated={100000}
        maxWorkingMemoryVersion={2}
      />,
    );
    const region = container.querySelector('[role="region"]');
    expect(region).toBeInTheDocument();
  });
});

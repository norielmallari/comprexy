import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { HeroCard } from '@/components/metrics/hero-card';

vi.mock('@/lib/utils', () => ({
  formatNumber: (n: number) => n.toLocaleString('en-US'),
}));

describe('HeroCard', () => {
  it('renders tokens saved value when provided', () => {
    render(<HeroCard tokensSaved={150000} />);
    expect(screen.getByText('150,000')).toBeInTheDocument();
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
    expect(region).toHaveTextContent('SoftBudget net (IR full − prepared)');
  });
});

import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { WeightedCompressionCard } from '@/components/metrics/weighted-compression-card';

describe('WeightedCompressionCard', () => {
  it('renders the aggregate savings ratio as a percentage', () => {
    render(<WeightedCompressionCard weightedTokenSavingsRatio={0.31746} />);

    expect(
      screen.getByRole('region', { name: 'Weighted Compression' }),
    ).toHaveTextContent('31.7');
  });

  it('shows a placeholder when no aggregate is available', () => {
    render(<WeightedCompressionCard weightedTokenSavingsRatio={null} />);

    expect(screen.getByText('—')).toBeInTheDocument();
  });
});

import { render, screen, fireEvent } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { CostModelPanel } from '@/components/benchmark/cost-model-panel';
import {
  COST_DISCLAIMER,
  DEFAULT_COST_RATES,
  LOCAL_COST_DISCLAIMER,
} from '@/lib/benchmark-cost';
import type { BenchmarkCostRates } from '@/types/api';

const baseRates: BenchmarkCostRates = { ...DEFAULT_COST_RATES };

describe('CostModelPanel', () => {
  it('shows local disclaimer by default', () => {
    render(
      <CostModelPanel
        modelKind="local"
        rates={baseRates}
        onModelKindChange={vi.fn()}
        onRatesChange={vi.fn()}
      />,
    );

    const disclaimer = screen.getByTestId('cost-model-disclaimer');
    expect(disclaimer).toHaveTextContent(LOCAL_COST_DISCLAIMER);
    expect(disclaimer).not.toHaveTextContent(COST_DISCLAIMER);
  });

  it('switches to USD disclaimer when USD is selected', () => {
    const onModelKindChange = vi.fn();
    const onRatesChange = vi.fn();

    render(
      <CostModelPanel
        modelKind="local"
        rates={baseRates}
        onModelKindChange={onModelKindChange}
        onRatesChange={onRatesChange}
      />,
    );

    fireEvent.click(screen.getByRole('button', { name: 'USD' }));

    expect(onModelKindChange).toHaveBeenCalledWith('usd');
    expect(onRatesChange).toHaveBeenCalled();
  });

  it('shows USD disclaimer and rate presets in USD mode', () => {
    render(
      <CostModelPanel
        modelKind="usd"
        rates={{ ...baseRates, modelKind: 'usd' }}
        onModelKindChange={vi.fn()}
        onRatesChange={vi.fn()}
      />,
    );

    expect(screen.getByTestId('cost-model-disclaimer')).toHaveTextContent(
      COST_DISCLAIMER,
    );
    expect(
      screen.getByRole('button', { name: 'Frontier default ($3 / $15 per 1M)' }),
    ).toBeInTheDocument();
    expect(screen.getByLabelText('Input $/1M')).toBeInTheDocument();
  });

  it('applies a preset when preset button is clicked', () => {
    const onRatesChange = vi.fn();

    render(
      <CostModelPanel
        modelKind="usd"
        rates={{ ...baseRates, modelKind: 'usd' }}
        onModelKindChange={vi.fn()}
        onRatesChange={onRatesChange}
      />,
    );

    fireEvent.click(
      screen.getByRole('button', { name: 'Local small ($0.50 / $2 per 1M)' }),
    );

    expect(onRatesChange).toHaveBeenCalledWith(
      expect.objectContaining({
        inputUsdPer1M: 0.5,
        outputUsdPer1M: 2,
        modelKind: 'usd',
      }),
    );
  });
});

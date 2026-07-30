import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { ChartTooltip } from '@/components/charts/chart-tooltip';
import type { ChartDataPoint } from '@/types/chart';

const mockDataPoint: ChartDataPoint = {
  turnIndex: 1,
  model: 'gpt-4',
  systemTokens: 3000,
  historyTokens: 5000,
  workingMemoryTokens: 2000,
  preparedPromptTokens: 10000,
  baselineTokens: 10500,
  workingMemoryVersion: 2,
  netTokensSaved: -500,
  savingsRatio: 0.05,
  softBudgetExceeded: false,
  hardBudgetExceeded: false,
};

describe('ChartTooltip', () => {
  it('renders active tooltip with data', () => {
    render(<ChartTooltip data={mockDataPoint} active={true} />);

    expect(screen.getByTestId('chart-tooltip')).toBeInTheDocument();
    expect(screen.getByText('Turn 1')).toBeInTheDocument();
    expect(screen.getByText('gpt-4')).toBeInTheDocument();
  });

  it('does not render when inactive', () => {
    render(<ChartTooltip data={mockDataPoint} active={false} />);

    expect(screen.queryByTestId('chart-tooltip')).not.toBeInTheDocument();
  });

  it('does not render when data is null', () => {
    render(<ChartTooltip data={null} active={true} />);

    expect(screen.queryByTestId('chart-tooltip')).not.toBeInTheDocument();
  });

  it('formats each prepared-prompt segment', () => {
    render(<ChartTooltip data={mockDataPoint} active={true} />);

    expect(screen.getByText('3.0K')).toBeInTheDocument();
    expect(screen.getByText('5.0K')).toBeInTheDocument();
    expect(screen.getByText('2.0K')).toBeInTheDocument();
  });

  it('formats prepared prompt and baseline totals', () => {
    render(<ChartTooltip data={mockDataPoint} active={true} />);

    expect(screen.getByText('10.0K')).toBeInTheDocument();
    expect(screen.getByText('10.5K')).toBeInTheDocument();
  });

  it('formats net tokens saved without a plus sign when negative', () => {
    render(<ChartTooltip data={mockDataPoint} active={true} />);

    expect(screen.getByText('-500.0')).toBeInTheDocument();
  });

  it('formats net tokens saved with a plus sign when positive', () => {
    render(<ChartTooltip data={{ ...mockDataPoint, netTokensSaved: 500 }} active={true} />);

    expect(screen.getByText('+500.0')).toBeInTheDocument();
  });

  it('formats the savings ratio as a percentage', () => {
    render(<ChartTooltip data={mockDataPoint} active={true} />);

    expect(screen.getByText('5.0%')).toBeInTheDocument();
  });

  it('renders soft and hard budget flags', () => {
    render(
      <ChartTooltip
        data={{ ...mockDataPoint, softBudgetExceeded: true, hardBudgetExceeded: true }}
        active={true}
      />,
    );

    expect(screen.getByText('Soft Budget')).toBeInTheDocument();
    expect(screen.getByText('Hard Budget')).toBeInTheDocument();
  });

  it('does not render budget flags when neither is exceeded', () => {
    render(<ChartTooltip data={mockDataPoint} active={true} />);

    expect(
      screen.getByTestId('chart-tooltip').querySelectorAll('span.rounded-full'),
    ).toHaveLength(0);
  });

  it('renders the working memory version', () => {
    render(<ChartTooltip data={mockDataPoint} active={true} />);

    expect(screen.getByText('WM v2')).toBeInTheDocument();
  });

  it('says no working memory exists yet instead of showing a zero WM version', () => {
    render(<ChartTooltip data={{ ...mockDataPoint, workingMemoryVersion: null }} active={true} />);

    expect(screen.getByText('No working memory yet')).toBeInTheDocument();
    expect(screen.getByText('none yet')).toBeInTheDocument();
    expect(screen.getByTestId('chart-tooltip').textContent).not.toContain('WM v');
  });

  it('renders all label rows', () => {
    render(<ChartTooltip data={mockDataPoint} active={true} />);

    const content = screen.getByTestId('chart-tooltip');
    expect(content).toHaveTextContent('System');
    expect(content).toHaveTextContent('History + tools');
    expect(content).toHaveTextContent('Compressed WM');
    expect(content).toHaveTextContent('Prepared prompt');
    expect(content).toHaveTextContent('Baseline (ghost)');
    expect(content).toHaveTextContent('Net Saved');
    expect(content).toHaveTextContent('Savings Ratio');
    expect(content).not.toHaveTextContent('Overhead');
  });

  it('applies emerald color for positive net tokens saved', () => {
    render(<ChartTooltip data={{ ...mockDataPoint, netTokensSaved: 500 }} active={true} />);

    expect(
      screen.getByTestId('chart-tooltip').querySelectorAll('span.text-emerald-600').length,
    ).toBeGreaterThan(0);
  });

  it('applies red color for negative net tokens saved', () => {
    render(<ChartTooltip data={mockDataPoint} active={true} />);

    expect(
      screen.getByTestId('chart-tooltip').querySelectorAll('span.text-red-600').length,
    ).toBeGreaterThan(0);
  });
});

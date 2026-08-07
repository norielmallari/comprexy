import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { ChartTooltip } from '@/components/charts/chart-tooltip';
import type { ChartDataPoint } from '@/types/chart';

const mockDataPoint: ChartDataPoint = {
  turnIndex: 1,
  model: 'gpt-4',
  systemTokens: 3000,
  virtualToolSchemaTokens: 400,
  clientToolSchemaTokens: 300,
  rulesTokens: 0,
  historyTokens: 4300,
  workingMemoryTokens: 2000,
  preparedPromptTokens: 10000,
  baselineTokens: 10500,
  virtualToolsTokensSaved: -200,
  isLegacyMixedAxis: false,
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
    expect(screen.getByText('400.0')).toBeInTheDocument();
    expect(screen.getByText('300.0')).toBeInTheDocument();
    expect(screen.getByText('4.3K')).toBeInTheDocument();
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
    expect(content).toHaveTextContent('Virtual tools (catalog)');
    expect(content).toHaveTextContent('Client tools (catalog)');
    expect(content).toHaveTextContent('History');
    expect(content).toHaveTextContent('Compressed WM');
    expect(content).toHaveTextContent('Prepared prompt');
    expect(content).toHaveTextContent('Full History Est.');
    expect(content).toHaveTextContent('Saved vs full history');
    expect(content).toHaveTextContent('Savings vs full history');
    expect(content).toHaveTextContent('VT / native-wire');
    expect(content).not.toHaveTextContent('not tools-only');
    expect(content).not.toHaveTextContent('may be negative');
    expect(content).not.toHaveTextContent('Rules');
    expect(content).not.toHaveTextContent('History + tools');
    expect(content).not.toHaveTextContent('Overhead');
    expect(content).not.toHaveTextContent('Baseline (ghost)');
    expect(content).not.toHaveTextContent('Net Saved');
    expect(content).not.toHaveTextContent('Savings Ratio');
  });

  it('shows a Rules row only when rulesTokens is greater than zero', () => {
    const { rerender } = render(<ChartTooltip data={mockDataPoint} active={true} />);
    expect(screen.getByTestId('chart-tooltip')).not.toHaveTextContent('Rules');

    rerender(
      <ChartTooltip
        data={{ ...mockDataPoint, rulesTokens: 150, historyTokens: 4150 }}
        active={true}
      />,
    );
    expect(screen.getByTestId('chart-tooltip')).toHaveTextContent('Rules');
    expect(screen.getByText('150.0')).toBeInTheDocument();
  });

  it('keeps VT / native-wire as a separate channel from catalog segments', () => {
    render(
      <ChartTooltip
        data={{
          ...mockDataPoint,
          virtualToolSchemaTokens: 400,
          virtualToolsTokensSaved: 900,
        }}
        active={true}
      />,
    );

    const content = screen.getByTestId('chart-tooltip');
    expect(content).toHaveTextContent('Virtual tools (catalog)');
    expect(content).toHaveTextContent('VT / native-wire');
    expect(screen.getByText('400.0')).toBeInTheDocument();
    expect(screen.getByText('+900.0')).toBeInTheDocument();
  });

  it('hides the VT / native-wire row when virtualToolsTokensSaved is null', () => {
    render(
      <ChartTooltip
        data={{ ...mockDataPoint, virtualToolsTokensSaved: null }}
        active={true}
      />,
    );

    const content = screen.getByTestId('chart-tooltip');
    expect(content).not.toHaveTextContent('VT / native-wire');
    expect(content).not.toHaveTextContent('not tools-only');
  });

  it('shows a legacy mixed-axis note when isLegacyMixedAxis is true', () => {
    render(
      <ChartTooltip
        data={{
          ...mockDataPoint,
          isLegacyMixedAxis: true,
          virtualToolsTokensSaved: null,
        }}
        active={true}
      />,
    );

    expect(screen.getByTestId('chart-tooltip')).toHaveTextContent(
      'Legacy mixed-axis — ghost uses NativeRaw',
    );
  });

  it('applies emerald color for positive net tokens saved', () => {
    render(<ChartTooltip data={{ ...mockDataPoint, netTokensSaved: 500 }} active={true} />);

    expect(
      screen.getByTestId('chart-tooltip').querySelectorAll('span.text-emerald-700').length,
    ).toBeGreaterThan(0);
  });

  it('applies red color for negative net tokens saved', () => {
    render(<ChartTooltip data={mockDataPoint} active={true} />);

    expect(
      screen.getByTestId('chart-tooltip').querySelectorAll('span.text-red-700').length,
    ).toBeGreaterThan(0);
  });
});

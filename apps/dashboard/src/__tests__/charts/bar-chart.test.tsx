import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { BarChart } from '@/components/charts/bar-chart';
import { CHART_HEIGHT, CHART_Y_AXIS_MIN } from '@/lib/constants';
import type { ChartDataPoint } from '@/types/chart';

const makePoint = (partial: Partial<ChartDataPoint> = {}): ChartDataPoint => ({
  turnIndex: 1,
  model: 'gpt-4',
  systemTokens: 3000,
  historyTokens: 5000,
  workingMemoryTokens: 2000,
  preparedPromptTokens: 10000,
  baselineTokens: 14000,
  workingMemoryVersion: 2,
  netTokensSaved: 4000,
  savingsRatio: 0.28,
  softBudgetExceeded: false,
  hardBudgetExceeded: false,
  ...partial,
});

const mockData: ChartDataPoint[] = [
  makePoint(),
  makePoint({
    turnIndex: 2,
    historyTokens: 7000,
    preparedPromptTokens: 12000,
    baselineTokens: 17000,
    netTokensSaved: 5000,
    softBudgetExceeded: true,
  }),
];

vi.mock('recharts', () => ({
  BarChart: ({ children, data, ...props }: any) => (
    <div data-testid="recharts-bar-chart" {...props}>
      <div data-testid="recharts-data">{JSON.stringify(data)}</div>
      {children}
    </div>
  ),
  Bar: ({ name, dataKey, stackId, xAxisId, ...props }: any) => (
    <div
      data-testid={`recharts-bar-${dataKey}`}
      data-name={name}
      data-datakey={dataKey}
      data-stackid={stackId}
      data-xaxisid={xAxisId}
      {...props}
    />
  ),
  XAxis: ({ dataKey, xAxisId, hide, ...props }: any) => (
    <div
      data-testid={xAxisId ? `recharts-xaxis-${xAxisId}` : 'recharts-xaxis'}
      data-datakey={dataKey}
      data-hidden={hide ? 'true' : undefined}
      {...props}
    />
  ),
  YAxis: ({ domain, ...props }: { domain?: [number, number] }) => (
    <div
      data-testid="recharts-yaxis"
      data-domain={domain ? JSON.stringify(domain) : undefined}
      {...props}
    />
  ),
  CartesianGrid: () => <div data-testid="recharts-cartesian-grid" />,
  ResponsiveContainer: ({ children, height }: any) => (
    <div data-testid="responsive-container" data-height={String(height)}>
      {children}
    </div>
  ),
  Tooltip: ({ content }: any) => (content ? <div data-testid="recharts-tooltip">{content}</div> : null),
}));

describe('BarChart', () => {
  it('renders chart with data', () => {
    render(<BarChart data={mockData} />);

    expect(screen.getByText('Token Counts by Turn')).toBeInTheDocument();
    expect(screen.getByTestId('recharts-bar-chart')).toBeInTheDocument();
  });

  it('exposes an accessible name on the chart root', () => {
    render(<BarChart data={mockData} />);

    const chart = screen.getByTestId('token-counts-by-turn-chart');
    expect(chart).toHaveAttribute('role', 'img');
    expect(chart.getAttribute('aria-label')).toMatch(/2 turns/);
  });

  it('renders empty state when no data', () => {
    render(<BarChart data={[]} />);

    expect(
      screen.getByText('No data to display. Select a conversation to view metrics.'),
    ).toBeInTheDocument();
    expect(screen.queryByTestId('recharts-bar-chart')).not.toBeInTheDocument();
  });

  it('renders loading skeleton when loading', () => {
    render(<BarChart data={[]} isLoading={true} />);

    expect(screen.getByText('Loading chart data...')).toBeInTheDocument();
    expect(screen.queryByTestId('recharts-bar-chart')).not.toBeInTheDocument();
  });

  it('renders loading skeleton even with data when loading', () => {
    render(<BarChart data={mockData} isLoading={true} />);

    expect(screen.getByText('Loading chart data...')).toBeInTheDocument();
    expect(screen.queryByText('Token Counts by Turn')).not.toBeInTheDocument();
  });

  it('stacks only the three prepared-prompt segments on one stackId', () => {
    render(<BarChart data={mockData} />);

    const segments = ['system', 'history', 'workingMemory'];
    for (const key of segments) {
      expect(screen.getByTestId(`recharts-bar-${key}`)).toHaveAttribute('data-stackid', 'prompt');
    }

    // The old chart double-counted the prompt via extra prompt/overhead segments.
    expect(screen.queryByTestId('recharts-bar-prompt')).not.toBeInTheDocument();
    expect(screen.queryByTestId('recharts-bar-overhead')).not.toBeInTheDocument();
  });

  it('emits segment values that sum to the prepared prompt', () => {
    render(<BarChart data={mockData} />);

    const rows = JSON.parse(screen.getByTestId('recharts-data').textContent || '[]');
    for (const row of rows) {
      expect(row.system + row.history + row.workingMemory).toBe(row.preparedPromptTokens);
    }
  });

  it('emits a zero working memory segment before the first version exists', () => {
    render(
      <BarChart
        data={[
          makePoint({
            workingMemoryVersion: null,
            workingMemoryTokens: 0,
            historyTokens: 7000,
          }),
        ]}
      />,
    );

    const rows = JSON.parse(screen.getByTestId('recharts-data').textContent || '[]');
    expect(rows[0].workingMemory).toBe(0);
  });

  it('renders the ghost bar outside the stack on its own hidden axis', () => {
    render(<BarChart data={mockData} />);

    const ghost = screen.getByTestId('recharts-bar-baseline');
    expect(ghost).toHaveAttribute('data-xaxisid', 'ghost');
    expect(ghost).not.toHaveAttribute('data-stackid', 'prompt');

    const ghostAxis = screen.getByTestId('recharts-xaxis-ghost');
    expect(ghostAxis).toHaveAttribute('data-hidden', 'true');
    expect(ghostAxis).toHaveAttribute('data-datakey', 'turnIndex');
  });

  it('declares the ghost bar before the stacked segments so it paints behind them', () => {
    render(<BarChart data={mockData} />);

    const bars = Array.from(document.querySelectorAll('[data-testid^="recharts-bar-"]')).map((el) =>
      el.getAttribute('data-datakey'),
    );

    expect(bars.indexOf('baseline')).toBeLessThan(bars.indexOf('system'));
  });

  it('renders XAxis with correct dataKey', () => {
    render(<BarChart data={mockData} />);

    expect(screen.getByTestId('recharts-xaxis')).toHaveAttribute('data-datakey', 'turnIndex');
  });

  it('renders YAxis and grid', () => {
    render(<BarChart data={mockData} />);

    expect(screen.getByTestId('recharts-yaxis')).toBeInTheDocument();
    expect(screen.getByTestId('recharts-cartesian-grid')).toBeInTheDocument();
  });

  it('renders legend items for the segments plus the ghost', () => {
    render(<BarChart data={mockData} />);

    expect(screen.getByText('System')).toBeInTheDocument();
    expect(screen.getByText('History + tools')).toBeInTheDocument();
    expect(screen.getByText('Compressed WM')).toBeInTheDocument();
    expect(screen.getByText('Baseline (ghost)')).toBeInTheDocument();
    expect(screen.queryByText('Overhead')).not.toBeInTheDocument();
    expect(screen.queryByText('Prompt')).not.toBeInTheDocument();
  });

  it('renders footer note', () => {
    render(<BarChart data={mockData} />);

    expect(
      screen.getByText(/prompt Comprexy actually prepared for each turn/),
    ).toBeInTheDocument();
  });

  it('renders ResponsiveContainer', () => {
    render(<BarChart data={mockData} />);

    expect(screen.getByTestId('responsive-container')).toBeInTheDocument();
  });

  it('uses fixed height by default', () => {
    render(<BarChart data={mockData} />);

    expect(screen.getByTestId('responsive-container')).toHaveAttribute(
      'data-height',
      String(CHART_HEIGHT),
    );
  });

  it('fills parent height when fill is set', () => {
    render(<BarChart fill data={mockData} />);

    expect(screen.getByTestId('responsive-container')).toHaveAttribute(
      'data-height',
      '100%',
    );
  });

  it('renders chart title as a heading', () => {
    render(<BarChart data={mockData} />);

    expect(screen.getByRole('heading', { level: 3 })).toHaveTextContent('Token Counts by Turn');
  });

  it('handles a single data point', () => {
    render(<BarChart data={[mockData[0]]} />);

    expect(screen.getByTestId('recharts-bar-chart')).toBeInTheDocument();
  });

  it('renders with many data points', () => {
    const largeData = Array.from({ length: 10 }, (_, i) =>
      makePoint({ turnIndex: i + 1, workingMemoryVersion: i % 3 }),
    );

    render(<BarChart data={largeData} />);

    expect(screen.getByTestId('recharts-bar-chart')).toBeInTheDocument();
  });

  it('uses sharedMaxY for Y-axis domain when provided', () => {
    render(<BarChart data={mockData} sharedMaxY={50_000} />);

    expect(screen.getByTestId('recharts-yaxis')).toHaveAttribute(
      'data-domain',
      JSON.stringify([CHART_Y_AXIS_MIN, 50_000]),
    );
  });

  it('renders custom title when title prop is set', () => {
    render(<BarChart data={mockData} title="Baseline chart" />);

    expect(screen.getByRole('heading', { level: 3 })).toHaveTextContent('Baseline chart');
  });

  it('uses custom testId when testId prop is set', () => {
    render(<BarChart data={mockData} testId="baseline-token-chart" />);

    expect(screen.getByTestId('baseline-token-chart')).toBeInTheDocument();
    expect(screen.queryByTestId('token-counts-by-turn-chart')).not.toBeInTheDocument();
  });
});

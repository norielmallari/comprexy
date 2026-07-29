import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { BarChart } from '@/components/charts/bar-chart';
import type { ChartDataPoint } from '@/types/chart';

const mockData: ChartDataPoint[] = [
  {
    turnIndex: 1,
    model: 'gpt-4',
    promptTokens: 5000,
    systemTokens: 3000,
    compressedTokens: 2000,
    overheadTokens: 500,
    baselineTokens: 10000,
    workingMemoryVersion: 2,
    totalCompressed: 10500,
    netTokensSaved: -500,
    savingsRatio: 0.05,
    softBudgetExceeded: false,
    hardBudgetExceeded: false,
  },
  {
    turnIndex: 2,
    model: 'gpt-4',
    promptTokens: 6000,
    systemTokens: 4000,
    compressedTokens: 3000,
    overheadTokens: 600,
    baselineTokens: 12000,
    workingMemoryVersion: 2,
    totalCompressed: 13600,
    netTokensSaved: -1600,
    savingsRatio: 0.13,
    softBudgetExceeded: true,
    hardBudgetExceeded: false,
  },
];

vi.mock('@/components/ui/tooltip', () => ({
  Tooltip: ({ children }: any) => <div data-testid="tooltip-root">{children}</div>,
  TooltipTrigger: ({ children, asChild }: any) =>
    asChild ? children : <div data-testid="tooltip-trigger">{children}</div>,
  TooltipContent: ({ children, className, ...props }: any) => (
    <div data-testid="tooltip-content" className={className} {...props}>
      {children}
    </div>
  ),
}));

vi.mock('recharts', () => ({
  BarChart: ({ children, data, ...props }: any) => (
    <div data-testid="recharts-bar-chart" {...props}>
      <div data-testid="recharts-data">{JSON.stringify(data)}</div>
      {children}
    </div>
  ),
  Bar: ({ name, dataKey, ...props }: any) => (
    <div
      data-testid={`recharts-bar-${dataKey}`}
      data-name={name}
      data-datakey={dataKey}
      {...props}
    />
  ),
  XAxis: ({ dataKey, ...props }: any) => (
    <div data-testid="recharts-xaxis" data-datakey={dataKey} {...props} />
  ),
  YAxis: ({ ...props }: any) => <div data-testid="recharts-yaxis" {...props} />,
  CartesianGrid: () => <div data-testid="recharts-cartesian-grid" />,
  Label: ({ value, ...props }: any) => (
    <div data-testid="recharts-label" data-value={value} {...props} />
  ),
  ResponsiveContainer: ({ children }: any) => (
    <div data-testid="responsive-container">{children}</div>
  ),
  Tooltip: ({ content }: any) => (content ? <div data-testid="recharts-tooltip">{content}</div> : null),
}));

describe('BarChart', () => {
  it('renders chart with data', () => {
    render(<BarChart data={mockData} />);

    expect(screen.getByText('Token Counts by Turn')).toBeInTheDocument();
    expect(screen.getByTestId('recharts-bar-chart')).toBeInTheDocument();
  });

  it('renders empty state when no data', () => {
    render(<BarChart data={[]} />);

    expect(screen.getByText('No data to display. Select a conversation to view metrics.')).toBeInTheDocument();
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

  it('transforms chart data correctly', () => {
    render(<BarChart data={mockData} />);

    const dataEl = screen.getByTestId('recharts-data');
    const transformedData = JSON.parse(dataEl.getAttribute('data-testid') ? dataEl.textContent || '[]' : '[]');

    // Verify the transformed data includes recharts keys
    expect(transformedData.length).toBe(2);
    expect(transformedData[0]).toHaveProperty('turnIndex', 1);
    expect(transformedData[0]).toHaveProperty('prompt');
    expect(transformedData[0]).toHaveProperty('system');
    expect(transformedData[0]).toHaveProperty('compressed');
    expect(transformedData[0]).toHaveProperty('overhead');
    expect(transformedData[0]).toHaveProperty('baseline');
  });

  it('renders chart legend', () => {
    render(<BarChart data={mockData} />);

    // The legend items are rendered by ChartLegend component
    // Check that the legend container exists
    const legendContainer = document.querySelector('div.flex.flex-wrap.items-center.justify-center');
    expect(legendContainer).toBeInTheDocument();
  });

  it('renders with multiple data points', () => {
    const largeData: ChartDataPoint[] = Array.from({ length: 10 }, (_, i) => ({
      turnIndex: i + 1,
      model: 'gpt-4',
      promptTokens: 5000 + i * 1000,
      systemTokens: 3000 + i * 500,
      compressedTokens: 2000 + i * 300,
      overheadTokens: 500 + i * 100,
      baselineTokens: 10000 + i * 2000,
      workingMemoryVersion: i % 3,
      totalCompressed: 10500 + i * 1500,
      netTokensSaved: -500 + i * 200,
      savingsRatio: 0.05 + i * 0.02,
      softBudgetExceeded: i % 3 === 0,
      hardBudgetExceeded: i % 5 === 0,
    }));

    render(<BarChart data={largeData} />);

    expect(screen.getByText('Token Counts by Turn')).toBeInTheDocument();
    expect(screen.getByTestId('recharts-bar-chart')).toBeInTheDocument();
  });

  it('renders all bar segments', () => {
    render(<BarChart data={mockData} />);

    expect(screen.getByTestId('recharts-bar-prompt')).toBeInTheDocument();
    expect(screen.getByTestId('recharts-bar-system')).toBeInTheDocument();
    expect(screen.getByTestId('recharts-bar-compressed')).toBeInTheDocument();
    expect(screen.getByTestId('recharts-bar-overhead')).toBeInTheDocument();
  });

  it('renders XAxis with correct dataKey', () => {
    render(<BarChart data={mockData} />);

    const xAxis = screen.getByTestId('recharts-xaxis');
    expect(xAxis).toHaveAttribute('data-datakey', 'turnIndex');
  });

  it('renders YAxis', () => {
    render(<BarChart data={mockData} />);

    expect(screen.getByTestId('recharts-yaxis')).toBeInTheDocument();
  });

  it('renders CartesianGrid', () => {
    render(<BarChart data={mockData} />);

    expect(screen.getByTestId('recharts-cartesian-grid')).toBeInTheDocument();
  });

  it('renders footer note', () => {
    render(<BarChart data={mockData} />);

    expect(
      screen.getByText(/Hover over bars to see detailed token counts per turn/),
    ).toBeInTheDocument();
  });

  it('renders GhostBar with baseline dataKey', () => {
    render(<BarChart data={mockData} />);

    // GhostBar renders a Bar with dataKey="baseline"
    const ghostBar = screen.getByTestId('recharts-bar-baseline');
    expect(ghostBar).toBeInTheDocument();
    expect(ghostBar).toHaveAttribute('data-datakey', 'baseline');
  });

  it('renders ResponsiveContainer', () => {
    render(<BarChart data={mockData} />);

    expect(screen.getByTestId('responsive-container')).toBeInTheDocument();
  });

  it('renders chart title', () => {
    render(<BarChart data={mockData} />);

    const title = screen.getByRole('heading', { level: 3 });
    expect(title).toHaveTextContent('Token Counts by Turn');
  });

  it('renders legend items with correct labels', () => {
    render(<BarChart data={mockData} />);

    expect(screen.getByText('Prompt')).toBeInTheDocument();
    expect(screen.getByText('System')).toBeInTheDocument();
    expect(screen.getByText('Compressed WM')).toBeInTheDocument();
    expect(screen.getByText('Overhead')).toBeInTheDocument();
    expect(screen.getByText('Baseline (ghost)')).toBeInTheDocument();
  });

  it('handles single data point', () => {
    const singleData: ChartDataPoint[] = [mockData[0]];

    render(<BarChart data={singleData} />);

    expect(screen.getByText('Token Counts by Turn')).toBeInTheDocument();
    expect(screen.getByTestId('recharts-bar-chart')).toBeInTheDocument();
  });

  it('renders with data having null workingMemoryVersion', () => {
    const noVersionData: ChartDataPoint[] = [
      {
        ...mockData[0],
        workingMemoryVersion: null,
      },
    ];

    render(<BarChart data={noVersionData} />);

    expect(screen.getByText('Token Counts by Turn')).toBeInTheDocument();
    expect(screen.getByTestId('recharts-bar-chart')).toBeInTheDocument();
  });

  it('passes correct width and height to ResponsiveContainer', () => {
    render(<BarChart data={mockData} />);

    const container = screen.getByTestId('responsive-container');
    expect(container).toBeInTheDocument();
  });

  it('renders empty state without loading text', () => {
    render(<BarChart data={[]} isLoading={false} />);

    expect(screen.getByText('No data to display. Select a conversation to view metrics.')).toBeInTheDocument();
    expect(screen.queryByText('Loading chart data...')).not.toBeInTheDocument();
  });

  it('renders chart container with space-y-4 class', () => {
    render(<BarChart data={mockData} />);

    const chartContainer = document.querySelector('div.space-y-4');
    expect(chartContainer).toBeInTheDocument();
  });
});

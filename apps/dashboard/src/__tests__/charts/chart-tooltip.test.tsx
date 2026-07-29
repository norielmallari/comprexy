import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { ChartTooltip } from '@/components/charts/chart-tooltip';
import type { ChartDataPoint } from '@/types/chart';

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

const mockDataPoint: ChartDataPoint = {
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
};

describe('ChartTooltip', () => {
  it('renders active tooltip with data', () => {
    render(<ChartTooltip data={mockDataPoint} active={true} />);

    expect(screen.getByTestId('tooltip-content')).toBeInTheDocument();
    expect(screen.getByText('Turn 1')).toBeInTheDocument();
    expect(screen.getByText('gpt-4')).toBeInTheDocument();
  });

  it('does not render when inactive', () => {
    const { container } = render(<ChartTooltip data={mockDataPoint} active={false} />);

    const content = container.querySelector('[data-testid="tooltip-content"]');
    expect(content).not.toBeInTheDocument();
  });

  it('does not render when data is null', () => {
    const { container } = render(<ChartTooltip data={null} active={true} />);

    const content = container.querySelector('[data-testid="tooltip-content"]');
    expect(content).not.toBeInTheDocument();
  });

  it('does not render when both inactive and null data', () => {
    const { container } = render(<ChartTooltip data={null} active={false} />);

    const content = container.querySelector('[data-testid="tooltip-content"]');
    expect(content).not.toBeInTheDocument();
  });

  it('formats prompt tokens using formatCompactNumber', () => {
    render(<ChartTooltip data={mockDataPoint} active={true} />);

    expect(screen.getByText('5.0K')).toBeInTheDocument();
  });

  it('formats system tokens using formatCompactNumber', () => {
    render(<ChartTooltip data={mockDataPoint} active={true} />);

    expect(screen.getByText('3.0K')).toBeInTheDocument();
  });

  it('formats compressed tokens using formatCompactNumber', () => {
    render(<ChartTooltip data={mockDataPoint} active={true} />);

    expect(screen.getByText('2.0K')).toBeInTheDocument();
  });

  it('formats overhead tokens using formatCompactNumber', () => {
    render(<ChartTooltip data={mockDataPoint} active={true} />);

    expect(screen.getByText('500.0')).toBeInTheDocument();
  });

  it('formats total compressed using formatCompactNumber', () => {
    render(<ChartTooltip data={mockDataPoint} active={true} />);

    expect(screen.getByText('10.5K')).toBeInTheDocument();
  });

  it('formats baseline tokens using formatCompactNumber', () => {
    render(<ChartTooltip data={mockDataPoint} active={true} />);

    expect(screen.getByText('10.0K')).toBeInTheDocument();
  });

  it('formats net tokens saved using formatCompactNumber', () => {
    render(<ChartTooltip data={mockDataPoint} active={true} />);

    // -500 should show as "-500.0" (negative, no plus sign)
    expect(screen.getByText('-500.0')).toBeInTheDocument();
  });

  it('formats net tokens saved with plus sign when positive', () => {
    const positiveData = { ...mockDataPoint, netTokensSaved: 500 };
    render(<ChartTooltip data={positiveData} active={true} />);

    expect(screen.getByText('+500.0')).toBeInTheDocument();
  });

  it('formats percentage using formatPercentage', () => {
    render(<ChartTooltip data={mockDataPoint} active={true} />);

    expect(screen.getByText('5.0%')).toBeInTheDocument();
  });

  it('renders soft budget exceeded flag', () => {
    const budgetData = { ...mockDataPoint, softBudgetExceeded: true };
    render(<ChartTooltip data={budgetData} active={true} />);

    expect(screen.getByText('Soft Budget')).toBeInTheDocument();
  });

  it('renders hard budget exceeded flag', () => {
    const budgetData = { ...mockDataPoint, hardBudgetExceeded: true };
    render(<ChartTooltip data={budgetData} active={true} />);

    expect(screen.getByText('Hard Budget')).toBeInTheDocument();
  });

  it('renders both budget flags when both exceeded', () => {
    const budgetData = { ...mockDataPoint, softBudgetExceeded: true, hardBudgetExceeded: true };
    render(<ChartTooltip data={budgetData} active={true} />);

    expect(screen.getByText('Soft Budget')).toBeInTheDocument();
    expect(screen.getByText('Hard Budget')).toBeInTheDocument();
  });

  it('does not render budget flags when neither exceeded', () => {
    const { container } = render(<ChartTooltip data={mockDataPoint} active={true} />);

    const budgetFlags = container.querySelectorAll('span.rounded-full');
    expect(budgetFlags).toHaveLength(0);
  });

  it('renders working memory version', () => {
    render(<ChartTooltip data={mockDataPoint} active={true} />);

    expect(screen.getByText('WM v2')).toBeInTheDocument();
  });

  it('does not render working memory version when null', () => {
    const noVersionData = { ...mockDataPoint, workingMemoryVersion: null };
    const { container } = render(<ChartTooltip data={noVersionData} active={true} />);

    const wmText = container.querySelector('[data-testid="tooltip-content"]')?.textContent;
    expect(wmText).not?.toContain('WM v');
  });

  it('renders all label-value pairs in tooltip', () => {
    render(<ChartTooltip data={mockDataPoint} active={true} />);

    const content = screen.getByTestId('tooltip-content');
    expect(content).toHaveTextContent('Prompt');
    expect(content).toHaveTextContent('System');
    expect(content).toHaveTextContent('Compressed WM');
    expect(content).toHaveTextContent('Overhead');
    expect(content).toHaveTextContent('Total Compressed');
    expect(content).toHaveTextContent('Baseline (ghost)');
    expect(content).toHaveTextContent('Net Saved');
    expect(content).toHaveTextContent('Savings Ratio');
  });

  it('applies emerald color for positive net tokens saved', () => {
    const positiveData = { ...mockDataPoint, netTokensSaved: 500 };
    const { container } = render(<ChartTooltip data={positiveData} active={true} />);

    const content = container.querySelector('[data-testid="tooltip-content"]');
    // Net Saved row has a plus sign and emerald color
    expect(content?.textContent).toContain('+500.0');
    // The emerald span should be present
    const emeraldSpans = content?.querySelectorAll('span.text-emerald-600');
    expect(emeraldSpans && emeraldSpans.length > 0).toBe(true);
  });

  it('applies red color for negative net tokens saved', () => {
    const { container } = render(<ChartTooltip data={mockDataPoint} active={true} />);

    const content = container.querySelector('[data-testid="tooltip-content"]');
    const netSavedSpans = content?.querySelectorAll('span.font-mono.font-medium');
    const hasRed = Array.from(netSavedSpans || []).some((s) => s.className.includes('text-red-600'));
    expect(hasRed).toBe(true);
  });
});

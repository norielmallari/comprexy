import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { BudgetTriggersCard } from '@/components/metrics/budget-triggers-card';

vi.mock('@/components/ui/badge', () => ({
  Badge: ({ children, variant, ...props }: any) => (
    <span data-badge-variant={variant} {...props}>{children}</span>
  ),
}));

describe('BudgetTriggersCard', () => {
  it('renders trigger count', () => {
    render(<BudgetTriggersCard budgetTriggerCount={5} />);
    expect(screen.getByText('5')).toBeInTheDocument();
  });

  it('shows badge when triggers exist', () => {
    render(<BudgetTriggersCard budgetTriggerCount={3} />);
    const badge = screen.getByText(/turns? exceeded budget/);
    expect(badge).toBeInTheDocument();
  });

  it('does not show badge when no triggers', () => {
    render(<BudgetTriggersCard budgetTriggerCount={0} />);
    const badge = screen.queryByText(/turns? exceeded budget/);
    expect(badge).not.toBeInTheDocument();
  });

  it('does not show badge when null', () => {
    render(<BudgetTriggersCard budgetTriggerCount={null} />);
    const badge = screen.queryByText(/turns? exceeded budget/);
    expect(badge).not.toBeInTheDocument();
  });

  it('renders with triggers data', () => {
    render(<BudgetTriggersCard budgetTriggerCount={10} />);
    expect(screen.getByText('10')).toBeInTheDocument();
    expect(screen.getByText('10 turns exceeded budget')).toBeInTheDocument();
  });

  it('renders singular "turn" when count is 1', () => {
    render(<BudgetTriggersCard budgetTriggerCount={1} />);
    expect(screen.getByText('1 turn exceeded budget')).toBeInTheDocument();
  });

  it('renders plural "turns" when count is greater than 1', () => {
    render(<BudgetTriggersCard budgetTriggerCount={5} />);
    expect(screen.getByText('5 turns exceeded budget')).toBeInTheDocument();
  });

  it('renders title "Budget Triggers"', () => {
    render(<BudgetTriggersCard budgetTriggerCount={3} />);
    expect(screen.getByText('Budget Triggers')).toBeInTheDocument();
  });

  it('shows placeholder when count is null', () => {
    render(<BudgetTriggersCard budgetTriggerCount={null} />);
    expect(screen.getByText('—')).toBeInTheDocument();
  });

  it('renders compact variant for zero triggers', () => {
    const { container } = render(<BudgetTriggersCard budgetTriggerCount={0} />);
    const root = container.querySelector('div');
    expect(root?.className).toContain('space-y-3');
  });
});

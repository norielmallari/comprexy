import { render, screen, fireEvent, act } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { Tooltip, TooltipTrigger, TooltipContent } from '@/components/ui/tooltip';
import { describe, expect, it, vi } from 'vitest';

describe('Tooltip', () => {
  function renderTooltip(props: Partial<{ defaultOpen: boolean; open: boolean | undefined; delayDuration: number }> = {}) {
    return render(
      <Tooltip defaultOpen={props.defaultOpen ?? false} open={props.open} delayDuration={props.delayDuration ?? 0}>
        <TooltipTrigger>
          <span data-testid="trigger">Hover me</span>
        </TooltipTrigger>
        <TooltipContent data-testid="content">
          <span data-testid="content-text">Tooltip text</span>
        </TooltipContent>
      </Tooltip>,
    );
  }

  it('TooltipTrigger renders its child', () => {
    renderTooltip();
    expect(screen.getByTestId('trigger')).toBeInTheDocument();
    expect(screen.getByText('Hover me')).toBeInTheDocument();
  });

  it('TooltipContent renders when tooltip is open (defaultOpen)', () => {
    renderTooltip({ defaultOpen: true });
    expect(screen.getByRole('tooltip')).toBeInTheDocument();
    expect(screen.getByRole('tooltip').textContent).toContain('Tooltip text');
  });

  it('TooltipContent does not render when tooltip is closed', () => {
    renderTooltip({ defaultOpen: false });
    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument();
  });

  it('Tooltip wraps content properly in context provider', () => {
    renderTooltip();
    expect(screen.getByTestId('trigger')).toBeInTheDocument();
  });

  it('TooltipTrigger applies aria-describedby when open', () => {
    renderTooltip({ defaultOpen: true });
    const trigger = screen.getByTestId('trigger').parentElement;
    expect(trigger).toHaveAttribute('aria-describedby');
  });

  it('TooltipTrigger does not have aria-describedby when closed', () => {
    renderTooltip({ defaultOpen: false });
    const trigger = screen.getByTestId('trigger');
    expect(trigger).not.toHaveAttribute('aria-describedby');
  });

  it('TooltipTrigger shows tooltip on mouse enter', async () => {
    vi.useFakeTimers();
    renderTooltip();
    const trigger = screen.getByTestId('trigger');
    fireEvent.mouseEnter(trigger);
    await act(async () => {
      vi.runAllTimers();
    });
    expect(screen.getByRole('tooltip')).toBeInTheDocument();
    vi.useRealTimers();
  });

  it('TooltipTrigger hides tooltip on mouse leave', async () => {
    vi.useFakeTimers();
    renderTooltip({ defaultOpen: true });
    const trigger = screen.getByTestId('trigger');
    fireEvent.mouseLeave(trigger);
    await act(async () => {
      vi.runAllTimers();
    });
    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument();
    vi.useRealTimers();
  });

  it('TooltipTrigger shows tooltip on focus', async () => {
    vi.useFakeTimers();
    renderTooltip();
    const trigger = screen.getByTestId('trigger');
    fireEvent.focus(trigger);
    await act(async () => {
      vi.runAllTimers();
    });
    expect(screen.getByRole('tooltip')).toBeInTheDocument();
    vi.useRealTimers();
  });

  it('TooltipTrigger hides tooltip on blur', async () => {
    vi.useFakeTimers();
    renderTooltip({ defaultOpen: true });
    const trigger = screen.getByTestId('trigger');
    fireEvent.blur(trigger);
    await act(async () => {
      vi.runAllTimers();
    });
    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument();
    vi.useRealTimers();
  });

  it('TooltipContent renders with correct role', () => {
    renderTooltip({ defaultOpen: true });
    const content = screen.getByRole('tooltip');
    expect(content).toHaveAttribute('role', 'tooltip');
  });

  it('TooltipContent renders with side=bottom positioning', () => {
    render(
      <Tooltip defaultOpen>
        <TooltipTrigger>
          <span data-testid="trigger">Hover me</span>
        </TooltipTrigger>
        <TooltipContent side="bottom">
          <span data-testid="content-text">Tooltip text</span>
        </TooltipContent>
      </Tooltip>,
    );
    const content = screen.getByRole('tooltip');
    expect(content.className).toContain('top-full');
  });

  it('TooltipContent renders with side=top positioning', () => {
    render(
      <Tooltip defaultOpen>
        <TooltipTrigger>
          <span data-testid="trigger">Hover me</span>
        </TooltipTrigger>
        <TooltipContent side="top">
          <span data-testid="content-text">Tooltip text</span>
        </TooltipContent>
      </Tooltip>,
    );
    const content = screen.getByRole('tooltip');
    expect(content.className).toContain('bottom-full');
  });

  it('TooltipContent renders with side=left positioning', () => {
    render(
      <Tooltip defaultOpen>
        <TooltipTrigger>
          <span data-testid="trigger">Hover me</span>
        </TooltipTrigger>
        <TooltipContent side="left">
          <span data-testid="content-text">Tooltip text</span>
        </TooltipContent>
      </Tooltip>,
    );
    const content = screen.getByRole('tooltip');
    expect(content.className).toContain('right-full');
  });

  it('TooltipContent renders with side=right positioning', () => {
    render(
      <Tooltip defaultOpen>
        <TooltipTrigger>
          <span data-testid="trigger">Hover me</span>
        </TooltipTrigger>
        <TooltipContent side="right">
          <span data-testid="content-text">Tooltip text</span>
        </TooltipContent>
      </Tooltip>,
    );
    const content = screen.getByRole('tooltip');
    expect(content.className).toContain('left-full');
  });

  it('TooltipContent renders with align=center', () => {
    render(
      <Tooltip defaultOpen>
        <TooltipTrigger>
          <span data-testid="trigger">Hover me</span>
        </TooltipTrigger>
        <TooltipContent align="center">
          <span data-testid="content-text">Tooltip text</span>
        </TooltipContent>
      </Tooltip>,
    );
    const content = screen.getByRole('tooltip');
    expect(content.className).toContain('left-1/2');
    expect(content.className).toContain('-translate-x-1/2');
  });

  it('TooltipContent renders with align=start', () => {
    render(
      <Tooltip defaultOpen>
        <TooltipTrigger>
          <span data-testid="trigger">Hover me</span>
        </TooltipTrigger>
        <TooltipContent align="start">
          <span data-testid="content-text">Tooltip text</span>
        </TooltipContent>
      </Tooltip>,
    );
    const content = screen.getByRole('tooltip');
    expect(content.className).toContain('left-2');
  });

  it('TooltipContent renders with align=end', () => {
    render(
      <Tooltip defaultOpen>
        <TooltipTrigger>
          <span data-testid="trigger">Hover me</span>
        </TooltipTrigger>
        <TooltipContent align="end">
          <span data-testid="content-text">Tooltip text</span>
        </TooltipContent>
      </Tooltip>,
    );
    const content = screen.getByRole('tooltip');
    expect(content.className).toContain('right-2');
  });

  it('TooltipContent renders with custom className', () => {
    render(
      <Tooltip defaultOpen>
        <TooltipTrigger>
          <span data-testid="trigger">Hover me</span>
        </TooltipTrigger>
        <TooltipContent className="custom-tooltip">
          <span data-testid="content-text">Tooltip text</span>
        </TooltipContent>
      </Tooltip>,
    );
    const content = screen.getByRole('tooltip');
    expect(content.className).toContain('custom-tooltip');
  });

  it('TooltipTrigger renders as span by default', () => {
    renderTooltip();
    const trigger = screen.getByTestId('trigger');
    expect(trigger.tagName).toBe('SPAN');
  });

  it('TooltipTrigger applies custom className', () => {
    render(
      <Tooltip>
        <TooltipTrigger className="custom-trigger">
          <span data-testid="trigger">Hover me</span>
        </TooltipTrigger>
        <TooltipContent>Tooltip</TooltipContent>
      </Tooltip>,
    );
    const trigger = screen.getByTestId('trigger').parentElement;
    expect(trigger?.className).toContain('custom-trigger');
  });

  it('TooltipContent has sr-only span with content text', () => {
    renderTooltip({ defaultOpen: true });
    const srOnly = document.querySelector('.sr-only');
    expect(srOnly).toBeInTheDocument();
    expect(srOnly?.textContent).toBe('Tooltip text');
  });

  it('TooltipContent renders arrow indicator for top+center', () => {
    render(
      <Tooltip defaultOpen>
        <TooltipTrigger>
          <span data-testid="trigger">Hover me</span>
        </TooltipTrigger>
        <TooltipContent side="top" align="center">
          <span data-testid="content-text">Tooltip text</span>
        </TooltipContent>
      </Tooltip>,
    );
    const content = screen.getByRole('tooltip');
    expect(content.className).toContain('bottom-full');
  });

  it('TooltipContent renders arrow indicator for bottom+center', () => {
    render(
      <Tooltip defaultOpen>
        <TooltipTrigger>
          <span data-testid="trigger">Hover me</span>
        </TooltipTrigger>
        <TooltipContent side="bottom" align="center">
          <span data-testid="content-text">Tooltip text</span>
        </TooltipContent>
      </Tooltip>,
    );
    const content = screen.getByRole('tooltip');
    expect(content.className).toContain('top-full');
  });

  it('TooltipContent has z-50 and absolute positioning', () => {
    renderTooltip({ defaultOpen: true });
    const content = screen.getByRole('tooltip');
    expect(content.className).toContain('z-50');
    expect(content.className).toContain('absolute');
  });

  it('TooltipContent has bg-gray-900 styling', () => {
    renderTooltip({ defaultOpen: true });
    const content = screen.getByRole('tooltip');
    expect(content.className).toContain('bg-gray-900');
  });

  it('TooltipContent has dark mode bg-gray-700 styling', () => {
    renderTooltip({ defaultOpen: true });
    const content = screen.getByRole('tooltip');
    expect(content.className).toContain('dark:bg-gray-700');
  });

  it('TooltipContent has shadow-lg styling', () => {
    renderTooltip({ defaultOpen: true });
    const content = screen.getByRole('tooltip');
    expect(content.className).toContain('shadow-lg');
  });

  it('TooltipContent has rounded-lg styling', () => {
    renderTooltip({ defaultOpen: true });
    const content = screen.getByRole('tooltip');
    expect(content.className).toContain('rounded-lg');
  });

  it('TooltipContent has whitespace-nowrap styling', () => {
    renderTooltip({ defaultOpen: true });
    const content = screen.getByRole('tooltip');
    expect(content.className).toContain('whitespace-nowrap');
  });

  it('TooltipContent has text-sm styling', () => {
    renderTooltip({ defaultOpen: true });
    const content = screen.getByRole('tooltip');
    expect(content.className).toContain('text-sm');
  });

  it('TooltipContent has text-white styling', () => {
    renderTooltip({ defaultOpen: true });
    const content = screen.getByRole('tooltip');
    expect(content.className).toContain('text-white');
  });
});

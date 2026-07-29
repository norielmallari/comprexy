import { render, screen } from '@testing-library/react';
import { Skeleton } from '@/components/ui/skeleton';

describe('Skeleton', () => {
  it('renders a div with skeleton CSS classes', () => {
    render(<Skeleton data-testid="skeleton" />);
    const skeleton = screen.getByTestId('skeleton');
    expect(skeleton).toBeInTheDocument();
    expect(skeleton.tagName).toBe('DIV');
    expect(skeleton.className).toContain('animate-pulse');
    expect(skeleton.className).toContain('bg-gray-200');
  });

  it('has animation pulse class', () => {
    render(<Skeleton data-testid="skeleton" />);
    const skeleton = screen.getByTestId('skeleton');
    expect(skeleton.className).toContain('animate-pulse');
  });

  it('renders with variant=rectangular (default)', () => {
    render(<Skeleton variant="rectangular" data-testid="skeleton" />);
    const skeleton = screen.getByTestId('skeleton');
    expect(skeleton.className).toContain('rounded');
  });

  it('renders with variant=circular', () => {
    render(<Skeleton variant="circular" data-testid="skeleton" />);
    const skeleton = screen.getByTestId('skeleton');
    expect(skeleton.className).toContain('rounded-full');
  });

  it('renders with variant=text', () => {
    render(<Skeleton variant="text" data-testid="skeleton" />);
    const skeleton = screen.getByTestId('skeleton');
    expect(skeleton.className).toContain('rounded');
    expect(skeleton.className).toContain('h-4');
    expect(skeleton.className).toContain('w-full');
  });

  it('renders with custom className', () => {
    render(<Skeleton className="custom-skeleton" data-testid="skeleton" />);
    const skeleton = screen.getByTestId('skeleton');
    expect(skeleton.className).toContain('custom-skeleton');
  });

  it('forwards ref to the div element', () => {
    const ref = { current: null as HTMLDivElement | null };
    render(<Skeleton ref={ref} data-testid="skeleton" />);
    expect(ref.current).toBeInstanceOf(HTMLDivElement);
  });

  it('passes through additional HTML attributes', () => {
    render(<Skeleton data-testid="skeleton" aria-label="Loading skeleton" />);
    const skeleton = screen.getByTestId('skeleton');
    expect(skeleton).toHaveAttribute('aria-label', 'Loading skeleton');
  });

  it('renders dark mode class', () => {
    render(<Skeleton data-testid="skeleton" />);
    const skeleton = screen.getByTestId('skeleton');
    expect(skeleton.className).toContain('dark:bg-gray-700');
  });

  it('variant classes override correctly with className', () => {
    render(<Skeleton variant="circular" className="extra-class" data-testid="skeleton" />);
    const skeleton = screen.getByTestId('skeleton');
    expect(skeleton.className).toContain('rounded-full');
    expect(skeleton.className).toContain('extra-class');
  });
});

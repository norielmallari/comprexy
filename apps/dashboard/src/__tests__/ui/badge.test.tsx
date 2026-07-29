import { render, screen } from '@testing-library/react';
import { Badge } from '@/components/ui/badge';

describe('Badge', () => {
  it('renders with default props (variant=default)', () => {
    render(<Badge>Default</Badge>);
    const badge = screen.getByText('Default');
    expect(badge).toBeInTheDocument();
    expect(badge.tagName).toBe('SPAN');
    expect(badge.className).toContain('inline-flex');
    expect(badge.className).toContain('items-center');
    expect(badge.className).toContain('px-2');
    expect(badge.className).toContain('py-0.5');
    expect(badge.className).toContain('rounded-full');
    expect(badge.className).toContain('text-xs');
    expect(badge.className).toContain('font-medium');
  });

  it('renders with variant=default', () => {
    render(<Badge variant="default">Default Badge</Badge>);
    const badge = screen.getByText('Default Badge');
    expect(badge.className).toContain('bg-gray-200');
    expect(badge.className).toContain('text-gray-800');
  });

  it('renders with variant=success', () => {
    render(<Badge variant="success">Success</Badge>);
    const badge = screen.getByText('Success');
    expect(badge.className).toContain('bg-green-100');
    expect(badge.className).toContain('text-green-800');
  });

  it('renders with variant=warning', () => {
    render(<Badge variant="warning">Warning</Badge>);
    const badge = screen.getByText('Warning');
    expect(badge.className).toContain('bg-yellow-100');
    expect(badge.className).toContain('text-yellow-800');
  });

  it('renders with variant=error', () => {
    render(<Badge variant="error">Error</Badge>);
    const badge = screen.getByText('Error');
    expect(badge.className).toContain('bg-red-100');
    expect(badge.className).toContain('text-red-800');
  });

  it('renders with variant=info', () => {
    render(<Badge variant="info">Info</Badge>);
    const badge = screen.getByText('Info');
    expect(badge.className).toContain('bg-blue-100');
    expect(badge.className).toContain('text-blue-800');
  });

  it('renders children content', () => {
    render(<Badge>Status: Active</Badge>);
    expect(screen.getByText('Status: Active')).toBeInTheDocument();
  });

  it('renders with custom className', () => {
    render(<Badge className="custom-badge">Custom</Badge>);
    const badge = screen.getByText('Custom');
    expect(badge.className).toContain('custom-badge');
  });

  it('forwards ref to the span element', () => {
    const ref = { current: null as HTMLSpanElement | null };
    render(<Badge ref={ref}>Ref Test</Badge>);
    expect(ref.current).toBeInstanceOf(HTMLSpanElement);
    expect(screen.getByText('Ref Test')).toBe(ref.current);
  });

  it('passes through additional HTML attributes', () => {
    render(<Badge data-testid="status-badge" aria-label="Status Badge">Badge</Badge>);
    const badge = screen.getByTestId('status-badge');
    expect(badge).toHaveAttribute('aria-label', 'Status Badge');
  });
});

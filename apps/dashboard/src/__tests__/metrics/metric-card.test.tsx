import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { MetricCard } from '@/components/metrics/metric-card';

vi.mock('@/components/ui/badge', () => ({
  Badge: ({ children, ...props }: any) => <span {...props}>{children}</span>,
}));

describe('MetricCard', () => {
  it('renders title', () => {
    render(<MetricCard title="Average Compression" value="67.3" unit="%" />);
    expect(screen.getByText('Average Compression')).toBeInTheDocument();
  });

  it('renders value', () => {
    render(<MetricCard title="Average Compression" value="67.3" unit="%" />);
    expect(screen.getByText('67.3')).toBeInTheDocument();
  });

  it('renders unit', () => {
    render(<MetricCard title="Average Compression" value="67.3" unit="%" />);
    expect(screen.getByText('%')).toBeInTheDocument();
  });

  it('renders with variant="default"', () => {
    const { container } = render(
      <MetricCard title="Default" value="100" unit="tokens" variant="default" />,
    );
    const valueSpan = container.querySelector('span.text-3xl');
    expect(valueSpan).toBeInTheDocument();
  });

  it('renders with variant="compact"', () => {
    const { container } = render(
      <MetricCard title="Compact" value="5" unit="triggers" variant="compact" />,
    );
    const valueSpan = container.querySelector('span.text-2xl');
    expect(valueSpan).toBeInTheDocument();
  });

  it('defaults to variant="default"', () => {
    const { container } = render(
      <MetricCard title="Default" value="100" unit="tokens" />,
    );
    const valueSpan = container.querySelector('span.text-3xl');
    expect(valueSpan).toBeInTheDocument();
  });

  it('renders value', () => {
    render(<MetricCard title="Average Compression" value="67.3" unit="%" />);
    expect(screen.getByText('67.3')).toBeInTheDocument();
    expect(screen.getByText('%')).toBeInTheDocument();
  });

  it('renders unit', () => {
    render(<MetricCard title="Average Compression" value="67.3" unit="%" />);
    expect(screen.getByText('%')).toBeInTheDocument();
  });

  it('has correct aria-label matching title', () => {
    render(<MetricCard title="My Metric" value="10" unit="x" />);
    const region = screen.getByRole('region', { name: 'My Metric' });
    expect(region).toBeInTheDocument();
  });

  it('renders zero value correctly', () => {
    render(<MetricCard title="Zero" value="0" unit="" />);
    expect(screen.getByText('0')).toBeInTheDocument();
  });

  it('renders large number values', () => {
    render(<MetricCard title="Big" value="999999" unit="bytes" />);
    expect(screen.getByText('999999')).toBeInTheDocument();
  });
});

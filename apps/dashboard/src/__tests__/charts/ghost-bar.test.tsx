import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { GhostBar } from '@/components/charts/ghost-bar';

// Custom JSX element for the Bar mock in tests
declare module 'react' {
  namespace JSX {
    interface IntrinsicElements {
      'mock-bar': React.HTMLAttributes<HTMLElement> & Record<string, string | number | string[]>;
    }
  }
}

vi.mock('recharts', () => ({
  Bar: vi.fn((props: any) => {
    const attrs: Record<string, string | number | string[]> = {
      'data-testid': 'mock-bar',
    };
    if (props.dataKey) attrs['data-datakey'] = props.dataKey;
    if (props.fill) attrs['data-fill'] = props.fill;
    if (props.opacity !== undefined) attrs['data-opacity'] = String(props.opacity);
    if (props.radius) attrs['data-radius'] = JSON.stringify(props.radius);
    return <mock-bar {...attrs} />;
  }),
}));

describe('GhostBar', () => {
  const mockData = [
    { turnIndex: 1, baseline: 1000 },
    { turnIndex: 2, baseline: 2000 },
    { turnIndex: 3, baseline: 1500 },
  ];

  it('renders a recharts Bar component', () => {
    render(<GhostBar dataKey="baseline" baselineData={mockData} />);

    const mockBar = screen.getByTestId('mock-bar');
    expect(mockBar).toBeInTheDocument();
  });

  it('passes dataKey prop through', () => {
    render(<GhostBar dataKey="baseline" baselineData={mockData} />);

    const mockBar = screen.getByTestId('mock-bar');
    expect(mockBar).toHaveAttribute('data-datakey', 'baseline');
  });

  it('passes fill prop through', () => {
    render(<GhostBar dataKey="baseline" baselineData={mockData} fill="#ff0000" />);

    const mockBar = screen.getByTestId('mock-bar');
    expect(mockBar).toHaveAttribute('data-fill', '#ff0000');
  });

  it('uses default fill color when not provided', () => {
    render(<GhostBar dataKey="baseline" baselineData={mockData} />);

    const mockBar = screen.getByTestId('mock-bar');
    expect(mockBar).toHaveAttribute('data-fill', '#cbd5e0');
  });

  it('applies opacity of 0.4', () => {
    render(<GhostBar dataKey="baseline" baselineData={mockData} />);

    const mockBar = screen.getByTestId('mock-bar');
    expect(mockBar).toHaveAttribute('data-opacity', '0.4');
  });

  it('applies correct border radius', () => {
    render(<GhostBar dataKey="baseline" baselineData={mockData} />);

    const mockBar = screen.getByTestId('mock-bar');
    const radius = JSON.parse(mockBar.getAttribute('data-radius') || '[]');
    expect(radius).toEqual([4, 4, 0, 0]);
  });

  it('passes through custom fill color', () => {
    render(<GhostBar dataKey="baseline" baselineData={mockData} fill="#999999" />);

    const mockBar = screen.getByTestId('mock-bar');
    expect(mockBar).toHaveAttribute('data-fill', '#999999');
  });

  it('renders with different dataKey values', () => {
    render(<GhostBar dataKey="customKey" baselineData={mockData} />);

    const mockBar = screen.getByTestId('mock-bar');
    expect(mockBar).toHaveAttribute('data-datakey', 'customKey');
  });

  it('renders with empty baseline data', () => {
    render(<GhostBar dataKey="baseline" baselineData={[]} />);

    const mockBar = screen.getByTestId('mock-bar');
    expect(mockBar).toBeInTheDocument();
    expect(mockBar).toHaveAttribute('data-datakey', 'baseline');
  });

  it('renders with large baseline data set', () => {
    const largeData = Array.from({ length: 100 }, (_, i) => ({
      turnIndex: i + 1,
      baseline: (i + 1) * 100,
    }));

    render(<GhostBar dataKey="baseline" baselineData={largeData} />);

    const mockBar = screen.getByTestId('mock-bar');
    expect(mockBar).toBeInTheDocument();
    expect(mockBar).toHaveAttribute('data-datakey', 'baseline');
  });
});

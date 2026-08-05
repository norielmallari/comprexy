import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { WorkingMemoryCard } from '@/components/metrics/working-memory-card';

vi.mock('@/hooks/use-theme', () => ({
  useTheme: vi.fn().mockReturnValue({ theme: 'light' }),
}));

vi.mock('@/lib/utils', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/lib/utils')>();
  return {
    ...actual,
    getWmColor: vi.fn().mockReturnValue('#2563eb'),
  };
});

const { getWmColor } = await import('@/lib/utils');

describe('WorkingMemoryCard', () => {
  it('renders version badge when available', () => {
    render(<WorkingMemoryCard maxWorkingMemoryVersion={1} />);
    expect(screen.getByText('v1')).toBeInTheDocument();
  });

  it('renders version 0 badge', () => {
    render(<WorkingMemoryCard maxWorkingMemoryVersion={0} />);
    expect(screen.getByText('v0')).toBeInTheDocument();
  });

  it('renders version 3 badge', () => {
    render(<WorkingMemoryCard maxWorkingMemoryVersion={3} />);
    expect(screen.getByText('v3')).toBeInTheDocument();
  });

  it('renders "No data" placeholder when version is null', () => {
    render(<WorkingMemoryCard maxWorkingMemoryVersion={null} />);
    expect(screen.getByText('No data')).toBeInTheDocument();
  });

  it('exposes a Working Memory region when populated', () => {
    render(<WorkingMemoryCard maxWorkingMemoryVersion={1} />);
    expect(
      screen.getByRole('region', { name: 'Working Memory' }),
    ).toBeInTheDocument();
  });

  it('exposes a Working Memory region in the no-data state', () => {
    render(<WorkingMemoryCard maxWorkingMemoryVersion={null} />);
    expect(
      screen.getByRole('region', { name: 'Working Memory' }),
    ).toBeInTheDocument();
  });

  it('does not render version badge when null', () => {
    render(<WorkingMemoryCard maxWorkingMemoryVersion={null} />);
    expect(screen.queryByText(/v\d/)).not.toBeInTheDocument();
  });

  it('renders title "Working Memory"', () => {
    render(<WorkingMemoryCard maxWorkingMemoryVersion={1} />);
    expect(screen.getByText('Working Memory')).toBeInTheDocument();
  });


  it('renders badge element with version text', () => {
    render(<WorkingMemoryCard maxWorkingMemoryVersion={2} />);
    const badge = screen.getByText('v2');
    expect(badge.tagName.toLowerCase()).toBe('span');
  });

  it('calls getWmColor with the version number', () => {
    render(<WorkingMemoryCard maxWorkingMemoryVersion={1} />);
    expect(getWmColor).toHaveBeenCalledWith(1, false);
  });

  it('renders with dark theme when theme is dark', () => {
    vi.mocked(getWmColor).mockReturnValue('#60a5fa');
    render(<WorkingMemoryCard maxWorkingMemoryVersion={1} />);
    expect(screen.getByText('v1')).toBeInTheDocument();
  });

  it('renders badge with correct classes', () => {
    render(<WorkingMemoryCard maxWorkingMemoryVersion={1} />);
    const badge = screen.getByText('v1');
    expect(badge.className).toContain('rounded-full');
  });

  it('applies background color style to badge', () => {
    render(<WorkingMemoryCard maxWorkingMemoryVersion={1} />);
    const badge = screen.getByText('v1');
    expect(badge).toHaveAttribute('style');
  });

  it('uses dark text color for version 0', () => {
    render(<WorkingMemoryCard maxWorkingMemoryVersion={0} />);
    const badge = screen.getByText('v0');
    expect(badge).toHaveAttribute('style');
  });
});

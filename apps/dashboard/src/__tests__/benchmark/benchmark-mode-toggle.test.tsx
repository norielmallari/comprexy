import { render, screen, fireEvent } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import {
  BenchmarkModeToggle,
  type BenchmarkMode,
} from '@/components/benchmark/benchmark-mode-toggle';

describe('BenchmarkModeToggle', () => {
  it('renders Telemetry and Comparison tabs', () => {
    const onChange = vi.fn();
    render(<BenchmarkModeToggle mode="telemetry" onChange={onChange} />);

    expect(screen.getByTestId('benchmark-mode-toggle')).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Telemetry' })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Comparison' })).toBeInTheDocument();
  });

  it('marks Telemetry as selected when mode is telemetry', () => {
    render(<BenchmarkModeToggle mode="telemetry" onChange={vi.fn()} />);

    expect(screen.getByRole('tab', { name: 'Telemetry' })).toHaveAttribute(
      'aria-selected',
      'true',
    );
    expect(screen.getByRole('tab', { name: 'Comparison' })).toHaveAttribute(
      'aria-selected',
      'false',
    );
  });

  it('marks Comparison as selected when mode is comparison', () => {
    render(<BenchmarkModeToggle mode="comparison" onChange={vi.fn()} />);

    expect(screen.getByRole('tab', { name: 'Comparison' })).toHaveAttribute(
      'aria-selected',
      'true',
    );
    expect(screen.getByRole('tab', { name: 'Telemetry' })).toHaveAttribute(
      'aria-selected',
      'false',
    );
  });

  it('calls onChange when switching modes', () => {
    const onChange = vi.fn();
    render(<BenchmarkModeToggle mode="telemetry" onChange={onChange} />);

    fireEvent.click(screen.getByRole('tab', { name: 'Comparison' }));
    expect(onChange).toHaveBeenCalledWith('comparison' satisfies BenchmarkMode);

    fireEvent.click(screen.getByRole('tab', { name: 'Telemetry' }));
    expect(onChange).toHaveBeenCalledWith('telemetry' satisfies BenchmarkMode);
  });
});

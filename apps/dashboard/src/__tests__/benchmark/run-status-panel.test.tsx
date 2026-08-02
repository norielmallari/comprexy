import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { RunStatusPanel } from '@/components/benchmark/run-status-panel';
import type { BenchmarkRunSummaryDto } from '@/types/api';

function makeRun(phase: string): BenchmarkRunSummaryDto {
  return {
    runId: 'fixture-run-001',
    phase,
    runPhase: null,
    startedAt: '2026-01-15T10:00:00.000Z',
    updatedAt: '2026-01-15T10:30:00.000Z',
    lastError: phase === 'failed' ? 'Harness exited with code 1' : null,
    arm: 'comprexy',
    conversationName: 'fixture-scenario-a',
    promptsCompleted: 5,
    promptCount: 5,
    conversationNames: ['fixture-scenario-a'],
    costRates: null,
  };
}

describe('RunStatusPanel', () => {
  it('maps completed to success label', () => {
    render(<RunStatusPanel run={makeRun('completed')} />);
    expect(screen.getByTestId('run-status-badge')).toHaveTextContent(
      'Run completed successfully',
    );
  });

  it('maps failed to run failed label', () => {
    render(<RunStatusPanel run={makeRun('failed')} />);
    expect(screen.getByTestId('run-status-badge')).toHaveTextContent('Run failed');
    expect(screen.getByTestId('run-last-error')).toHaveTextContent(
      'Harness exited with code 1',
    );
  });

  it('maps cancelled to cancelled label', () => {
    render(<RunStatusPanel run={makeRun('cancelled')} />);
    expect(screen.getByTestId('run-status-badge')).toHaveTextContent('Cancelled');
  });

  it('maps completed_with_report_error to report failed label', () => {
    render(<RunStatusPanel run={makeRun('completed_with_report_error')} />);
    expect(screen.getByTestId('run-status-badge')).toHaveTextContent(
      'Run finished; report failed',
    );
  });

  it('shows loading state when loading without run data', () => {
    render(<RunStatusPanel run={undefined} isLoading />);
    expect(screen.getByTestId('run-status-panel')).toHaveTextContent(
      'Loading run status…',
    );
  });

  it('renders nothing when no run and not loading', () => {
    const { container } = render(<RunStatusPanel run={undefined} />);
    expect(container).toBeEmptyDOMElement();
  });
});

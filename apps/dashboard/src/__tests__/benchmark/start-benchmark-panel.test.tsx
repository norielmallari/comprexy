import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MockedFunction, describe, expect, it, vi, beforeEach } from 'vitest';

import { StartBenchmarkPanel } from '@/components/benchmark/start-benchmark-panel';
import {
  getRunPresentation,
  useBenchmarkRun,
  useBenchmarkScenarios,
  useCancelBenchmarkRun,
  useReportBenchmarkRun,
  useStartBenchmarkRun,
} from '@/lib/api/benchmarks';
import { DEFAULT_COST_RATES } from '@/lib/benchmark-cost';
import type { BenchmarkScenarioDto } from '@/types/api';

vi.mock('@/lib/api/benchmarks', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/lib/api/benchmarks')>();
  return {
    ...actual,
    useBenchmarkScenarios: vi.fn(),
    useStartBenchmarkRun: vi.fn(),
    useCancelBenchmarkRun: vi.fn(),
    useReportBenchmarkRun: vi.fn(),
    useBenchmarkRun: vi.fn(),
    getRunPresentation: vi.fn(),
  };
});

const mockUseBenchmarkScenarios = useBenchmarkScenarios as MockedFunction<
  typeof useBenchmarkScenarios
>;
const mockUseStartBenchmarkRun = useStartBenchmarkRun as MockedFunction<
  typeof useStartBenchmarkRun
>;
const mockUseCancelBenchmarkRun = useCancelBenchmarkRun as MockedFunction<
  typeof useCancelBenchmarkRun
>;
const mockUseReportBenchmarkRun = useReportBenchmarkRun as MockedFunction<
  typeof useReportBenchmarkRun
>;
const mockUseBenchmarkRun = useBenchmarkRun as MockedFunction<typeof useBenchmarkRun>;
const mockGetRunPresentation = getRunPresentation as MockedFunction<
  typeof getRunPresentation
>;

const BASELINE_ID = '00000000-0000-4000-8000-000000000001';
const COMPARE_ID = '00000000-0000-4000-8000-000000000002';

const scenarios: BenchmarkScenarioDto[] = [
  { name: 'fixture-scenario-a', promptCount: 5 },
];

const defaultProps = {
  rates: DEFAULT_COST_RATES,
  modelKind: 'local' as const,
  activeRunId: null,
  onRunStarted: vi.fn(),
  onRunCompleted: vi.fn(),
  onAutoFillIds: vi.fn(),
};

function setupMutations() {
  const mutateAsync = vi.fn();
  mockUseStartBenchmarkRun.mockReturnValue({
    mutateAsync,
    isPending: false,
  } as unknown as ReturnType<typeof useStartBenchmarkRun>);
  mockUseCancelBenchmarkRun.mockReturnValue({
    mutateAsync: vi.fn(),
    isPending: false,
  } as unknown as ReturnType<typeof useCancelBenchmarkRun>);
  mockUseReportBenchmarkRun.mockReturnValue({
    mutateAsync: vi.fn(),
    isPending: false,
  } as unknown as ReturnType<typeof useReportBenchmarkRun>);
  return { mutateAsync };
}

beforeEach(() => {
  vi.clearAllMocks();
  mockUseBenchmarkScenarios.mockReturnValue({
    data: scenarios,
    isLoading: false,
  } as unknown as ReturnType<typeof useBenchmarkScenarios>);
  mockUseBenchmarkRun.mockReturnValue({
    data: undefined,
  } as unknown as ReturnType<typeof useBenchmarkRun>);
  setupMutations();
});

describe('StartBenchmarkPanel', () => {
  it('disables Start until acknowledgement and scenario are selected', () => {
    render(<StartBenchmarkPanel {...defaultProps} />);

    const startButton = screen.getByTestId('start-benchmark-button');
    expect(startButton).toBeDisabled();

    fireEvent.click(screen.getByTestId('benchmark-ack-checkbox'));
    expect(startButton).toBeDisabled();

    fireEvent.click(screen.getByLabelText(/fixture-scenario-a/));
    expect(startButton).not.toBeDisabled();
  });

  it('disables Start while an active run is in progress', () => {
    mockUseBenchmarkRun.mockReturnValue({
      data: {
        runId: 'active-run-001',
        phase: 'running',
        runPhase: 'running',
        startedAt: null,
        updatedAt: null,
        lastError: null,
        arm: 'comprexy',
        conversationName: null,
        promptsCompleted: 1,
        promptCount: 5,
        conversationNames: [],
        costRates: null,
      },
    } as unknown as ReturnType<typeof useBenchmarkRun>);

    render(<StartBenchmarkPanel {...defaultProps} activeRunId="active-run-001" />);

    fireEvent.click(screen.getByTestId('benchmark-ack-checkbox'));
    fireEvent.click(screen.getByLabelText(/fixture-scenario-a/));

    expect(screen.getByTestId('start-benchmark-button')).toBeDisabled();
    expect(screen.getByTestId('cancel-benchmark-button')).toBeInTheDocument();
  });

  it('shows conflict message on 409 active-run lock', async () => {
    const { mutateAsync } = setupMutations();
    mutateAsync.mockRejectedValue({
      statusCode: 409,
      activeRunId: 'locked-run-999',
      message: 'Another run is active',
    });

    const onRunStarted = vi.fn();
    render(<StartBenchmarkPanel {...defaultProps} onRunStarted={onRunStarted} />);

    fireEvent.click(screen.getByTestId('benchmark-ack-checkbox'));
    fireEvent.click(screen.getByLabelText(/fixture-scenario-a/));
    fireEvent.click(screen.getByTestId('start-benchmark-button'));

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent(/Another run is active/);
      expect(screen.getByRole('alert')).toHaveTextContent(/locked-run-999/);
    });
    expect(onRunStarted).not.toHaveBeenCalled();
  });

  it('shows read-only timeout defaults', () => {
    render(<StartBenchmarkPanel {...defaultProps} />);

    const timeouts = screen.getByTestId('benchmark-timeout-defaults');
    expect(timeouts).toHaveTextContent('300s');
    expect(timeouts).toHaveTextContent('7200s');
    expect(timeouts).toHaveTextContent('2 hr');
    expect(timeouts).toHaveTextContent('Server defaults');
    expect(screen.queryByRole('spinbutton')).not.toBeInTheDocument();
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
  });

  it('shows smoke timeout defaults when only smoke scenarios are selected', () => {
    mockUseBenchmarkScenarios.mockReturnValue({
      data: [
        { name: 'fixture-scenario-a', promptCount: 5 },
        {
          name: 'smoke-large-blob',
          promptCount: 10,
          description: 'fixture smoke description',
          isSmoke: true,
        },
      ],
      isLoading: false,
    } as unknown as ReturnType<typeof useBenchmarkScenarios>);

    render(<StartBenchmarkPanel {...defaultProps} />);

    fireEvent.click(screen.getByLabelText(/smoke-large-blob/));

    const timeouts = screen.getByTestId('benchmark-timeout-defaults');
    expect(timeouts).toHaveTextContent('1200s');
    expect(timeouts).toHaveTextContent('20 min');
    expect(timeouts).toHaveTextContent('smoke run');
    expect(timeouts).toHaveTextContent('baseline survival early-stop');
    expect(screen.getByText('Smoke')).toBeInTheDocument();
    expect(screen.getByText('fixture smoke description')).toBeInTheDocument();
  });

  it('calls onAutoFillIds when run reaches completed terminal phase', async () => {
    mockGetRunPresentation.mockResolvedValue({
      totals: {
        baseline: {
          conversationId: BASELINE_ID,
          turnCount: 3,
          inputTokens: 12_000,
          outputTokens: 3_000,
          overheadTokens: 400,
          totalSentTokens: 15_400,
          wallClockMs: 120_000,
          totalProxyDurationMs: 95_000,
          totalUpstreamDurationMs: 80_000,
          totalPrepareDurationMs: 15_000,
        },
        compare: {
          conversationId: COMPARE_ID,
          turnCount: 4,
          inputTokens: 11_000,
          outputTokens: 3_200,
          overheadTokens: 350,
          totalSentTokens: 14_550,
          wallClockMs: 135_000,
          totalProxyDurationMs: 100_000,
          totalUpstreamDurationMs: 85_000,
          totalPrepareDurationMs: 15_000,
        },
        input: { baseline: 12_000, compare: 11_000, delta: -1000, deltaPercent: -8.33 },
        output: { baseline: 3000, compare: 3200, delta: 200, deltaPercent: 6.67 },
        overhead: { baseline: 400, compare: 350, delta: -50, deltaPercent: -12.5 },
        turnCount: { baseline: 3, compare: 4, delta: 1, deltaPercent: 33.33 },
        wallClockMs: {
          baseline: 120_000,
          compare: 135_000,
          delta: 15_000,
          deltaPercent: 12.5,
        },
        proxyDurationMs: {
          baseline: 95_000,
          compare: 100_000,
          delta: 5000,
          deltaPercent: 5.26,
        },
        caveats: [],
      },
      cost: null,
      baselineConversationId: BASELINE_ID,
      compareConversationId: COMPARE_ID,
      runId: 'fixture-run-001',
      turnSeriesPaths: ['/tmp/fixture-bench/fixture-run-001/turns-baseline.json'],
    });

    mockUseBenchmarkRun.mockReturnValue({
      data: {
        runId: 'fixture-run-001',
        phase: 'completed',
        runPhase: 'run_finished',
        startedAt: null,
        updatedAt: null,
        lastError: null,
        arm: null,
        conversationName: null,
        promptsCompleted: null,
        promptCount: null,
        conversationNames: [],
        costRates: null,
      },
    } as unknown as ReturnType<typeof useBenchmarkRun>);

    const onAutoFillIds = vi.fn();
    render(
      <StartBenchmarkPanel
        {...defaultProps}
        activeRunId="fixture-run-001"
        onAutoFillIds={onAutoFillIds}
      />,
    );

    await waitFor(() => {
      expect(mockGetRunPresentation).toHaveBeenCalledWith('fixture-run-001');
      expect(onAutoFillIds).toHaveBeenCalledWith(BASELINE_ID, COMPARE_ID);
    });
  });

  it('shows report-failed terminal copy for completed_with_report_error', () => {
    mockUseBenchmarkRun.mockReturnValue({
      data: {
        runId: 'fixture-run-001',
        phase: 'completed_with_report_error',
        runPhase: 'run_finished',
        startedAt: null,
        updatedAt: null,
        lastError: 'Report agent timed out',
        arm: null,
        conversationName: null,
        promptsCompleted: null,
        promptCount: null,
        conversationNames: [],
        costRates: null,
      },
    } as unknown as ReturnType<typeof useBenchmarkRun>);

    render(<StartBenchmarkPanel {...defaultProps} activeRunId="fixture-run-001" />);

    expect(screen.getByTestId('run-phase-label')).toHaveTextContent(
      'Run finished; report failed',
    );
  });
});

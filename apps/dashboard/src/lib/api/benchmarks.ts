/**
 * Benchmark control-api client and React Query hooks.
 */

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { apiFetch, apiGet, apiPost } from '@/lib/api/client';
import type {
  ApiError,
  BenchmarkComparisonPresentationResponse,
  BenchmarkConflictError,
  BenchmarkCostRates,
  BenchmarkModelKind,
  BenchmarkRunSummaryDto,
  BenchmarkScenarioDto,
  BenchmarkStartRunRequest,
  BenchmarkStartRunResponse,
  BenchmarkTelemetryPresentationResponse,
} from '@/types/api';

const BENCHMARK_BASE = '/v1/comprexy/benchmarks';

export const benchmarkKeys = {
  all: ['benchmarks'] as const,
  scenarios: () => [...benchmarkKeys.all, 'scenarios'] as const,
  runs: () => [...benchmarkKeys.all, 'runs'] as const,
  run: (runId: string) => [...benchmarkKeys.runs(), runId] as const,
  runPresentation: (runId: string) =>
    [...benchmarkKeys.run(runId), 'presentation'] as const,
  telemetryPresentation: (
    conversationId: string,
    modelKind: BenchmarkModelKind,
    ratesKey: string,
  ) =>
    [...benchmarkKeys.all, 'telemetry', conversationId, modelKind, ratesKey] as const,
  comparisonPresentation: (
    baselineId: string,
    compareId: string,
    modelKind: BenchmarkModelKind,
    ratesKey: string,
  ) =>
    [...benchmarkKeys.all, 'compare', baselineId, compareId, modelKind, ratesKey] as const,
};

/** Stable React Query key segment so presentation refetches when rates change. */
export function benchmarkRatesKeyPart(
  rates: BenchmarkCostRates | undefined,
): string {
  if (!rates) {
    return 'none';
  }
  return JSON.stringify([
    rates.inputUsdPer1M,
    rates.outputUsdPer1M,
    rates.compressionInputUsdPer1M,
    rates.compressionOutputUsdPer1M,
    rates.developerUsdPerHour,
    rates.machineUsdPerHour,
    rates.modelKind,
  ]);
}

export const TERMINAL_RUN_PHASES = new Set([
  'completed',
  'failed',
  'cancelled',
  'completed_with_report_error',
]);

export function isActiveRunPhase(phase: string): boolean {
  return !TERMINAL_RUN_PHASES.has(phase);
}

export function getRunStatusLabel(phase: string): string {
  switch (phase) {
    case 'completed':
      return 'Run completed successfully';
    case 'failed':
      return 'Run failed';
    case 'cancelled':
      return 'Cancelled';
    case 'completed_with_report_error':
      return 'Run finished; report failed';
    case 'queued':
      return 'Queued';
    case 'starting':
      return 'Starting';
    case 'running':
      return 'Running';
    case 'reporting':
      return 'Generating report';
    default:
      return phase;
  }
}

export function listScenarios(): Promise<BenchmarkScenarioDto[]> {
  return apiGet<BenchmarkScenarioDto[]>(`${BENCHMARK_BASE}/scenarios`);
}

export function listRuns(): Promise<BenchmarkRunSummaryDto[]> {
  return apiGet<BenchmarkRunSummaryDto[]>(`${BENCHMARK_BASE}/runs`);
}

export function getRun(runId: string): Promise<BenchmarkRunSummaryDto> {
  return apiGet<BenchmarkRunSummaryDto>(`${BENCHMARK_BASE}/runs/${runId}`);
}

export async function startRun(
  request: BenchmarkStartRunRequest,
): Promise<BenchmarkStartRunResponse> {
  return apiPost<BenchmarkStartRunResponse>(`${BENCHMARK_BASE}/runs`, request);
}

export async function cancelRun(runId: string): Promise<void> {
  await apiFetch<void>(`${BENCHMARK_BASE}/runs/${runId}/cancel`, {
    method: 'POST',
  });
}

export async function reportRun(
  runId: string,
  rates?: BenchmarkCostRates,
): Promise<{ runId: string; exitCode: number }> {
  return apiPost<{ runId: string; exitCode: number }>(
    `${BENCHMARK_BASE}/runs/${runId}/report`,
    rates ? { rates } : {},
  );
}

export function getRunPresentation(
  runId: string,
): Promise<BenchmarkComparisonPresentationResponse> {
  return apiGet<BenchmarkComparisonPresentationResponse>(
    `${BENCHMARK_BASE}/runs/${runId}/presentation`,
  );
}

export function fetchTelemetryPresentation(
  conversationId: string,
  rates: BenchmarkCostRates | undefined,
  modelKind: BenchmarkModelKind,
): Promise<BenchmarkTelemetryPresentationResponse> {
  return apiPost<BenchmarkTelemetryPresentationResponse>(
    `${BENCHMARK_BASE}/presentation/telemetry`,
    { conversationId, rates, modelKind },
  );
}

export function fetchComparisonPresentation(
  baselineConversationId: string,
  compareConversationId: string,
  rates: BenchmarkCostRates | undefined,
  modelKind: BenchmarkModelKind,
): Promise<BenchmarkComparisonPresentationResponse> {
  return apiPost<BenchmarkComparisonPresentationResponse>(
    `${BENCHMARK_BASE}/presentation/compare`,
    { baselineConversationId, compareConversationId, rates, modelKind },
  );
}

export function isConflictError(error: unknown): error is ApiError & BenchmarkConflictError {
  if (!error || typeof error !== 'object') {
    return false;
  }
  const e = error as ApiError & Partial<BenchmarkConflictError>;
  return e.statusCode === 409 && typeof e.activeRunId === 'string';
}

// ---------------------------------------------------------------------------
// React Query hooks
// ---------------------------------------------------------------------------

export function useBenchmarkScenarios() {
  return useQuery({
    queryKey: benchmarkKeys.scenarios(),
    queryFn: listScenarios,
    staleTime: 10 * 60 * 1000,
  });
}

export function useBenchmarkRuns() {
  return useQuery({
    queryKey: benchmarkKeys.runs(),
    queryFn: listRuns,
    staleTime: 30 * 1000,
  });
}

export function useBenchmarkRun(runId: string | null, pollActive = false) {
  return useQuery({
    queryKey: benchmarkKeys.run(runId ?? ''),
    queryFn: () => getRun(runId!),
    enabled: Boolean(runId),
    refetchInterval: (query) => {
      if (!pollActive) {
        return false;
      }
      const phase = query.state.data?.phase;
      if (!phase || !isActiveRunPhase(phase)) {
        return false;
      }
      return 3000;
    },
  });
}

export function useRunPresentation(runId: string | null, enabled = false) {
  return useQuery({
    queryKey: benchmarkKeys.runPresentation(runId ?? ''),
    queryFn: () => getRunPresentation(runId!),
    enabled: Boolean(runId) && enabled,
  });
}

export function useTelemetryPresentation(
  conversationId: string | null,
  rates: BenchmarkCostRates | undefined,
  modelKind: BenchmarkModelKind,
) {
  return useQuery({
    queryKey: benchmarkKeys.telemetryPresentation(
      conversationId ?? '',
      modelKind,
      benchmarkRatesKeyPart(rates),
    ),
    queryFn: () =>
      fetchTelemetryPresentation(conversationId!, rates, modelKind),
    enabled: Boolean(conversationId),
    staleTime: 60 * 1000,
  });
}

export function useComparisonPresentation(
  baselineId: string | null,
  compareId: string | null,
  rates: BenchmarkCostRates | undefined,
  modelKind: BenchmarkModelKind,
) {
  return useQuery({
    queryKey: benchmarkKeys.comparisonPresentation(
      baselineId ?? '',
      compareId ?? '',
      modelKind,
      benchmarkRatesKeyPart(rates),
    ),
    queryFn: () =>
      fetchComparisonPresentation(baselineId!, compareId!, rates, modelKind),
    enabled: Boolean(baselineId) && Boolean(compareId),
    staleTime: 60 * 1000,
  });
}

export function useStartBenchmarkRun() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: startRun,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: benchmarkKeys.runs() });
    },
  });
}

export function useCancelBenchmarkRun() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: cancelRun,
    onSuccess: (_data, runId) => {
      queryClient.invalidateQueries({ queryKey: benchmarkKeys.run(runId) });
      queryClient.invalidateQueries({ queryKey: benchmarkKeys.runs() });
    },
  });
}

export function useReportBenchmarkRun() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ runId, rates }: { runId: string; rates?: BenchmarkCostRates }) =>
      reportRun(runId, rates),
    onSuccess: (_data, { runId }) => {
      queryClient.invalidateQueries({ queryKey: benchmarkKeys.run(runId) });
      queryClient.invalidateQueries({ queryKey: benchmarkKeys.runPresentation(runId) });
    },
  });
}

/**
 * Start / Cancel / Report controls for benchmark runs.
 */

'use client';

import { useEffect, useState } from 'react';

import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import {
  getRunPresentation,
  getRunStatusLabel,
  isActiveRunPhase,
  isConflictError,
  TERMINAL_RUN_PHASES,
  useBenchmarkRun,
  useBenchmarkScenarios,
  useCancelBenchmarkRun,
  useReportBenchmarkRun,
  useStartBenchmarkRun,
} from '@/lib/api/benchmarks';
import { BENCHMARK_TIMEOUT_DEFAULTS, buildCostRates } from '@/lib/benchmark-cost';
import type { BenchmarkCostRates, BenchmarkModelKind } from '@/types/api';

interface StartBenchmarkPanelProps {
  rates: BenchmarkCostRates;
  modelKind: BenchmarkModelKind;
  activeRunId: string | null;
  onRunStarted: (runId: string) => void;
  onRunCompleted: (runId: string) => void;
  onAutoFillIds: (baselineId: string, compareId: string) => void;
}

export function StartBenchmarkPanel({
  rates,
  modelKind,
  activeRunId,
  onRunStarted,
  onRunCompleted,
  onAutoFillIds,
}: StartBenchmarkPanelProps) {
  const { data: scenarios, isLoading: scenariosLoading } = useBenchmarkScenarios();
  const [selectedScenarios, setSelectedScenarios] = useState<string[]>([]);
  const [acknowledged, setAcknowledged] = useState(false);
  const [conflictMessage, setConflictMessage] = useState<string | null>(null);
  const [localRunId, setLocalRunId] = useState<string | null>(activeRunId);

  const startMutation = useStartBenchmarkRun();
  const cancelMutation = useCancelBenchmarkRun();
  const reportMutation = useReportBenchmarkRun();

  const effectiveRunId = localRunId ?? activeRunId;
  const { data: runStatus } = useBenchmarkRun(
    effectiveRunId,
    Boolean(effectiveRunId),
  );

  useEffect(() => {
    if (activeRunId) {
      setLocalRunId(activeRunId);
    }
  }, [activeRunId]);

  useEffect(() => {
    if (!runStatus) {
      return;
    }
    if (TERMINAL_RUN_PHASES.has(runStatus.phase)) {
      onRunCompleted(runStatus.runId);
      if (
        runStatus.phase === 'completed' ||
        runStatus.phase === 'completed_with_report_error'
      ) {
        void fetchPresentationAndAutoFill(runStatus.runId);
      }
    }
  }, [runStatus?.phase, runStatus?.runId]);

  async function fetchPresentationAndAutoFill(runId: string) {
    try {
      const presentation = await getRunPresentation(runId);
      if (presentation.baselineConversationId && presentation.compareConversationId) {
        onAutoFillIds(
          presentation.baselineConversationId,
          presentation.compareConversationId,
        );
      }
    } catch {
      // Presentation may not be ready yet; operator can retry Report.
    }
  }

  const toggleScenario = (name: string) => {
    setSelectedScenarios((prev) =>
      prev.includes(name) ? prev.filter((n) => n !== name) : [...prev, name],
    );
  };

  const handleStart = async () => {
    setConflictMessage(null);
    try {
      const response = await startMutation.mutateAsync({
        conversations: selectedScenarios,
        rates: buildCostRates(rates, modelKind),
        modelKind,
      });
      setLocalRunId(response.runId);
      onRunStarted(response.runId);
    } catch (error) {
      if (isConflictError(error)) {
        setConflictMessage(
          `Another run is active (${error.activeRunId}). Cancel it or wait for completion.`,
        );
        setLocalRunId(error.activeRunId);
      }
    }
  };

  const handleCancel = async () => {
    if (!effectiveRunId) {
      return;
    }
    await cancelMutation.mutateAsync(effectiveRunId);
  };

  const handleReport = async () => {
    if (!effectiveRunId) {
      return;
    }
    await reportMutation.mutateAsync({
      runId: effectiveRunId,
      rates: buildCostRates(rates, modelKind),
    });
    await fetchPresentationAndAutoFill(effectiveRunId);
  };

  const isRunning = runStatus ? isActiveRunPhase(runStatus.phase) : false;
  const selectedScenarioDetails = (scenarios ?? []).filter((scenario) =>
    selectedScenarios.includes(scenario.name),
  );
  const smokeOnlySelection =
    selectedScenarioDetails.length > 0 &&
    selectedScenarioDetails.every((scenario) => scenario.isSmoke);
  const conversationTimeoutSeconds = smokeOnlySelection
    ? BENCHMARK_TIMEOUT_DEFAULTS.smokeConversationTimeoutSeconds
    : BENCHMARK_TIMEOUT_DEFAULTS.conversationTimeoutSeconds;
  const conversationTimeoutLabel =
    conversationTimeoutSeconds >= 3600
      ? `${Math.round(conversationTimeoutSeconds / 3600)} hr`
      : `${Math.round(conversationTimeoutSeconds / 60)} min`;
  const canStart =
    acknowledged &&
    selectedScenarios.length > 0 &&
    !isRunning &&
    !startMutation.isPending;

  return (
    <section
      className="rounded-lg border bg-card p-4"
      aria-label="Start benchmark"
      data-testid="start-benchmark-panel"
    >
      <h3 className="text-base font-semibold">Start Benchmark</h3>
      <p className="mt-1 text-xs text-slate-500">
        Spawns a dual-arm harness run under a single active-run lock. Artifacts land in{' '}
        <code className="text-xs">reports/bench/</code>.
      </p>

      <div className="mt-3">
        <p className="text-sm font-medium text-slate-600 dark:text-slate-300">Scenarios</p>
        {scenariosLoading ? (
          <p className="text-sm text-slate-500">Loading scenarios…</p>
        ) : (
          <div className="mt-2 max-h-40 space-y-2 overflow-y-auto">
            {(scenarios ?? []).map((scenario) => (
              <label
                key={scenario.name}
                className="flex cursor-pointer gap-2 text-sm"
              >
                <input
                  type="checkbox"
                  className="mt-0.5"
                  checked={selectedScenarios.includes(scenario.name)}
                  onChange={() => toggleScenario(scenario.name)}
                  disabled={isRunning}
                />
                <span className="min-w-0">
                  <span className="flex flex-wrap items-center gap-2">
                    <span>{scenario.name}</span>
                    {scenario.isSmoke && <Badge variant="info">Smoke</Badge>}
                    <span className="text-slate-500">({scenario.promptCount} prompts)</span>
                  </span>
                  {scenario.description && (
                    <span className="mt-0.5 block text-xs text-slate-500">
                      {scenario.description}
                    </span>
                  )}
                </span>
              </label>
            ))}
            {(scenarios ?? []).length === 0 && (
              <p className="text-sm text-slate-500">No scenarios found.</p>
            )}
          </div>
        )}
      </div>

      <div className="mt-3" data-testid="benchmark-timeout-defaults">
        <p className="text-sm font-medium text-slate-600 dark:text-slate-300">Timeouts</p>
        <p className="mt-1 text-xs text-slate-500">
          Server defaults (not configurable from dashboard): per-completion{' '}
          {BENCHMARK_TIMEOUT_DEFAULTS.completionTimeoutSeconds}s, per-conversation{' '}
          {conversationTimeoutSeconds}s
          {smokeOnlySelection ? ' (smoke run)' : ''} ({conversationTimeoutLabel}). Set via{' '}
          <code className="text-xs">BenchOrchestration</code> in control-api appsettings.
          {smokeOnlySelection && (
            <>
              {' '}
              Smoke runs also disable baseline survival early-stop.
            </>
          )}
        </p>
      </div>

      <label className="mt-4 flex items-start gap-2 text-sm">
        <input
          type="checkbox"
          checked={acknowledged}
          onChange={(e) => setAcknowledged(e.target.checked)}
          data-testid="benchmark-ack-checkbox"
        />
        <span>
          I understand rates are assumptions, report agent may incur provider cost, and only one
          dashboard run can be active at a time.
        </span>
      </label>

      {conflictMessage && (
        <p className="mt-3 text-sm text-amber-700 dark:text-amber-400" role="alert">
          {conflictMessage}
        </p>
      )}

      {runStatus && (
        <p className="mt-3 text-sm" data-testid="run-phase-label">
          Status: {getRunStatusLabel(runStatus.phase)}
        </p>
      )}

      <div className="mt-4 flex flex-wrap gap-2">
        <Button
          type="button"
          onClick={handleStart}
          disabled={!canStart}
          data-testid="start-benchmark-button"
        >
          {startMutation.isPending ? 'Starting…' : 'Start'}
        </Button>
        {isRunning && effectiveRunId && (
          <Button
            type="button"
            variant="secondary"
            onClick={handleCancel}
            disabled={cancelMutation.isPending}
            data-testid="cancel-benchmark-button"
          >
            Cancel
          </Button>
        )}
        {runStatus && TERMINAL_RUN_PHASES.has(runStatus.phase) && effectiveRunId && (
          <Button
            type="button"
            variant="secondary"
            onClick={handleReport}
            disabled={reportMutation.isPending}
            data-testid="report-benchmark-button"
          >
            {reportMutation.isPending ? 'Reporting…' : 'Re-run Report'}
          </Button>
        )}
      </div>
    </section>
  );
}

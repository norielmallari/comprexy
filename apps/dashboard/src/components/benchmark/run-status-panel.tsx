/**
 * Run status display with terminal copy mapping and poll support.
 */

'use client';

import { Badge } from '@/components/ui/badge';
import { getRunStatusLabel, isActiveRunPhase } from '@/lib/api/benchmarks';
import type { BenchmarkRunSummaryDto } from '@/types/api';

interface RunStatusPanelProps {
  run: BenchmarkRunSummaryDto | undefined;
  isLoading?: boolean;
}

function phaseVariant(phase: string): 'default' | 'success' | 'warning' | 'error' | 'info' {
  switch (phase) {
    case 'completed':
      return 'success';
    case 'failed':
      return 'error';
    case 'cancelled':
      return 'warning';
    case 'completed_with_report_error':
      return 'warning';
    case 'running':
    case 'reporting':
      return 'info';
    default:
      return 'default';
  }
}

export function RunStatusPanel({ run, isLoading }: RunStatusPanelProps) {
  if (!run && !isLoading) {
    return null;
  }

  if (isLoading && !run) {
    return (
      <div className="rounded-lg border bg-card p-4" data-testid="run-status-panel">
        <p className="text-sm text-slate-500">Loading run status…</p>
      </div>
    );
  }

  if (!run) {
    return null;
  }

  const label = getRunStatusLabel(run.phase);
  const active = isActiveRunPhase(run.phase);

  return (
    <div
      className="rounded-lg border bg-card p-4"
      role="status"
      aria-live="polite"
      data-testid="run-status-panel"
    >
      <div className="flex flex-wrap items-center gap-2">
        <span className="text-sm font-medium">Run {run.runId}</span>
        <Badge variant={phaseVariant(run.phase)} data-testid="run-status-badge">
          {label}
        </Badge>
        {active && (
          <span className="text-xs text-slate-500 animate-pulse">Polling…</span>
        )}
      </div>

      {run.arm && (
        <p className="mt-2 text-sm text-slate-600 dark:text-slate-300">
          Arm: {run.arm}
          {run.conversationName && ` · ${run.conversationName}`}
          {run.promptsCompleted !== null && run.promptCount !== null && (
            <span>
              {' '}
              · Prompts {run.promptsCompleted}/{run.promptCount}
            </span>
          )}
        </p>
      )}

      {run.lastError && (
        <p className="mt-2 text-sm text-red-600 dark:text-red-400" data-testid="run-last-error">
          {run.lastError}
        </p>
      )}
    </div>
  );
}

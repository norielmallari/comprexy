/**
 * Separated input / output / overhead metric cards for benchmark totals.
 * Shows presentation `$` beside token counts when a non-zero catalog model is selected.
 */

'use client';

import { formatTokenCostOverlay } from '@/components/cost/format-token-cost';
import { useCostModels } from '@/lib/queries/use-cost-models';
import { useDashboardStore } from '@/lib/store/dashboard-store';
import type { BenchmarkChannelDelta, ConversationTokenTotals } from '@/types/api';
import { formatCompactNumber, formatNumber } from '@/lib/utils';

interface IoTotalsCardsProps {
  /** Single-side telemetry totals */
  totals?: ConversationTokenTotals;
  /** Comparison channel deltas (when in comparison mode) */
  deltas?: {
    input: BenchmarkChannelDelta;
    output: BenchmarkChannelDelta;
    overhead: BenchmarkChannelDelta;
    turnCount: BenchmarkChannelDelta;
  };
  showComparison?: boolean;
}

function formatDelta(delta: number, deltaPercent: number | null): string {
  const sign = delta > 0 ? '+' : '';
  const pct =
    deltaPercent !== null && Number.isFinite(deltaPercent)
      ? ` (${sign}${deltaPercent.toFixed(1)}%)`
      : '';
  return `${sign}${formatNumber(delta)}${pct}`;
}

function DeltaBadge({
  label,
  delta,
  costOverlay,
}: {
  label: string;
  delta: BenchmarkChannelDelta;
  costOverlay?: string | null;
}) {
  const isNegative = delta.delta < 0;
  const isPositive = delta.delta > 0;
  return (
    <div
      className="rounded-lg border bg-white px-4 py-3 dark:border-slate-700 dark:bg-slate-800"
      role="region"
      aria-label={`${label} comparison`}
      data-testid={`io-card-${label.toLowerCase().replace(/\s+/g, '-')}`}
    >
      <p className="text-sm font-medium text-slate-500 dark:text-slate-400">{label}</p>
      <p className="mt-1 text-xs text-slate-500 dark:text-slate-400">
        Baseline: {formatNumber(delta.baseline)} → Compare: {formatNumber(delta.compare)}
      </p>
      <p
        className={`mt-2 text-lg font-semibold ${
          isNegative
            ? 'text-red-700 dark:text-red-400'
            : isPositive
              ? 'text-green-700 dark:text-green-400'
              : 'text-slate-900 dark:text-slate-100'
        }`}
      >
        Δ {formatDelta(delta.delta, delta.deltaPercent)}
        {costOverlay ? (
          <span
            className="ml-2 text-sm font-medium text-slate-600 dark:text-slate-300"
            aria-label={`Estimated cost ${costOverlay}`}
          >
            {costOverlay}
          </span>
        ) : null}
      </p>
    </div>
  );
}

function SingleCard({
  label,
  value,
  unit = 'tokens',
  costOverlay,
}: {
  label: string;
  value: number;
  unit?: string;
  costOverlay?: string | null;
}) {
  return (
    <div
      className="rounded-lg border bg-white px-4 py-3 dark:border-slate-700 dark:bg-slate-800"
      role="region"
      aria-label={label}
      data-testid={`io-card-${label.toLowerCase().replace(/\s+/g, '-')}`}
    >
      <p className="text-sm font-medium text-slate-500 dark:text-slate-400">{label}</p>
      <p className="mt-1 flex flex-wrap items-baseline gap-1.5 text-2xl font-semibold text-slate-900 dark:text-slate-100">
        <span>{formatCompactNumber(value, '', 0)}</span>
        <span className="text-sm font-normal text-slate-500">{unit}</span>
        {costOverlay ? (
          <span
            className="text-sm font-medium text-slate-600 dark:text-slate-300"
            aria-label={`Estimated cost ${costOverlay}`}
          >
            {costOverlay}
          </span>
        ) : null}
      </p>
    </div>
  );
}

export function IoTotalsCards({ totals, deltas, showComparison = false }: IoTotalsCardsProps) {
  const selectedCostModelKey = useDashboardStore((s) => s.selectedCostModelKey);
  const { data: models } = useCostModels();
  const model = models?.find((m) => m.modelKey === selectedCostModelKey) ?? null;

  if (showComparison && deltas) {
    return (
      <div
        className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4"
        data-testid="benchmark-io-cards"
      >
        <DeltaBadge
          label="Input tokens"
          delta={deltas.input}
          costOverlay={formatTokenCostOverlay(Math.abs(deltas.input.delta), model, 'input')}
        />
        <DeltaBadge
          label="Output tokens"
          delta={deltas.output}
          costOverlay={formatTokenCostOverlay(Math.abs(deltas.output.delta), model, 'output')}
        />
        <DeltaBadge
          label="Overhead tokens"
          delta={deltas.overhead}
          costOverlay={formatTokenCostOverlay(Math.abs(deltas.overhead.delta), model, 'input')}
        />
        <DeltaBadge label="Turn count" delta={deltas.turnCount} />
      </div>
    );
  }

  if (!totals) {
    return null;
  }

  return (
    <div
      className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4"
      data-testid="benchmark-io-cards"
    >
      <SingleCard
        label="Input tokens"
        value={totals.inputTokens}
        costOverlay={formatTokenCostOverlay(totals.inputTokens, model, 'input')}
      />
      <SingleCard
        label="Output tokens"
        value={totals.outputTokens}
        costOverlay={formatTokenCostOverlay(totals.outputTokens, model, 'output')}
      />
      <SingleCard
        label="Overhead tokens"
        value={totals.overheadTokens}
        costOverlay={formatTokenCostOverlay(totals.overheadTokens, model, 'input')}
      />
      <SingleCard label="Turn count" value={totals.turnCount} unit="turns" />
    </div>
  );
}

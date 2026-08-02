/**
 * Separated input / output / overhead metric cards for benchmark totals.
 */

'use client';

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
}: {
  label: string;
  delta: BenchmarkChannelDelta;
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
            ? 'text-red-600 dark:text-red-400'
            : isPositive
              ? 'text-green-600 dark:text-green-400'
              : 'text-slate-900 dark:text-slate-100'
        }`}
      >
        Δ {formatDelta(delta.delta, delta.deltaPercent)}
      </p>
    </div>
  );
}

function SingleCard({
  label,
  value,
  unit = 'tokens',
}: {
  label: string;
  value: number;
  unit?: string;
}) {
  return (
    <div
      className="rounded-lg border bg-white px-4 py-3 dark:border-slate-700 dark:bg-slate-800"
      role="region"
      aria-label={label}
      data-testid={`io-card-${label.toLowerCase().replace(/\s+/g, '-')}`}
    >
      <p className="text-sm font-medium text-slate-500 dark:text-slate-400">{label}</p>
      <p className="mt-1 text-2xl font-semibold text-slate-900 dark:text-slate-100">
        {formatCompactNumber(value, '', 0)}
        <span className="ml-1 text-sm font-normal text-slate-500">{unit}</span>
      </p>
    </div>
  );
}

export function IoTotalsCards({ totals, deltas, showComparison = false }: IoTotalsCardsProps) {
  if (showComparison && deltas) {
    return (
      <div
        className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4"
        data-testid="benchmark-io-cards"
      >
        <DeltaBadge label="Input tokens" delta={deltas.input} />
        <DeltaBadge label="Output tokens" delta={deltas.output} />
        <DeltaBadge label="Overhead tokens" delta={deltas.overhead} />
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
      <SingleCard label="Input tokens" value={totals.inputTokens} />
      <SingleCard label="Output tokens" value={totals.outputTokens} />
      <SingleCard label="Overhead tokens" value={totals.overheadTokens} />
      <SingleCard label="Turn count" value={totals.turnCount} unit="turns" />
    </div>
  );
}

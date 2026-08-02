/**
 * Clock and cost summary cards for benchmark comparison.
 */

'use client';

import type {
  BenchmarkChannelDelta,
  BenchmarkCostBreakdown,
  ConversationTokenTotals,
} from '@/types/api';
import { formatCompactNumber, formatNumber } from '@/lib/utils';

interface ComparisonSummaryCardsProps {
  baseline: ConversationTokenTotals;
  compare: ConversationTokenTotals;
  wallClockMs: BenchmarkChannelDelta | null;
  proxyDurationMs: BenchmarkChannelDelta | null;
  cost: BenchmarkCostBreakdown | null;
}

function formatMs(ms: number | null | undefined): string {
  if (ms === null || ms === undefined) {
    return '—';
  }
  if (ms < 1000) {
    return `${formatNumber(ms)} ms`;
  }
  return `${(ms / 1000).toFixed(1)} s`;
}

function ClockCard({
  label,
  baselineMs,
  compareMs,
  delta,
}: {
  label: string;
  baselineMs: number | null | undefined;
  compareMs: number | null | undefined;
  delta: BenchmarkChannelDelta | null;
}) {
  return (
    <div
      className="rounded-lg border bg-white px-4 py-3 dark:border-slate-700 dark:bg-slate-800"
      role="region"
      aria-label={label}
    >
      <p className="text-sm font-medium text-slate-500 dark:text-slate-400">{label}</p>
      <p className="mt-1 text-xs text-slate-500">
        Baseline: {formatMs(baselineMs)} · Compare: {formatMs(compareMs)}
      </p>
      {delta && (
        <p
          className={`mt-2 text-lg font-semibold ${
            delta.delta < 0
              ? 'text-green-600 dark:text-green-400'
              : delta.delta > 0
                ? 'text-red-600 dark:text-red-400'
                : ''
          }`}
        >
          Δ {formatMs(delta.delta)}
        </p>
      )}
      {label.includes('Wall') && !baselineMs && !compareMs && (
        <p className="mt-1 text-xs text-amber-600 dark:text-amber-400">
          Wall clock unavailable for ad-hoc operator conversations.
        </p>
      )}
    </div>
  );
}

export function ComparisonSummaryCards({
  baseline,
  compare,
  wallClockMs,
  proxyDurationMs,
  cost,
}: ComparisonSummaryCardsProps) {
  return (
    <div className="space-y-3" data-testid="benchmark-summary-cards">
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        <ClockCard
          label="Wall clock"
          baselineMs={baseline.wallClockMs}
          compareMs={compare.wallClockMs}
          delta={wallClockMs}
        />
        <ClockCard
          label="Proxy Σ DurationMs (diagnostic)"
          baselineMs={baseline.totalProxyDurationMs}
          compareMs={compare.totalProxyDurationMs}
          delta={proxyDurationMs}
        />
      </div>

      {cost && cost.modelKind === 'usd' && (
        <div
          className="rounded-lg border bg-white px-4 py-3 dark:border-slate-700 dark:bg-slate-800"
          role="region"
          aria-label="Cost comparison"
          data-testid="benchmark-cost-card"
        >
          <p className="text-sm font-medium text-slate-500 dark:text-slate-400">
            Estimated cost (USD)
          </p>
          <div className="mt-2 grid grid-cols-1 gap-2 text-sm sm:grid-cols-3">
            <div>
              <span className="text-slate-500">Baseline input:</span>{' '}
              ${cost.baselineInputCostUsd?.toFixed(4) ?? '—'}
            </div>
            <div>
              <span className="text-slate-500">Baseline output:</span>{' '}
              ${cost.baselineOutputCostUsd?.toFixed(4) ?? '—'}
            </div>
            <div>
              <span className="text-slate-500">Baseline overhead:</span>{' '}
              ${cost.baselineOverheadCostUsd?.toFixed(4) ?? '—'}
            </div>
            <div>
              <span className="text-slate-500">Compare input:</span>{' '}
              ${cost.compareInputCostUsd?.toFixed(4) ?? '—'}
            </div>
            <div>
              <span className="text-slate-500">Compare output:</span>{' '}
              ${cost.compareOutputCostUsd?.toFixed(4) ?? '—'}
            </div>
            <div>
              <span className="text-slate-500">Compare overhead:</span>{' '}
              ${cost.compareOverheadCostUsd?.toFixed(4) ?? '—'}
            </div>
          </div>
          <p className="mt-2 text-lg font-semibold">
            Total Δ ${cost.costDeltaUsd?.toFixed(4) ?? '—'}
            {cost.timeValueDeltaUsd !== null && cost.timeValueDeltaUsd !== undefined && (
              <span className="ml-3 text-sm font-normal text-slate-500">
                Time-value Δ ${cost.timeValueDeltaUsd.toFixed(4)}
              </span>
            )}
          </p>
          <p className="mt-2 text-xs text-slate-500" data-testid="cost-disclaimer">
            {cost.disclaimer}
          </p>
        </div>
      )}
    </div>
  );
}

interface TelemetryTimingCardProps {
  totals: ConversationTokenTotals;
  cost: BenchmarkCostBreakdown | null;
}

export function TelemetryTimingCard({ totals, cost }: TelemetryTimingCardProps) {
  return (
    <div className="space-y-3" data-testid="telemetry-timing-cards">
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
        <div className="rounded-lg border bg-white px-4 py-3 dark:border-slate-700 dark:bg-slate-800">
          <p className="text-sm text-slate-500">Proxy Σ DurationMs</p>
          <p className="text-xl font-semibold">
            {formatCompactNumber(totals.totalProxyDurationMs ?? 0, ' ms', 0)}
          </p>
        </div>
        <div className="rounded-lg border bg-white px-4 py-3 dark:border-slate-700 dark:bg-slate-800">
          <p className="text-sm text-slate-500">Upstream Σ DurationMs</p>
          <p className="text-xl font-semibold">
            {formatCompactNumber(totals.totalUpstreamDurationMs ?? 0, ' ms', 0)}
          </p>
        </div>
        <div className="rounded-lg border bg-white px-4 py-3 dark:border-slate-700 dark:bg-slate-800">
          <p className="text-sm text-slate-500">Prepare Σ DurationMs</p>
          <p className="text-xl font-semibold">
            {formatCompactNumber(totals.totalPrepareDurationMs ?? 0, ' ms', 0)}
          </p>
        </div>
      </div>
      {cost && cost.modelKind === 'usd' && cost.baselineTotalCostUsd !== null && (
        <div className="rounded-lg border bg-white px-4 py-3 dark:border-slate-700 dark:bg-slate-800">
          <p className="text-sm text-slate-500">Estimated total cost (USD)</p>
          <p className="text-xl font-semibold">${cost.baselineTotalCostUsd.toFixed(4)}</p>
          <p className="mt-1 text-xs text-slate-500" data-testid="cost-disclaimer">
            {cost.disclaimer}
          </p>
        </div>
      )}
    </div>
  );
}

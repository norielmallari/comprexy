/**
 * ChartTooltip component — hover panel for the bar chart.
 *
 * Rendered as recharts tooltip content, which positions the panel itself, so this component is
 * the plain panel with no positioning wrapper of its own.
 */

'use client';

import { type ChartDataPoint } from '@/types/chart';
import { formatCompactNumber, formatPercentage } from '@/lib/utils';

export interface ChartTooltipProps {
  data: ChartDataPoint | null;
  active: boolean;
}

function Row({
  label,
  value,
  className,
}: {
  label: string;
  value: string;
  className?: string;
}) {
  return (
    <div className="flex justify-between gap-6 text-xs">
      <span className="text-gray-500 dark:text-gray-400">{label}</span>
      <span className={className ?? 'font-mono text-gray-700 dark:text-gray-300'}>{value}</span>
    </div>
  );
}

/**
 * Shows the prepared-prompt breakdown, the baseline it is compared against, budget flags, and the
 * working memory version in play for a turn.
 */
export function ChartTooltip({ data, active }: ChartTooltipProps) {
  if (!active || !data) {
    return null;
  }

  return (
    <div
      data-testid="chart-tooltip"
      className="max-w-xs rounded-lg border border-gray-200 bg-white p-3 shadow-lg dark:border-gray-700 dark:bg-gray-900"
    >
      <div className="space-y-2">
        <div className="flex items-center justify-between gap-6 border-b border-gray-200 pb-2 dark:border-gray-700">
          <span className="text-sm font-semibold text-gray-900 dark:text-gray-100">
            Turn {data.turnIndex}
          </span>
          <span className="text-xs text-gray-500 dark:text-gray-400">{data.model}</span>
        </div>

        <div className="space-y-1">
          <Row label="System" value={formatCompactNumber(data.systemTokens)} />
          <Row label="History + tools" value={formatCompactNumber(data.historyTokens)} />
          <Row
            label="Compressed WM"
            value={
              data.workingMemoryVersion === null
                ? 'none yet'
                : formatCompactNumber(data.workingMemoryTokens)
            }
          />
        </div>

        <div className="space-y-1 border-t border-gray-200 pt-2 dark:border-gray-700">
          <Row
            label="Prepared prompt"
            value={formatCompactNumber(data.preparedPromptTokens)}
            className="font-mono font-medium text-gray-700 dark:text-gray-300"
          />
          <Row
            label="Baseline (ghost)"
            value={formatCompactNumber(data.baselineTokens)}
            className="font-mono font-medium text-gray-500 dark:text-gray-400"
          />
          <Row
            label="Net Saved"
            value={`${data.netTokensSaved >= 0 ? '+' : ''}${formatCompactNumber(data.netTokensSaved)}`}
            className={`font-mono font-medium ${
              data.netTokensSaved >= 0
                ? 'text-emerald-600 dark:text-emerald-400'
                : 'text-red-600 dark:text-red-400'
            }`}
          />
          <Row
            label="Savings Ratio"
            value={formatPercentage(data.savingsRatio)}
            className="font-mono font-medium text-blue-600 dark:text-blue-400"
          />
        </div>

        {(data.softBudgetExceeded || data.hardBudgetExceeded) && (
          <div className="flex gap-2">
            {data.softBudgetExceeded && (
              <span className="rounded-full bg-amber-100 px-2 py-0.5 text-xs font-medium text-amber-700 dark:bg-amber-900/30 dark:text-amber-400">
                Soft Budget
              </span>
            )}
            {data.hardBudgetExceeded && (
              <span className="rounded-full bg-red-100 px-2 py-0.5 text-xs font-medium text-red-700 dark:bg-red-900/30 dark:text-red-400">
                Hard Budget
              </span>
            )}
          </div>
        )}

        <div className="text-xs text-gray-500 dark:text-gray-400">
          {data.workingMemoryVersion === null
            ? 'No working memory yet'
            : `WM v${data.workingMemoryVersion}`}
        </div>
      </div>
    </div>
  );
}

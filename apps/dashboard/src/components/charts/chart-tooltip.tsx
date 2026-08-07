/**
 * ChartTooltip component — hover panel for the bar chart.
 *
 * Rendered as recharts tooltip content, which positions the panel itself, so this component is
 * the plain panel with no positioning wrapper of its own.
 */

'use client';

import { type ChartDataPoint } from '@/types/chart';
import {
  CLIENT_TOOLS_STACK_LABEL,
  FULL_HISTORY_EST_LABEL,
  HISTORY_STACK_LABEL,
  RULES_STACK_LABEL,
  SAVED_VS_FULL_HISTORY_LABEL,
  SAVINGS_VS_FULL_HISTORY_RATIO_LABEL,
  VIRTUAL_TOOLS_STACK_LABEL,
} from '@/lib/constants';
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
 * Shows the prepared-prompt breakdown, SoftBudget IR-full ghost baseline, VT channel when
 * present, budget flags, and the working memory version in play for a turn.
 */
export function ChartTooltip({ data, active }: ChartTooltipProps) {
  if (!active || !data) {
    return null;
  }

  const vt = data.virtualToolsTokensSaved;

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
          <Row
            label={`${VIRTUAL_TOOLS_STACK_LABEL} (catalog)`}
            value={formatCompactNumber(data.virtualToolSchemaTokens)}
          />
          <Row
            label={`${CLIENT_TOOLS_STACK_LABEL} (catalog)`}
            value={formatCompactNumber(data.clientToolSchemaTokens)}
          />
          {data.rulesTokens > 0 && (
            <Row
              label={RULES_STACK_LABEL}
              value={formatCompactNumber(data.rulesTokens)}
            />
          )}
          <Row
            label={HISTORY_STACK_LABEL}
            value={formatCompactNumber(data.historyTokens)}
          />
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
            label={FULL_HISTORY_EST_LABEL}
            value={formatCompactNumber(data.baselineTokens)}
            className="font-mono font-medium text-gray-500 dark:text-gray-400"
          />
          {data.isLegacyMixedAxis && (
            <p className="text-[11px] text-amber-800 dark:text-amber-400">
              Legacy mixed-axis — ghost uses NativeRaw
            </p>
          )}
          <Row
            label={SAVED_VS_FULL_HISTORY_LABEL}
            value={`${data.netTokensSaved >= 0 ? '+' : ''}${formatCompactNumber(data.netTokensSaved)}`}
            className={`font-mono font-medium ${
              data.netTokensSaved >= 0
                ? 'text-emerald-700 dark:text-emerald-400'
                : 'text-red-700 dark:text-red-400'
            }`}
          />
          <Row
            label={SAVINGS_VS_FULL_HISTORY_RATIO_LABEL}
            value={formatPercentage(data.savingsRatio)}
            className="font-mono font-medium text-blue-600 dark:text-blue-400"
          />
          {vt !== null && (
            <>
              <Row
                label="VT / native-wire"
                value={`${vt >= 0 ? '+' : ''}${formatCompactNumber(vt)}`}
                className={`font-mono font-medium ${
                  vt >= 0
                    ? 'text-emerald-700 dark:text-emerald-400'
                    : 'text-red-700 dark:text-red-400'
                }`}
              />
            </>
          )}
        </div>

        {(data.softBudgetExceeded || data.hardBudgetExceeded) && (
          <div className="flex gap-2">
            {data.softBudgetExceeded && (
              <span className="rounded-full bg-amber-100 px-2 py-0.5 text-xs font-medium text-amber-800 dark:bg-amber-900/30 dark:text-amber-400">
                Soft Budget
              </span>
            )}
            {data.hardBudgetExceeded && (
              <span className="rounded-full bg-red-100 px-2 py-0.5 text-xs font-medium text-red-800 dark:bg-red-900/30 dark:text-red-400">
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

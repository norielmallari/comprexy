/**
 * ChartTooltip component — hover tooltip for the bar chart.
 *
 * Displays detailed turn information when hovering over a bar segment.
 */

'use client';

import { type ChartDataPoint } from '@/types/chart';
import { formatCompactNumber, formatPercentage } from '@/lib/utils';
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from '@/components/ui/tooltip';

export interface ChartTooltipProps {
  data: ChartDataPoint | null;
  active: boolean;
}

/**
 * A tooltip that follows the cursor on hover over the chart.
 * Shows turn index, model, token counts, budget flags, and WM version.
 */
export function ChartTooltip({ data, active }: ChartTooltipProps) {
  if (!active || !data) {
    return null;
  }

  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <div className="cursor-crosshair" />
      </TooltipTrigger>
      <TooltipContent
        className="max-w-xs border border-gray-200 bg-white p-3 shadow-lg dark:border-gray-700 dark:bg-gray-900"
        side="top"
        align="center"
      >
        <div className="space-y-2">
          {/* Header: Turn index + model */}
          <div className="flex items-center justify-between border-b border-gray-200 pb-2 dark:border-gray-700">
            <span className="text-sm font-semibold text-gray-900 dark:text-gray-100">
              Turn {data.turnIndex}
            </span>
            <span className="text-xs text-gray-500 dark:text-gray-400">
              {data.model}
            </span>
          </div>

          {/* Token breakdown */}
          <div className="space-y-1">
            <div className="flex justify-between text-xs">
              <span className="text-gray-500 dark:text-gray-400">
                Prompt
              </span>
              <span className="font-mono text-gray-700 dark:text-gray-300">
                {formatCompactNumber(data.promptTokens)}
              </span>
            </div>
            <div className="flex justify-between text-xs">
              <span className="text-gray-500 dark:text-gray-400">
                System
              </span>
              <span className="font-mono text-gray-700 dark:text-gray-300">
                {formatCompactNumber(data.systemTokens)}
              </span>
            </div>
            <div className="flex justify-between text-xs">
              <span className="text-gray-500 dark:text-gray-400">
                Compressed WM
              </span>
              <span className="font-mono text-gray-700 dark:text-gray-300">
                {formatCompactNumber(data.compressedTokens)}
              </span>
            </div>
            <div className="flex justify-between text-xs">
              <span className="text-gray-500 dark:text-gray-400">
                Overhead
              </span>
              <span className="font-mono text-amber-600 dark:text-amber-400">
                {formatCompactNumber(data.overheadTokens)}
              </span>
            </div>
          </div>

          {/* Totals */}
          <div className="border-t border-gray-200 pt-2 dark:border-gray-700">
            <div className="flex justify-between text-xs">
              <span className="text-gray-500 dark:text-gray-400">
                Total Compressed
              </span>
              <span className="font-mono font-medium text-gray-700 dark:text-gray-300">
                {formatCompactNumber(data.totalCompressed)}
              </span>
            </div>
            <div className="flex justify-between text-xs">
              <span className="text-gray-500 dark:text-gray-400">
                Baseline (ghost)
              </span>
              <span className="font-mono font-medium text-gray-500 dark:text-gray-400">
                {formatCompactNumber(data.baselineTokens)}
              </span>
            </div>
            <div className="flex justify-between text-xs">
              <span className="text-gray-500 dark:text-gray-400">
                Net Saved
              </span>
              <span
                className={`font-mono font-medium ${
                  data.netTokensSaved >= 0
                    ? 'text-emerald-600 dark:text-emerald-400'
                    : 'text-red-600 dark:text-red-400'
                }`}
              >
                {data.netTokensSaved >= 0 ? '+' : ''}
                {formatCompactNumber(data.netTokensSaved)}
              </span>
            </div>
          </div>

          {/* Savings ratio */}
          <div className="flex justify-between text-xs">
            <span className="text-gray-500 dark:text-gray-400">
              Savings Ratio
            </span>
            <span className="font-mono font-medium text-blue-600 dark:text-blue-400">
              {formatPercentage(data.savingsRatio)}
            </span>
          </div>

          {/* Budget flags */}
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

          {/* WM version */}
          {data.workingMemoryVersion !== null && (
            <div className="text-xs text-gray-500 dark:text-gray-400">
              WM v{data.workingMemoryVersion}
            </div>
          )}
        </div>
      </TooltipContent>
    </Tooltip>
  );
}

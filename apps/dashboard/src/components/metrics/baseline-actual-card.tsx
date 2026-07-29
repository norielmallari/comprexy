/**
 * Baseline vs Actual Tokens card showing estimated baseline tokens,
 * actual tokens consumed, and the delta/savings percentage.
 *
 * Displays two-column sub-layout within the card showing baseline
 * and actual side-by-side with computed savings.
 */

import { formatNumber } from "@/lib/utils";

interface BaselineActualCardProps {
  totalBaselineTokensEstimated: number | null;
  totalActualTokensEstimated: number | null;
}

export function BaselineActualCard({
  totalBaselineTokensEstimated,
  totalActualTokensEstimated,
}: BaselineActualCardProps) {
  const baseline = totalBaselineTokensEstimated ?? null;
  const actual = totalActualTokensEstimated ?? null;
  const hasData = baseline !== null && actual !== null && baseline > 0;

  const delta = hasData ? baseline - actual : 0;
  const savingsPct =
    hasData && delta > 0
      ? ((delta / baseline) * 100).toFixed(1)
      : null;

  return (
    <div
      className="rounded-lg border bg-white px-6 py-5 dark:border-slate-700 dark:bg-slate-800"
      role="region"
      aria-label="Baseline vs Actual Tokens"
    >
      <p className="mb-2 text-sm font-medium text-slate-500 dark:text-slate-400">
        Baseline vs Actual Tokens
      </p>

      <div className="grid grid-cols-2 gap-4">
        {/* Baseline */}
        <div>
          <p className="text-xs font-medium text-slate-400 dark:text-slate-500">
            Baseline
          </p>
          <p className="text-2xl font-semibold text-slate-900 dark:text-slate-100">
            {baseline !== null ? formatNumber(baseline) : "\u2014"}
          </p>
        </div>

        {/* Actual */}
        <div>
          <p className="text-xs font-medium text-slate-400 dark:text-slate-500">
            Actual
          </p>
          <p className="text-2xl font-semibold text-slate-900 dark:text-slate-100">
            {actual !== null ? formatNumber(actual) : "\u2014"}
          </p>
        </div>
      </div>

      {/* Delta / Savings */}
      <div className="mt-3 flex items-baseline gap-2">
        <span className="text-sm text-slate-500 dark:text-slate-400">
          {hasData && delta !== null ? (
            <>
              Delta: {formatNumber(Math.abs(delta))}{" "}
              {delta > 0 ? "saved" : "over"}
              {savingsPct !== null && (
                <>
                  {" "}
                  ({savingsPct}%)
                </>
              )}
            </>
          ) : (
            "\u2014"
          )}
        </span>
      </div>
    </div>
  );
}

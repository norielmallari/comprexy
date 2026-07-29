/**
 * Hero card showing the two most important metrics:
 * - Tokens Saved (absolute)
 * - Weighted Compression Ratio (percentage)
 *
 * Displays numbers in large monospaced font in a 2-column grid.
 */

import { formatNumber } from "@/lib/utils";

interface HeroCardProps {
  tokensSaved: number | null;
  weightedCompressionRatio: number | null;
}

export function HeroCard({
  tokensSaved,
  weightedCompressionRatio,
}: HeroCardProps) {
  return (
    <div className="grid grid-cols-1 gap-4 sm:grid-cols-2" role="region" aria-label="Key metrics">
      {/* Tokens Saved */}
      <div className="rounded-lg border bg-white px-6 py-6 dark:border-slate-700 dark:bg-slate-800">
        <p className="mb-2 text-sm font-medium text-slate-500 dark:text-slate-400">
          Tokens Saved
        </p>
        <p className="flex items-baseline gap-2">
          <span className="font-mono text-[48px] leading-none font-semibold text-emerald-600 dark:text-emerald-400">
            {tokensSaved !== null ? formatNumber(tokensSaved) : "—"}
          </span>
        </p>
      </div>

      {/* Weighted Compression Ratio */}
      <div className="rounded-lg border bg-white px-6 py-6 dark:border-slate-700 dark:bg-slate-800">
        <p className="mb-2 text-sm font-medium text-slate-500 dark:text-slate-400">
          Weighted Compression
        </p>
        <p className="flex items-baseline gap-2">
          <span className="font-mono text-[48px] leading-none font-semibold text-blue-600 dark:text-blue-400">
            {weightedCompressionRatio !== null
              ? `${(weightedCompressionRatio * 100).toFixed(1)}%`
              : "—"}
          </span>
        </p>
      </div>
    </div>
  );
}

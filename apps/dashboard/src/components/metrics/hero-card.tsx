/**
 * Hero card showing the single most important metric:
 * - Tokens Saved (SoftBudget net absolute)
 *
 * Displays the number in large monospaced font.
 */

import { formatNumber } from "@/lib/utils";

interface HeroCardProps {
  tokensSaved: number | null;
}

export function HeroCard({ tokensSaved }: HeroCardProps) {
  return (
    <div
      className="h-full rounded-lg border bg-white px-4 py-2.5 dark:border-slate-700 dark:bg-slate-800"
      role="region"
      aria-label="Tokens Saved"
    >
      <p className="mb-0.5 text-sm font-medium text-slate-500 dark:text-slate-400">
        Tokens Saved
      </p>
      <p className="flex items-baseline gap-1.5">
        <span className="font-mono text-4xl leading-tight font-semibold text-emerald-700 dark:text-emerald-400">
          {tokensSaved !== null ? formatNumber(tokensSaved) : "\u2014"}
        </span>
      </p>
      <p className="mt-1 text-xs leading-snug text-slate-500 dark:text-slate-400">
        SoftBudget net (IR full − prepared)
      </p>
    </div>
  );
}

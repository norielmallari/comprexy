/**
 * Hero card showing the single most important metric:
 * - Tokens Saved (SoftBudget net absolute)
 *
 * Displays the number in large monospaced font, with optional `$` overlay.
 */

'use client';

import { formatTokenCostOverlay } from '@/components/cost/format-token-cost';
import { useCostModels } from '@/lib/queries/use-cost-models';
import { useDashboardStore } from '@/lib/store/dashboard-store';
import { formatNumber } from '@/lib/utils';

interface HeroCardProps {
  tokensSaved: number | null;
}

export function HeroCard({ tokensSaved }: HeroCardProps) {
  const selectedCostModelKey = useDashboardStore((s) => s.selectedCostModelKey);
  const { data: models } = useCostModels();
  const model = models?.find((m) => m.modelKey === selectedCostModelKey) ?? null;
  const costOverlay = formatTokenCostOverlay(tokensSaved, model, 'input');

  return (
    <div
      className="h-full rounded-lg border bg-white px-4 py-2.5 dark:border-slate-700 dark:bg-slate-800"
      role="region"
      aria-label="Tokens Saved"
    >
      <p className="mb-0.5 text-sm font-medium text-slate-500 dark:text-slate-400">
        Tokens Saved
      </p>
      <p className="flex flex-wrap items-baseline gap-2">
        <span className="font-mono text-4xl leading-tight font-semibold text-emerald-700 dark:text-emerald-400">
          {tokensSaved !== null ? formatNumber(tokensSaved) : '\u2014'}
        </span>
        {costOverlay ? (
          <span
            className="text-lg font-medium text-emerald-800 dark:text-emerald-300"
            aria-label={`Estimated cost ${costOverlay}`}
          >
            {costOverlay}
          </span>
        ) : null}
      </p>
      <p className="mt-1 text-xs leading-snug text-slate-500 dark:text-slate-400">
        Saved vs full history (full history − prepared)
      </p>
    </div>
  );
}

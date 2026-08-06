/**
 * Baseline (combined) card — SoftBudget IR full + completion (not NativeRaw input-only).
 * Kept distinct from the chart SoftBudget (IR full) ghost, which is prompt-only.
 */

'use client';

import { formatTokenCostOverlay } from '@/components/cost/format-token-cost';
import { useCostModels } from '@/lib/queries/use-cost-models';
import { useDashboardStore } from '@/lib/store/dashboard-store';
import { formatNumber } from '@/lib/utils';

import { MetricCard } from './metric-card';

interface BaselineTokensCardProps {
  totalBaselineTokensEstimated: number | null;
}

export function BaselineTokensCard({
  totalBaselineTokensEstimated,
}: BaselineTokensCardProps) {
  const selectedCostModelKey = useDashboardStore((s) => s.selectedCostModelKey);
  const { data: models } = useCostModels();
  const model = models?.find((m) => m.modelKey === selectedCostModelKey) ?? null;

  return (
    <MetricCard
      title="Baseline (combined)"
      value={
        totalBaselineTokensEstimated !== null
          ? formatNumber(totalBaselineTokensEstimated)
          : '—'
      }
      unit="tokens"
      description="SoftBudget IR full + completion"
      costOverlay={formatTokenCostOverlay(totalBaselineTokensEstimated, model, 'input')}
    />
  );
}

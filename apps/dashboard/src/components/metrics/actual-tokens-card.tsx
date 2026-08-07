/**
 * Actual (combined) card — SoftBudget prepared input + output + overhead (absolute).
 * Kept distinct from the chart SoftBudget (IR full) ghost baseline.
 */

'use client';

import { formatTokenCostOverlay } from '@/components/cost/format-token-cost';
import { useCostModels } from '@/lib/queries/use-cost-models';
import { useDashboardStore } from '@/lib/store/dashboard-store';
import { formatNumber } from '@/lib/utils';

import { MetricCard } from './metric-card';

interface ActualTokensCardProps {
  totalActualTokensEstimated: number | null;
}

export function ActualTokensCard({
  totalActualTokensEstimated,
}: ActualTokensCardProps) {
  const selectedCostModelKey = useDashboardStore((s) => s.selectedCostModelKey);
  const { data: models } = useCostModels();
  const model = models?.find((m) => m.modelKey === selectedCostModelKey) ?? null;

  return (
    <MetricCard
      title="Actual (combined)"
      value={
        totalActualTokensEstimated !== null
          ? formatNumber(totalActualTokensEstimated)
          : '—'
      }
      unit="tokens"
      description="Prepared + completion + overhead"
      costOverlay={formatTokenCostOverlay(totalActualTokensEstimated, model, 'input')}
    />
  );
}

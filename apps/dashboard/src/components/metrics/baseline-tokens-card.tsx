/**
 * Baseline (combined) card — SoftBudget IR full + completion (not NativeRaw input-only).
 * Kept distinct from the chart SoftBudget (IR full) ghost, which is prompt-only.
 */

import { formatNumber } from "@/lib/utils";

import { MetricCard } from "./metric-card";

interface BaselineTokensCardProps {
  totalBaselineTokensEstimated: number | null;
}

export function BaselineTokensCard({
  totalBaselineTokensEstimated,
}: BaselineTokensCardProps) {
  return (
    <MetricCard
      title="Baseline (combined)"
      value={
        totalBaselineTokensEstimated !== null
          ? formatNumber(totalBaselineTokensEstimated)
          : "—"
      }
      unit="tokens"
      description="SoftBudget IR full + completion"
    />
  );
}

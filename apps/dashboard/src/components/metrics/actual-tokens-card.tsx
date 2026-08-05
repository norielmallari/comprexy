/**
 * Actual (combined) card — SoftBudget prepared input + output + overhead (absolute).
 * Kept distinct from the chart SoftBudget (IR full) ghost baseline.
 */

import { formatNumber } from "@/lib/utils";

import { MetricCard } from "./metric-card";

interface ActualTokensCardProps {
  totalActualTokensEstimated: number | null;
}

export function ActualTokensCard({
  totalActualTokensEstimated,
}: ActualTokensCardProps) {
  return (
    <MetricCard
      title="Actual (combined)"
      value={
        totalActualTokensEstimated !== null
          ? formatNumber(totalActualTokensEstimated)
          : "—"
      }
      unit="tokens"
      description="SoftBudget prepared + completion + overhead"
    />
  );
}

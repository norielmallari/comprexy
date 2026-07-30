/**
 * Baseline Tokens card — estimated tokens without compression.
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
      title="Baseline Tokens"
      value={
        totalBaselineTokensEstimated !== null
          ? formatNumber(totalBaselineTokensEstimated)
          : "—"
      }
      unit="tokens"
    />
  );
}

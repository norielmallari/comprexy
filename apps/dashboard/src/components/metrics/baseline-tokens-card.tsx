/**
 * Baseline (combined) card — estimated uncompressed input + output (not input-only).
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
    />
  );
}

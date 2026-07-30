/**
 * Actual Tokens card — total tokens consumed after compression.
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
      title="Actual Tokens"
      value={
        totalActualTokensEstimated !== null
          ? formatNumber(totalActualTokensEstimated)
          : "—"
      }
      unit="tokens"
    />
  );
}

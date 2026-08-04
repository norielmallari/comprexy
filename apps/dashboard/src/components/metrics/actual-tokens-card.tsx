/**
 * Actual (combined) card — compressed input + output + overhead (absolute).
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
    />
  );
}

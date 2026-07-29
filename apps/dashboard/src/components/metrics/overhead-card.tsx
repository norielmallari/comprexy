/**
 * Overhead card showing compression overhead as a percentage.
 * Computed from MetricsSummary.TotalCompressionOverheadTokens and TotalBaselineTokensEstimated.
 */

import { MetricCard } from "./metric-card";

interface OverheadCardProps {
  totalCompressionOverheadTokens: number | null;
  totalBaselineTokensEstimated: number | null;
}

export function OverheadCard({
  totalCompressionOverheadTokens,
  totalBaselineTokensEstimated,
}: OverheadCardProps) {
  const displayValue =
    totalBaselineTokensEstimated !== null && totalBaselineTokensEstimated > 0
      ? `${(
          (totalCompressionOverheadTokens ?? 0) /
          totalBaselineTokensEstimated *
          100
        ).toFixed(1)}`
      : "—";

  return (
    <MetricCard
      title="Overhead"
      value={displayValue}
      unit="%"
      variant="compact"
    />
  );
}

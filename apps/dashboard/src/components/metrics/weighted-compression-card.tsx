/**
 * Weighted Compression card — SoftBudget aggregate savings ÷ SoftBudget aggregate baseline.
 *
 * The REST contract exposes this value as `AverageTokenSavingsRatio` for legacy reasons.
 */

import { MetricCard } from "./metric-card";

interface WeightedCompressionCardProps {
  weightedTokenSavingsRatio: number | null;
}

export function WeightedCompressionCard({
  weightedTokenSavingsRatio,
}: WeightedCompressionCardProps) {
  const displayValue =
    weightedTokenSavingsRatio !== null
      ? (weightedTokenSavingsRatio * 100).toFixed(1)
      : "—";

  return (
    <MetricCard
      title="Weighted Compression"
      value={displayValue}
      unit="%"
      description="Weighted vs full history"
    />
  );
}

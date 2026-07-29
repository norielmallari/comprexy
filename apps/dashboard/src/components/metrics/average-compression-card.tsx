/**
 * Average Compression card showing the average token savings ratio as a percentage.
 * Derived from MetricsSummary.AverageTokenSavingsRatio.
 */

import { MetricCard } from "./metric-card";

interface AverageCompressionCardProps {
  averageTokenSavingsRatio: number | null;
}

export function AverageCompressionCard({
  averageTokenSavingsRatio,
}: AverageCompressionCardProps) {
  const displayValue =
    averageTokenSavingsRatio !== null
      ? `${(averageTokenSavingsRatio * 100).toFixed(1)}`
      : "—";

  return (
    <MetricCard
      title="Average Compression"
      value={displayValue}
      unit="%"
    />
  );
}

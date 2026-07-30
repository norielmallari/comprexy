/**
 * Average Compression card showing the simple mean of per-turn savings ratios.
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

/**
 * Best Compression card — peak per-turn net token savings ratio as a percentage.
 * Derived from turn metrics (max NetTokenSavingsRatio); same idea as telemetry PeakSavingsRatio.
 */

import { MetricCard } from "./metric-card";

interface BestCompressionCardProps {
  bestCompressionRatio: number | null;
}

export function BestCompressionCard({
  bestCompressionRatio,
}: BestCompressionCardProps) {
  const displayValue =
    bestCompressionRatio !== null
      ? `${(bestCompressionRatio * 100).toFixed(1)}`
      : "—";

  return (
    <MetricCard
      title="Best Compression"
      value={displayValue}
      unit="%"
    />
  );
}

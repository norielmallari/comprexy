/**
 * Compression Health card showing Best Compression %, Overhead %,
 * and Working Memory badge in a compact horizontal row.
 */

import { Badge } from "@/components/ui/badge";
import { getWmColor } from "@/lib/utils";
import { useTheme } from "@/hooks/use-theme";

interface CompressionHealthCardProps {
  bestCompressionRatio: number | null;
  totalCompressionOverheadTokens: number | null;
  totalBaselineTokensEstimated: number | null;
  maxWorkingMemoryVersion: number | null;
}

export function CompressionHealthCard({
  bestCompressionRatio,
  totalCompressionOverheadTokens,
  totalBaselineTokensEstimated,
  maxWorkingMemoryVersion,
}: CompressionHealthCardProps) {
  const { theme } = useTheme();
  const isDark = theme === "dark";

  const bestDisplay =
    bestCompressionRatio !== null
      ? `${(bestCompressionRatio * 100).toFixed(1)}%`
      : "\u2014";

  const overheadDisplay =
    totalBaselineTokensEstimated !== null && totalBaselineTokensEstimated > 0
      ? `${(
          (totalCompressionOverheadTokens ?? 0) /
          totalBaselineTokensEstimated *
          100
        ).toFixed(1)}%`
      : "\u2014";

  return (
    <div
      className="rounded-lg border bg-white px-6 py-5 dark:border-slate-700 dark:bg-slate-800"
      role="region"
      aria-label="Compression Health"
    >
      <p className="mb-2 text-sm font-medium text-slate-500 dark:text-slate-400">
        Compression Health
      </p>

      <div className="flex items-center justify-between gap-4">
        {/* Best Compression */}
        <div>
          <p className="text-xs font-medium text-slate-400 dark:text-slate-500">
            Best Compression
          </p>
          <p className="text-lg font-semibold text-slate-900 dark:text-slate-100">
            {bestDisplay}
          </p>
        </div>

        {/* Overhead */}
        <div>
          <p className="text-xs font-medium text-slate-400 dark:text-slate-500">
            Overhead
          </p>
          <p className="text-lg font-semibold text-slate-900 dark:text-slate-100">
            {overheadDisplay}
          </p>
        </div>

        {/* Working Memory */}
        <div>
          <p className="text-xs font-medium text-slate-400 dark:text-slate-500">
            Working Memory
          </p>
          {maxWorkingMemoryVersion !== null ? (
            <Badge
              variant="default"
              style={{
                backgroundColor: getWmColor(maxWorkingMemoryVersion, isDark),
                color: maxWorkingMemoryVersion === 0 ? "#1e293b" : "#ffffff",
              }}
            >
              v{maxWorkingMemoryVersion}
            </Badge>
          ) : (
            <p className="text-sm text-slate-400 dark:text-slate-500">{"\u2014"}</p>
          )}
        </div>
      </div>
    </div>
  );
}

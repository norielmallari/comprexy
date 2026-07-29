/**
 * Compression Ratios card showing Weighted Compression and Average
 * Compression side-by-side. Both display the same underlying value
 * (`averageTokenSavingsRatio * 100`) as a percentage.
 */

interface CompressionRatioCardProps {
  averageTokenSavingsRatio: number | null;
}

export function CompressionRatioCard({
  averageTokenSavingsRatio,
}: CompressionRatioCardProps) {
  const displayValue =
    averageTokenSavingsRatio !== null
      ? `${(averageTokenSavingsRatio * 100).toFixed(1)}`
      : "\u2014";

  return (
    <div
      className="rounded-lg border bg-white px-6 py-5 dark:border-slate-700 dark:bg-slate-800"
      role="region"
      aria-label="Compression Ratios"
    >
      <p className="mb-2 text-sm font-medium text-slate-500 dark:text-slate-400">
        Compression Ratios
      </p>

      <div className="grid grid-cols-2 gap-4">
        {/* Weighted Compression */}
        <div>
          <p className="text-xs font-medium text-blue-600 dark:text-blue-400">
            Weighted Compression
          </p>
          <p className="text-2xl font-semibold text-slate-900 dark:text-slate-100">
            {displayValue}%
          </p>
        </div>

        {/* Average Compression */}
        <div>
          <p className="text-xs font-medium text-slate-500 dark:text-slate-400">
            Average Compression
          </p>
          <p className="text-2xl font-semibold text-slate-900 dark:text-slate-100">
            {displayValue}%
          </p>
        </div>
      </div>
    </div>
  );
}

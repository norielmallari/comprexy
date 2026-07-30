/**
 * Reusable single-column card for displaying a single metric.
 *
 * Usage:
 *   <MetricCard title="Average Compression" value="67.3" unit="%" />
 */

interface MetricCardProps {
  title: string;
  value: string;
  unit: string;
  variant?: "default" | "compact";
}

export function MetricCard({
  title,
  value,
  unit,
  variant = "default",
}: MetricCardProps) {
  return (
    <div
      className="h-full rounded-lg border bg-white px-6 py-5 dark:border-slate-700 dark:bg-slate-800"
      role="region"
      aria-label={title}
    >
      <p className="mb-1 text-sm font-medium text-slate-500 dark:text-slate-400">
        {title}
      </p>
      {variant === "compact" ? (
        <p className="flex items-baseline gap-2">
          <span className="text-2xl font-semibold text-slate-900 dark:text-slate-100">
            {value}
          </span>
          <span className="text-sm text-slate-500 dark:text-slate-400">
            {unit}
          </span>
        </p>
      ) : (
        <p className="flex items-baseline gap-2">
          <span className="text-4xl font-semibold text-slate-900 dark:text-slate-100">
            {value}
          </span>
          <span className="text-base text-slate-500 dark:text-slate-400">
            {unit}
          </span>
        </p>
      )}
    </div>
  );
}

/**
 * Reusable single-column card for displaying a single metric.
 *
 * Usage:
 *   <MetricCard title="Average Compression" value="67.3" unit="%" />
 *   <MetricCard title="Input tokens" value="1,200" unit="tokens" costOverlay="$0.0036" />
 */

interface MetricCardProps {
  title: string;
  value: string;
  unit: string;
  variant?: "default" | "compact";
  /** Optional factual note under the value (e.g. SoftBudget / VT channel copy). */
  description?: string;
  descriptionPlacement?: "below" | "inline";
  /** Optional presentation `$` overlay beside tokens (null/undefined = tokens only). */
  costOverlay?: string | null;
}

export function MetricCard({
  title,
  value,
  unit,
  variant = "default",
  description,
  descriptionPlacement = "below",
  costOverlay,
}: MetricCardProps) {
  return (
    <div
      className="h-full rounded-lg border bg-white px-4 py-2.5 dark:border-slate-700 dark:bg-slate-800"
      role="region"
      aria-label={title}
    >
      <p className="mb-0.5 text-sm font-medium text-slate-500 dark:text-slate-400">
        {title}
      </p>
      {variant === "compact" ? (
        <p className={`flex items-baseline gap-1.5 ${descriptionPlacement === "inline" ? "flex-nowrap" : "flex-wrap"}`}>
          <span className="text-2xl font-semibold leading-tight text-slate-900 dark:text-slate-100">
            {value}
          </span>
          <span className="text-sm text-slate-500 dark:text-slate-400">
            {unit}
          </span>
          {costOverlay ? (
            <span className="text-sm font-medium text-slate-600 dark:text-slate-300" aria-label={`Estimated cost ${costOverlay}`}>
              {costOverlay}
            </span>
          ) : null}
          {description && descriptionPlacement === "inline" ? (
            <span className="min-w-0 flex-1 self-start text-right text-xs leading-snug text-slate-500 dark:text-slate-400">
              {description}
            </span>
          ) : null}
        </p>
      ) : (
        <p className={`flex items-baseline gap-1.5 ${descriptionPlacement === "inline" ? "flex-nowrap" : "flex-wrap"}`}>
          <span className="text-3xl font-semibold leading-tight text-slate-900 dark:text-slate-100">
            {value}
          </span>
          <span className="text-base text-slate-500 dark:text-slate-400">
            {unit}
          </span>
          {costOverlay ? (
            <span className="text-base font-medium text-slate-600 dark:text-slate-300" aria-label={`Estimated cost ${costOverlay}`}>
              {costOverlay}
            </span>
          ) : null}
          {description && descriptionPlacement === "inline" ? (
            <span className="min-w-0 flex-1 self-start text-right text-xs leading-snug text-slate-500 dark:text-slate-400">
              {description}
            </span>
          ) : null}
        </p>
      )}
      {description && descriptionPlacement === "below" ? (
        <p className="mt-1 text-xs leading-snug text-slate-500 dark:text-slate-400">
          {description}
        </p>
      ) : null}
    </div>
  );
}

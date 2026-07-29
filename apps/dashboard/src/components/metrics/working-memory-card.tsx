/**
 * Working Memory card showing the max working memory version used.
 * Derived from MetricsSummary.MaxWorkingMemoryVersionUsed.
 *
 * Displays the version as a color-coded badge.
 */

import { Badge } from "@/components/ui/badge";
import { getWmColor } from "@/lib/utils";
import { useTheme } from "@/hooks/use-theme";

interface WorkingMemoryCardProps {
  maxWorkingMemoryVersion: number | null;
}

export function WorkingMemoryCard({
  maxWorkingMemoryVersion,
}: WorkingMemoryCardProps) {
  const { theme } = useTheme();
  const isDark = theme === "dark";

  if (maxWorkingMemoryVersion === null) {
    return (
      <div className="rounded-lg border bg-white px-6 py-5 dark:border-slate-700 dark:bg-slate-800">
        <p className="mb-1 text-sm font-medium text-slate-500 dark:text-slate-400">
          Working Memory
        </p>
        <p className="text-sm text-slate-400 dark:text-slate-500">No data</p>
      </div>
    );
  }

  const color = getWmColor(maxWorkingMemoryVersion, isDark);

  return (
    <div className="rounded-lg border bg-white px-6 py-5 dark:border-slate-700 dark:bg-slate-800">
      <p className="mb-2 text-sm font-medium text-slate-500 dark:text-slate-400">
        Working Memory
      </p>
      <Badge
        variant="default"
        style={{
          backgroundColor: color,
          color: maxWorkingMemoryVersion === 0 ? "#1e293b" : "#ffffff",
        }}
      >
        v{maxWorkingMemoryVersion}
      </Badge>
    </div>
  );
}

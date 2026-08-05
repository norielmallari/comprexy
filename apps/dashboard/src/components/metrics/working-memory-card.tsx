/**
 * Working Memory card showing the max working memory version used.
 * Derived from turn metrics (`WorkingMemoryVersionUsed`); see getMaxWorkingMemoryVersion.
 *
 * Displays the version as a color-coded badge.
 */

import { Badge } from "@/components/ui/badge";
import { getContrastingForeground, getWmColor } from "@/lib/utils";
import { useTheme } from "@/hooks/use-theme";

interface WorkingMemoryCardProps {
  maxWorkingMemoryVersion: number | null;
}

export function WorkingMemoryCard({
  maxWorkingMemoryVersion,
}: WorkingMemoryCardProps) {
  const { theme } = useTheme();
  const isDark = theme === "dark";

  if (maxWorkingMemoryVersion == null) {
    return (
      <div
        className="h-full rounded-lg border bg-white px-4 py-2.5 dark:border-slate-700 dark:bg-slate-800"
        role="region"
        aria-label="Working Memory"
      >
        <p className="mb-0.5 text-sm font-medium text-slate-500 dark:text-slate-400">
          Working Memory
        </p>
        <p className="text-2xl font-semibold leading-tight text-slate-500 dark:text-slate-400">
          No data
        </p>
      </div>
    );
  }

  const color = getWmColor(maxWorkingMemoryVersion, isDark);

  return (
    <div
      className="h-full rounded-lg border bg-white px-4 py-2.5 dark:border-slate-700 dark:bg-slate-800"
      role="region"
      aria-label="Working Memory"
    >
      <p className="mb-0.5 text-sm font-medium text-slate-500 dark:text-slate-400">
        Working Memory
      </p>
      <Badge
        variant="default"
        className="px-2.5 py-0.5 text-2xl font-semibold"
        style={{
          backgroundColor: color,
          color: getContrastingForeground(color),
        }}
      >
        v{maxWorkingMemoryVersion}
      </Badge>
    </div>
  );
}

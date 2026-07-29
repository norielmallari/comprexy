/**
 * ChartLegend component — color keys for the bar chart segments.
 */

'use client';

import { ChartLegendItem } from '@/types/chart';

export interface ChartLegendProps {
  items: ChartLegendItem[];
}

/**
 * Renders a horizontal legend showing color-coded keys for all chart segments.
 */
export function ChartLegend({ items }: ChartLegendProps) {
  return (
    <div className="flex flex-wrap items-center justify-center gap-4 py-4">
      {items.map((item) => (
        <div key={item.label} className="flex items-center gap-2">
          <span
            className="inline-block h-3 w-3 rounded"
            style={{ backgroundColor: item.color }}
          />
          <span className="text-sm text-gray-600 dark:text-gray-400">
            {item.label}
          </span>
        </div>
      ))}
    </div>
  );
}

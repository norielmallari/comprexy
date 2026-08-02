/**
 * Shared Y-axis max helper for comparison mode charts.
 */

import { CHART_Y_AXIS_MAX_DEFAULT } from '@/lib/constants';
import type { ChartDataPoint } from '@/types/chart';

export function computeChartYMax(data: ChartDataPoint[]): number {
  if (data.length === 0) {
    return CHART_Y_AXIS_MAX_DEFAULT;
  }
  const maxVal = Math.max(
    ...data.map((d) => Math.max(d.baselineTokens, d.preparedPromptTokens)),
  );
  return Math.ceil(maxVal * 1.1);
}

export function computeSharedChartYMax(
  ...datasets: ChartDataPoint[][]
): number {
  const nonEmpty = datasets.filter((d) => d.length > 0);
  if (nonEmpty.length === 0) {
    return CHART_Y_AXIS_MAX_DEFAULT;
  }
  return Math.max(...nonEmpty.map(computeChartYMax));
}

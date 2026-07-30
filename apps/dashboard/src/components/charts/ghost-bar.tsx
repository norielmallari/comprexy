/**
 * Ghost bar — the baseline (uncompressed) reference drawn behind the stacked bars.
 *
 * Exported as props rather than a component: recharts discovers its graphical items by inspecting
 * the direct children of `BarChart` for its own component types, so a custom wrapper element is
 * skipped entirely and never renders. The caller spreads these onto a real `<Bar>`.
 */

import {
  GHOST_BAR_FILL_DARK,
  GHOST_BAR_FILL_LIGHT,
  GHOST_BAR_FILL_OPACITY,
  GHOST_BAR_STROKE_DARK,
  GHOST_BAR_STROKE_LIGHT,
} from '@/lib/constants';

export interface GhostBarOptions {
  dataKey: string;
  /**
   * Recharts lays sibling bars out side by side within one x-axis band. Assigning the ghost to a
   * second, hidden x-axis gives it its own band so it overlaps the stack instead of sitting beside
   * it. Render it before the stacked bars so it paints underneath.
   */
  xAxisId: string;
  isDark?: boolean;
}

/** Only the recharts `Bar` props the ghost sets; deliberately no `stackId`. */
export interface GhostBarRenderProps {
  dataKey: string;
  xAxisId: string;
  fill: string;
  fillOpacity: number;
  stroke: string;
  strokeWidth: number;
  strokeDasharray: string;
  isAnimationActive: boolean;
  radius: [number, number, number, number];
}

export function getGhostBarProps({
  dataKey,
  xAxisId,
  isDark = false,
}: GhostBarOptions): GhostBarRenderProps {
  return {
    dataKey,
    xAxisId,
    fill: isDark ? GHOST_BAR_FILL_DARK : GHOST_BAR_FILL_LIGHT,
    fillOpacity: GHOST_BAR_FILL_OPACITY,
    stroke: isDark ? GHOST_BAR_STROKE_DARK : GHOST_BAR_STROKE_LIGHT,
    strokeWidth: 1,
    strokeDasharray: '3 2',
    isAnimationActive: false,
    radius: [4, 4, 0, 0],
  };
}

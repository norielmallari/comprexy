/**
 * BarChart component — stacked vertical bar chart for SoftBudget compression metrics.
 *
 * Each bar is the prompt actually prepared for a turn, split into system, virtual tools,
 * client tools, optional rules, history, and working memory. A ghost bar behind the stack
 * shows the SoftBudget IR-full estimate (no WM fold) for the same turn.
 */

'use client';

import { useMemo, useState } from 'react';
import {
  BarChart as RechartsBarChart,
  Bar,
  XAxis,
  YAxis,
  CartesianGrid,
  ResponsiveContainer,
  Tooltip as RechartsTooltip,
} from 'recharts';
import { ChartDataPoint, ChartLegendItem } from '@/types/chart';
import {
  CHART_HEIGHT,
  CHART_WIDTH,
  CHART_Y_AXIS_MIN,
  CHART_Y_AXIS_MAX_DEFAULT,
  getPreparedSegmentColors,
  CHART_SEGMENT_STROKE_LIGHT,
  CHART_SEGMENT_STROKE_DARK,
  CHART_SEGMENT_STROKE_WIDTH,
  GHOST_BAR_STROKE_LIGHT,
  GHOST_BAR_STROKE_DARK,
  FULL_HISTORY_EST_LABEL,
  VIRTUAL_TOOLS_STACK_LABEL,
  CLIENT_TOOLS_STACK_LABEL,
  RULES_STACK_LABEL,
  HISTORY_STACK_LABEL,
} from '@/lib/constants';
import { formatCompactNumber } from '@/lib/utils';
import { ChartLegend } from './chart-legend';
import { ChartTooltip } from './chart-tooltip';
import { getGhostBarProps } from './ghost-bar';

/** Hidden second x-axis that lets the ghost bar overlap the stack instead of sitting beside it. */
const GHOST_X_AXIS_ID = 'ghost';

export interface BarChartProps {
  data: ChartDataPoint[];
  isLoading?: boolean;
  /** When set (e.g. comparison mode), both charts share this Y-axis max. */
  sharedMaxY?: number;
  title?: string;
  compact?: boolean;
  /**
   * Stretch the plot into the parent’s remaining height. Parent must be a sized
   * flex child (`flex-1 min-h-0`). Falls back to {@link CHART_HEIGHT} when false.
   */
  fill?: boolean;
  /** Root test hook; defaults to telemetry single-chart id. */
  testId?: string;
}

/**
 * Flattens ChartDataPoint[] into the named keys recharts stacks on.
 */
function transformChartData(data: ChartDataPoint[]): Record<string, unknown>[] {
  return data.map((point) => ({
    ...point,
    system: point.systemTokens,
    virtualTools: point.virtualToolSchemaTokens,
    clientTools: point.clientToolSchemaTokens,
    rules: point.rulesTokens,
    history: point.historyTokens,
    workingMemory: point.workingMemoryTokens,
    baseline: point.baselineTokens,
  }));
}

function seriesHasRules(data: ChartDataPoint[]): boolean {
  return data.some((point) => point.rulesTokens > 0);
}

function getLegendItems(isDark: boolean, includeRules: boolean): ChartLegendItem[] {
  const colors = getPreparedSegmentColors(isDark);
  const items: ChartLegendItem[] = [
    { label: 'System', color: colors.system },
    { label: VIRTUAL_TOOLS_STACK_LABEL, color: colors.virtualToolSchema },
    { label: CLIENT_TOOLS_STACK_LABEL, color: colors.clientToolSchema },
  ];
  if (includeRules) {
    items.push({ label: RULES_STACK_LABEL, color: colors.rules });
  }
  items.push(
    { label: HISTORY_STACK_LABEL, color: colors.history },
    { label: 'Compressed WM', color: colors.workingMemory },
    {
      label: FULL_HISTORY_EST_LABEL,
      color: isDark ? GHOST_BAR_STROKE_DARK : GHOST_BAR_STROKE_LIGHT,
      outlined: true,
    },
  );
  return items;
}

/**
 * Recharts clones its tooltip content with the hovered payload, so the data point is read back
 * off `payload` rather than passed in as a prop.
 */
function TurnTooltip({
  active,
  payload,
}: {
  active?: boolean;
  payload?: { payload?: ChartDataPoint }[];
}) {
  return <ChartTooltip active={Boolean(active)} data={payload?.[0]?.payload ?? null} />;
}

export function BarChart({
  data,
  isLoading = false,
  sharedMaxY,
  title = 'Token Counts by Turn',
  compact = false,
  fill = false,
  testId = 'token-counts-by-turn-chart',
}: BarChartProps) {
  const [isDark] = useState(() => {
    if (typeof window !== 'undefined') {
      return document.documentElement.classList.contains('dark');
    }
    return false;
  });

  const chartData = useMemo(() => transformChartData(data), [data]);
  const includeRules = useMemo(() => seriesHasRules(data), [data]);

  // Both series share one y-axis, so the domain must cover the taller of ghost and stack.
  const yMax = useMemo(() => {
    if (sharedMaxY !== undefined) {
      return sharedMaxY;
    }
    if (data.length === 0) return CHART_Y_AXIS_MAX_DEFAULT;
    const maxVal = Math.max(
      ...data.map((d) => Math.max(d.baselineTokens, d.preparedPromptTokens)),
    );
    return Math.ceil(maxVal * 1.1);
  }, [data, sharedMaxY]);

  const legendItems = useMemo(
    () => getLegendItems(isDark, includeRules),
    [isDark, includeRules],
  );
  const shellClass = fill
    ? 'flex h-full min-h-0 flex-col gap-2'
    : 'flex flex-col gap-2';
  const plotHeight = fill ? '100%' : CHART_HEIGHT;
  const fallbackBoxStyle = fill
    ? undefined
    : { width: CHART_WIDTH, height: CHART_HEIGHT };
  const segmentStroke = isDark ? CHART_SEGMENT_STROKE_DARK : CHART_SEGMENT_STROKE_LIGHT;
  const segmentColors = getPreparedSegmentColors(isDark);

  if (data.length === 0 && !isLoading) {
    return (
      <div
        className={
          fill
            ? 'flex h-full min-h-[220px] items-center justify-center'
            : 'flex h-96 items-center justify-center'
        }
        style={fallbackBoxStyle}
      >
        <p className="text-gray-500 dark:text-gray-400">
          No data to display. Select a conversation to view metrics.
        </p>
      </div>
    );
  }

  if (isLoading) {
    return (
      <div
        className={
          fill
            ? 'flex h-full min-h-[220px] items-center justify-center'
            : 'flex h-96 items-center justify-center'
        }
        style={fallbackBoxStyle}
      >
        <p className="text-gray-500 dark:text-gray-400">Loading chart data...</p>
      </div>
    );
  }

  return (
    <div className={shellClass}>
      <h3 className="shrink-0 text-sm font-semibold text-gray-900 dark:text-gray-100">
        {title}
      </h3>

      <div className="shrink-0">
        <ChartLegend items={legendItems} />
      </div>

      <div
        role="img"
        aria-label={`Prepared prompt tokens per turn across ${data.length} turns, with Full History Est. (no WM fold) as the ghost baseline behind each bar`}
        data-testid={testId}
        className={fill ? 'min-h-0 flex-1' : undefined}
      >
        <ResponsiveContainer width="100%" height={plotHeight}>
          <RechartsBarChart
            data={chartData}
            margin={{ top: 8, right: 20, left: 12, bottom: 12 }}
          >
            <CartesianGrid strokeDasharray="3 3" stroke={isDark ? '#374151' : '#e5e7eb'} />

            <XAxis
              dataKey="turnIndex"
              stroke={isDark ? '#4b5563' : '#d1d5db'}
              label={{
                value: 'Turn Index',
                position: 'insideBottom',
                offset: -15,
                style: {
                  fill: isDark ? '#9ca3af' : '#6b7280',
                  fontSize: 12,
                },
              }}
            />

            {/* Layout-only axis for the ghost overlay; shares the same categories. */}
            <XAxis dataKey="turnIndex" xAxisId={GHOST_X_AXIS_ID} hide />

            <YAxis
              label={{
                value: 'Tokens',
                angle: -90,
                position: 'insideLeft',
                offset: -10,
                style: {
                  fill: isDark ? '#9ca3af' : '#6b7280',
                  fontSize: 12,
                },
              }}
              stroke={isDark ? '#4b5563' : '#d1d5db'}
              tickFormatter={(value: number) => formatCompactNumber(value)}
              domain={[CHART_Y_AXIS_MIN, yMax]}
            />

            <RechartsTooltip
              content={<TurnTooltip />}
              cursor={{ fill: isDark ? '#1f2937' : '#f3f4f6', opacity: 0.5 }}
            />

            {/* Declared first so it paints behind the stacked segments. */}
            <Bar
              name={FULL_HISTORY_EST_LABEL}
              {...getGhostBarProps({
                dataKey: 'baseline',
                xAxisId: GHOST_X_AXIS_ID,
                isDark,
              })}
            />

            <Bar
              name="System"
              dataKey="system"
              stackId="prompt"
              fill={segmentColors.system}
              stroke={segmentStroke}
              strokeWidth={CHART_SEGMENT_STROKE_WIDTH}
            />
            <Bar
              name={VIRTUAL_TOOLS_STACK_LABEL}
              dataKey="virtualTools"
              stackId="prompt"
              fill={segmentColors.virtualToolSchema}
              stroke={segmentStroke}
              strokeWidth={CHART_SEGMENT_STROKE_WIDTH}
            />
            <Bar
              name={CLIENT_TOOLS_STACK_LABEL}
              dataKey="clientTools"
              stackId="prompt"
              fill={segmentColors.clientToolSchema}
              stroke={segmentStroke}
              strokeWidth={CHART_SEGMENT_STROKE_WIDTH}
            />
            {includeRules && (
              <Bar
                name={RULES_STACK_LABEL}
                dataKey="rules"
                stackId="prompt"
                fill={segmentColors.rules}
                stroke={segmentStroke}
                strokeWidth={CHART_SEGMENT_STROKE_WIDTH}
              />
            )}
            <Bar
              name={HISTORY_STACK_LABEL}
              dataKey="history"
              stackId="prompt"
              fill={segmentColors.history}
              stroke={segmentStroke}
              strokeWidth={CHART_SEGMENT_STROKE_WIDTH}
            />
            <Bar
              name="Compressed WM"
              dataKey="workingMemory"
              stackId="prompt"
              fill={segmentColors.workingMemory}
              stroke={segmentStroke}
              strokeWidth={CHART_SEGMENT_STROKE_WIDTH}
              radius={[4, 4, 0, 0]}
            />
          </RechartsBarChart>
        </ResponsiveContainer>
      </div>

      {!compact && (
        <p className="shrink-0 text-xs text-gray-500 dark:text-gray-400">
          Bars show the prompt Comprexy actually prepared for each turn. The dashed ghost behind each
          bar is Full History Est. (no WM fold). On legacy turns without IrFull, the ghost falls back
          to NativeRaw. Compressed WM stays empty until the first working memory version exists.
        </p>
      )}
    </div>
  );
}

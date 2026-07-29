/**
 * BarChart component — stacked vertical bar chart for compression metrics.
 *
 * Shows per-turn token counts with compressed WM segments and a ghost bar
 * for baseline comparison.
 */

'use client';

import { useMemo, useState, useCallback, useRef, useEffect } from 'react';
import {
  BarChart as RechartsBarChart,
  Bar,
  XAxis,
  YAxis,
  CartesianGrid,
  ResponsiveContainer,
  Tooltip as RechartsTooltip,
  Label,
} from 'recharts';
import { ChartDataPoint } from '@/types/chart';
import { CHART_HEIGHT, CHART_WIDTH, CHART_Y_AXIS_MIN, CHART_Y_AXIS_MAX_DEFAULT, WM_COLORS_LIGHT, WM_COLORS_DARK, OVERHEAD_COLOR, GHOST_BAR_COLOR } from '@/lib/constants';
import { formatCompactNumber } from '@/lib/utils';
import { ChartTooltip } from './chart-tooltip';
import { ChartLegend } from './chart-legend';
import { GhostBar } from './ghost-bar';

export interface BarChartProps {
  data: ChartDataPoint[];
  isLoading?: boolean;
}

/**
 * Transforms ChartDataPoint[] into a format suitable for recharts stacked bars.
 * Each turn becomes a data entry with all segment keys.
 */
function transformChartData(data: ChartDataPoint[]): Record<string, unknown>[] {
  return data.map((point) => ({
    turnIndex: point.turnIndex,
    model: point.model,
    promptTokens: point.promptTokens,
    systemTokens: point.systemTokens,
    compressedTokens: point.compressedTokens,
    overheadTokens: point.overheadTokens,
    baselineTokens: point.baselineTokens,
    workingMemoryVersion: point.workingMemoryVersion,
    totalCompressed: point.totalCompressed,
    netTokensSaved: point.netTokensSaved,
    savingsRatio: point.savingsRatio,
    softBudgetExceeded: point.softBudgetExceeded,
    hardBudgetExceeded: point.hardBudgetExceeded,
    // recharts stacked bar needs named keys for each segment
    prompt: point.promptTokens,
    system: point.systemTokens,
    compressed: point.compressedTokens,
    overhead: point.overheadTokens,
    baseline: point.baselineTokens,
  }));
}

/**
 * Legend items with their color keys.
 */
function getLegendItems(isDark: boolean): { label: string; color: string }[] {
  return [
    { label: 'Prompt', color: '#94a3b8' },
    { label: 'System', color: '#cbd5e0' },
    { label: 'Compressed WM', color: isDark ? WM_COLORS_DARK[3] : WM_COLORS_LIGHT[3] },
    { label: 'Overhead', color: OVERHEAD_COLOR },
    { label: 'Baseline (ghost)', color: GHOST_BAR_COLOR },
  ];
}

/**
 * Custom tooltip component that follows the cursor.
 */
function CustomTooltip({ active, payload, dataPoint }: { active?: boolean; payload?: Record<string, unknown>[]; dataPoint?: ChartDataPoint | null }) {
  if (!active || !dataPoint) {
    return null;
  }

  return (
    <div className="max-w-xs rounded-lg border border-gray-200 bg-white p-3 shadow-lg dark:border-gray-700 dark:bg-gray-900">
      <div className="space-y-2">
        {/* Header */}
        <div className="flex items-center justify-between border-b border-gray-200 pb-2 dark:border-gray-700">
          <span className="text-sm font-semibold text-gray-900 dark:text-gray-100">
            Turn {dataPoint.turnIndex}
          </span>
          <span className="text-xs text-gray-500 dark:text-gray-400">
            {dataPoint.model}
          </span>
        </div>

        {/* Token breakdown */}
        <div className="space-y-1">
          <div className="flex justify-between text-xs">
            <span className="text-gray-500 dark:text-gray-400">Prompt</span>
            <span className="font-mono text-gray-700 dark:text-gray-300">
              {formatCompactNumber(dataPoint.promptTokens)}
            </span>
          </div>
          <div className="flex justify-between text-xs">
            <span className="text-gray-500 dark:text-gray-400">System</span>
            <span className="font-mono text-gray-700 dark:text-gray-300">
              {formatCompactNumber(dataPoint.systemTokens)}
            </span>
          </div>
          <div className="flex justify-between text-xs">
            <span className="text-gray-500 dark:text-gray-400">Compressed WM</span>
            <span className="font-mono text-gray-700 dark:text-gray-300">
              {formatCompactNumber(dataPoint.compressedTokens)}
            </span>
          </div>
          <div className="flex justify-between text-xs">
            <span className="text-gray-500 dark:text-gray-400">Overhead</span>
            <span className="font-mono text-amber-600 dark:text-amber-400">
              {formatCompactNumber(dataPoint.overheadTokens)}
            </span>
          </div>
        </div>

        {/* Totals */}
        <div className="border-t border-gray-200 pt-2 dark:border-gray-700">
          <div className="flex justify-between text-xs">
            <span className="text-gray-500 dark:text-gray-400">Total Compressed</span>
            <span className="font-mono font-medium text-gray-700 dark:text-gray-300">
              {formatCompactNumber(dataPoint.totalCompressed)}
            </span>
          </div>
          <div className="flex justify-between text-xs">
            <span className="text-gray-500 dark:text-gray-400">Baseline (ghost)</span>
            <span className="font-mono font-medium text-gray-500 dark:text-gray-400">
              {formatCompactNumber(dataPoint.baselineTokens)}
            </span>
          </div>
          <div className="flex justify-between text-xs">
            <span className="text-gray-500 dark:text-gray-400">Net Saved</span>
            <span
              className={`font-mono font-medium ${
                dataPoint.netTokensSaved >= 0
                  ? 'text-emerald-600 dark:text-emerald-400'
                  : 'text-red-600 dark:text-red-400'
              }`}
            >
              {dataPoint.netTokensSaved >= 0 ? '+' : ''}
              {formatCompactNumber(dataPoint.netTokensSaved)}
            </span>
          </div>
        </div>

        {/* Savings ratio */}
        <div className="flex justify-between text-xs">
          <span className="text-gray-500 dark:text-gray-400">Savings Ratio</span>
          <span className="font-mono font-medium text-blue-600 dark:text-blue-400">
            {dataPoint.savingsRatio * 100}%
          </span>
        </div>

        {/* Budget flags */}
        {(dataPoint.softBudgetExceeded || dataPoint.hardBudgetExceeded) && (
          <div className="flex gap-2">
            {dataPoint.softBudgetExceeded && (
              <span className="rounded-full bg-amber-100 px-2 py-0.5 text-xs font-medium text-amber-700 dark:bg-amber-900/30 dark:text-amber-400">
                Soft Budget
              </span>
            )}
            {dataPoint.hardBudgetExceeded && (
              <span className="rounded-full bg-red-100 px-2 py-0.5 text-xs font-medium text-red-700 dark:bg-red-900/30 dark:text-red-400">
                Hard Budget
              </span>
            )}
          </div>
        )}

        {/* WM version */}
        {dataPoint.workingMemoryVersion !== null && (
          <div className="text-xs text-gray-500 dark:text-gray-400">
            WM v{dataPoint.workingMemoryVersion}
          </div>
        )}
      </div>
    </div>
  );
}

/**
 * Main BarChart component.
 *
 * Renders a stacked vertical bar chart showing token counts per turn,
 * with a ghost bar for baseline comparison and hover tooltips.
 */
export function BarChart({ data, isLoading = false }: BarChartProps) {
  const [activeIndex, setActiveIndex] = useState<number | null>(null);
  const [activeDataPoint, setActiveDataPoint] = useState<ChartDataPoint | null>(null);
  const chartRef = useRef<HTMLDivElement>(null);
  const [isDark] = useState(() => {
    if (typeof window !== 'undefined') {
      return document.documentElement.classList.contains('dark');
    }
    return false;
  });

  // Recharts needs data in a specific format
  const chartData = useMemo(() => transformChartData(data), [data]);

  // Compute dynamic Y-axis max: highest of baseline or total tokens across all turns
  const yMax = useMemo(() => {
    if (data.length === 0) return CHART_Y_AXIS_MAX_DEFAULT;
    const maxVal = Math.max(...data.map((d) => Math.max(d.baselineTokens, d.totalCompressed)));
    return Math.ceil(maxVal * 1.1); // 10% headroom
  }, [data]);

  // Get legend items based on theme
  const legendItems = useMemo(() => getLegendItems(isDark), [isDark]);

  // Handle bar hover
  const handleBarHover = useCallback(
    (entry: { payload: Record<string, unknown>; index: number }) => {
      if (entry.payload && typeof entry.payload.turnIndex === 'number') {
        const idx = entry.index;
        setActiveIndex(idx);
        if (data[idx]) {
          setActiveDataPoint(data[idx]);
        }
      }
    },
    [data]
  );

  // Reset hover state
  const handleBarLeave = useCallback(() => {
    setActiveIndex(null);
    setActiveDataPoint(null);
  }, []);

  // If no data, show empty state
  if (data.length === 0 && !isLoading) {
    return (
      <div
        className="flex h-96 items-center justify-center"
        style={{ width: CHART_WIDTH, height: CHART_HEIGHT }}
      >
        <p className="text-gray-500 dark:text-gray-400">
          No data to display. Select a conversation to view metrics.
        </p>
      </div>
    );
  }

  // If loading, show skeleton
  if (isLoading) {
    return (
      <div
        className="flex h-96 items-center justify-center"
        style={{ width: CHART_WIDTH, height: CHART_HEIGHT }}
      >
        <p className="text-gray-500 dark:text-gray-400">Loading chart data...</p>
      </div>
    );
  }

  return (
    <div className="space-y-4" ref={chartRef}>
      {/* Chart title */}
      <h3 className="text-lg font-semibold text-gray-900 dark:text-gray-100">
        Token Counts by Turn
      </h3>

      {/* Legend */}
      <ChartLegend items={legendItems} />

      {/* Chart */}
      <ResponsiveContainer width="100%" height={CHART_HEIGHT}>
        <RechartsBarChart
          data={chartData}
          margin={{ top: 10, right: 30, left: 20, bottom: 20 }}
        >
          {/* Grid */}
          <CartesianGrid
            strokeDasharray="3 3"
            stroke={isDark ? '#374151' : '#e5e7eb'}
          />

          {/* X axis */}
          <XAxis
            dataKey="turnIndex"
            stroke={isDark ? '#4b5563' : '#d1d5db'}
          />

          {/* X axis label */}
          <Label
            value="Turn Index"
            position="outside"
            offset={60}
            style={{
              fill: isDark ? '#9ca3af' : '#6b7280',
              fontSize: 12,
            }}
          />

          {/* Y axis */}
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
            domain={[
              CHART_Y_AXIS_MIN,
              yMax,
            ]}
          />

          {/* Tooltip */}
          <RechartsTooltip
            content={<CustomTooltip />}
            cursor={{ fill: isDark ? '#1f2937' : '#f3f4f6', opacity: 0.5 }}
          />

          {/* Ghost bar (baseline) — rendered behind */}
          <GhostBar
            dataKey="baseline"
            baselineData={chartData}
            fill={GHOST_BAR_COLOR}
          />

          {/* Stacked bars */}
          <Bar
            name="Prompt"
            dataKey="prompt"
            stackId="tokens"
            fill="#94a3b8"
            radius={[0, 0, 0, 0]}
          />
          <Bar
            name="System"
            dataKey="system"
            stackId="tokens"
            fill="#cbd5e0"
          />
          <Bar
            name="Compressed WM"
            dataKey="compressed"
            stackId="tokens"
            fill={isDark ? WM_COLORS_DARK[3] : WM_COLORS_LIGHT[3]}
          />
          <Bar
            name="Overhead"
            dataKey="overhead"
            stackId="tokens"
            fill={OVERHEAD_COLOR}
            radius={[4, 4, 0, 0]}
          />
        </RechartsBarChart>
      </ResponsiveContainer>

      {/* Footer note */}
      <p className="text-xs text-gray-500 dark:text-gray-400">
        Hover over bars to see detailed token counts per turn. Ghost bar shows baseline (uncompressed) token count.
      </p>
    </div>
  );
}

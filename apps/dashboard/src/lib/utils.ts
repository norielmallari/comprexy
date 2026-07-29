/**
 * Utility functions for the dashboard.
 */

import { clsx, type ClassValue } from 'clsx';
import { twMerge } from 'tailwind-merge';
import type { ConversationTurnMetricDto } from '@/types/api';
import type { ChartDataPoint } from '@/types/chart';

/**
 * Conditionally join Tailwind CSS class names.
 *
 * Uses clsx for conditional joining and tailwind-merge to resolve conflicts.
 *
 * @param inputs - Class names to join
 * @returns Merged class name string
 */
export function cn(...inputs: ClassValue[]): string {
  return twMerge(clsx(inputs));
}

/**
 * Max working-memory version used across turn metrics.
 * Per dashboard plan: derived from turns (`WorkingMemoryVersionUsed`); null → 0.
 * Returns null when there are no turns (card shows "No data").
 */
export function getMaxWorkingMemoryVersion(
  turns: ConversationTurnMetricDto[] | null | undefined,
): number | null {
  if (!turns || turns.length === 0) {
    return null;
  }

  let max = 0;
  for (const turn of turns) {
    const version = turn.workingMemoryVersionUsed ?? 0;
    if (version > max) {
      max = version;
    }
  }

  return max;
}

/**
 * Best (peak) per-turn net token savings ratio across turn metrics.
 * Matches telemetry PeakSavingsRatio: max of NetTokenSavingsRatio.
 * Returns null when there are no turns.
 */
export function getBestCompressionRatio(
  turns: ConversationTurnMetricDto[] | null | undefined,
): number | null {
  if (!turns || turns.length === 0) {
    return null;
  }

  let peak = Number.NEGATIVE_INFINITY;
  for (const turn of turns) {
    if (turn.netTokenSavingsRatio > peak) {
      peak = turn.netTokenSavingsRatio;
    }
  }

  return peak;
}

/**
 * Format a number with thousand separators.
 *
 * @param value - The number to format
 * @returns Formatted string with commas
 */
export function formatNumber(value: number): string {
  return new Intl.NumberFormat('en-US').format(value);
}

/**
 * Format a number with significant digits and optional unit suffix.
 *
 * @param value - The number to format
 * @param suffix - Optional unit suffix (e.g., 'K', 'M')
 * @param decimals - Number of decimal places (default: 1)
 * @returns Formatted string
 */
export function formatCompactNumber(
  value: number,
  suffix?: string,
  decimals: number = 1,
): string {
  if (Math.abs(value) >= 1_000_000) {
    return `${(value / 1_000_000).toFixed(decimals)}M${suffix ?? ''}`;
  }
  if (Math.abs(value) >= 1_000) {
    return `${(value / 1_000).toFixed(decimals)}K${suffix ?? ''}`;
  }
  return `${value.toFixed(decimals)}${suffix ?? ''}`;
}

/**
 * Format a percentage value.
 *
 * @param ratio - A ratio between 0 and 1 (e.g., 0.25 = 25%)
 * @param decimals - Number of decimal places (default: 1)
 * @returns Formatted percentage string
 */
export function formatPercentage(ratio: number, decimals: number = 1): string {
  return `${(ratio * 100).toFixed(decimals)}%`;
}

/**
 * Format a timestamp for display.
 *
 * @param date - ISO 8601 date string or Date object
 * @returns Formatted date string (e.g., "Jul 29, 2025, 3:45 PM")
 */
export function formatDateTime(date: string | Date): string {
  const d = typeof date === 'string' ? new Date(date) : date;
  return new Intl.DateTimeFormat('en-US', {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
  }).format(d);
}

/**
 * Format a relative time (e.g., "2 hours ago").
 *
 * @param date - ISO 8601 date string or Date object
 * @returns Relative time string
 */
export function formatRelativeTime(date: string | Date): string {
  const d = typeof date === 'string' ? new Date(date) : date;
  const now = new Date();
  const diffMs = now.getTime() - d.getTime();
  const diffSec = Math.floor(diffMs / 1000);
  const diffMin = Math.floor(diffSec / 60);
  const diffHour = Math.floor(diffMin / 60);
  const diffDay = Math.floor(diffHour / 24);

  if (diffSec < 60) return 'just now';
  if (diffMin < 60) return `${diffMin}m ago`;
  if (diffHour < 24) return `${diffHour}h ago`;
  return `${diffDay}d ago`;
}

/**
 * Truncate a conversation ID to a short display form.
 *
 * @param id - Full UUID string
 * @returns First 8 characters of the ID
 */
export function truncateConversationId(id: string): string {
  return id.slice(0, 8);
}

/**
 * Encode a conversation ID for use in URL query parameters.
 *
 * @param id - Conversation ID UUID
 * @returns URL-encoded string
 */
export function encodeConversationId(id: string): string {
  return encodeURIComponent(id);
}

/**
 * Decode a conversation ID from URL query parameters.
 *
 * @param encoded - URL-encoded string
 * @returns Decoded conversation ID
 */
export function decodeConversationId(encoded: string): string {
  return decodeURIComponent(encoded);
}

/**
 * Get the color for a working memory version.
 *
 * @param version - Working memory version number
 * @param isDark - Whether dark mode is active
 * @returns Color hex string
 */
export function getWmColor(
  version: number,
  isDark: boolean,
): string {
  const colors = isDark
    ? ['#2a3a52', '#3d5a80', '#4a7ab5', '#5b8fd4']
    : ['#e0e7ef', '#a8c4e0', '#6ba3d6', '#2d6bc4'];

  return colors[version] ?? colors[0];
}

/**
 * Transform turn metrics from the API into chart-ready data points.
 *
 * The API returns aggregated token counts, while the chart needs a
 * breakdown into segments (prompt, system, compressed WM, overhead).
 *
 * Mapping:
 *   - promptTokens: actualPromptTokens from API
 *   - systemTokens: derived as rawInput - actualPrompt
 *   - compressedTokens: compressedInputTokensEstimated
 *   - overheadTokens: derived as compressedTotal - (prompt + system + compressed)
 *   - baselineTokens: rawInputTokensEstimated
 *
 * @param turns - API turn metrics
 * @returns Chart data points
 */
export function transformTurnsToChartData(
  turns: ConversationTurnMetricDto[],
): ChartDataPoint[] {
  return turns.map((turn) => {
    const promptTokens = turn.actualPromptTokens ?? 0;
    const systemTokens = Math.max(0, turn.rawInputTokensEstimated - promptTokens);
    const compressedTokens = turn.compressedInputTokensEstimated ?? 0;
    const totalCompressed = turn.compressedTotalTokensEstimated;
    const overheadTokens = Math.max(
      0,
      totalCompressed - (promptTokens + systemTokens + compressedTokens),
    );
    const baselineTokens = turn.rawInputTokensEstimated;

    return {
      turnIndex: turn.turnIndex,
      model: turn.model,
      promptTokens,
      systemTokens,
      compressedTokens,
      overheadTokens,
      baselineTokens,
      workingMemoryVersion: turn.workingMemoryVersionUsed,
      totalCompressed,
      netTokensSaved: turn.netTokensSaved,
      savingsRatio: turn.netTokenSavingsRatio,
      softBudgetExceeded: turn.softBudgetExceeded,
      hardBudgetExceeded: turn.hardBudgetExceeded,
    };
  });
}
